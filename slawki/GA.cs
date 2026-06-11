using System.Numerics;
using Raylib_cs;
using System.Xml.Serialization;
using System.Collections.Concurrent;

namespace Slawki;

public enum RozmiarAutobusu { Duzy, Sredni }

public class Osobnik
{
    public List<Linia> Linie = new();
    public float Fitness = float.MinValue;
    [XmlIgnore] public Dictionary<string, float>? SkladoweKary = null;
    public Osobnik() { }
    public Osobnik(List<Linia> linie) => Linie = linie;
}

public class Linia
{
    public List<int> Przystanki = new();
    public List<int> Odjazdy = new();
    public RozmiarAutobusu Rozmiar = RozmiarAutobusu.Duzy;
}

public static class SiecPrzystankow
{
    public static float[,] CzasyPrzejazdu = null!;
    public static List<(int roadIdx, bool forward)>[,] Sciezki = null!;
    public static int LiczbaPrzystankow;

    public static void Buduj(Mapa mapa)
    {
        var punkty = mapa.punkty;
        var przystanki = mapa.przystanki;
        int nP = punkty.Count, nS = przystanki.Count;
        LiczbaPrzystankow = nS;
        int total = nP + nS;

        Vector2[] pozycje = new Vector2[total];
        for (int i = 0; i < nP; i++) pozycje[i] = punkty[i];
        for (int i = 0; i < nS; i++) pozycje[nP + i] = przystanki[i].pos;

        var sasiedzi = new List<(int to, float waga, int roadIdx)>[total];
        for (int i = 0; i < total; i++) sasiedzi[i] = new();

        float Odl2Min(Vector2 a, Vector2 b) => Vector2.Distance(a, b) * 19.2f;

        for (int roadIdx = 0; roadIdx < mapa.drogi.Count; roadIdx++)
        {
            var droga = mapa.drogi[roadIdx];
            int a = droga.a, b = droga.b;
            Vector2 A = punkty[a], B = punkty[b];

            var stopList = new List<(int globalIdx, Vector2 pos, float offset)>();
            for (int s = 0; s < nS; s++)
                if (przystanki[s].index == roadIdx)
                    stopList.Add((nP + s, przystanki[s].pos, przystanki[s].offset));
            stopList.Sort((x, y) => x.offset.CompareTo(y.offset));

            int prev = a;
            foreach (var (idx, pos, _) in stopList)
            {
                float dist = Odl2Min(pozycje[prev], pos);
                sasiedzi[prev].Add((idx, dist, roadIdx));
                sasiedzi[idx].Add((prev, dist, roadIdx));
                prev = idx;
            }
            float distB = Odl2Min(pozycje[prev], B);
            sasiedzi[prev].Add((b, distB, roadIdx));
            sasiedzi[b].Add((prev, distB, roadIdx));
        }

        CzasyPrzejazdu = new float[nS, nS];
        Sciezki = new List<(int, bool)>[nS, nS];
        for (int i = 0; i < nS; i++)
            for (int j = 0; j < nS; j++)
                Sciezki[i, j] = new();

        for (int startIdx = 0; startIdx < nS; startIdx++)
        {
            int startNode = nP + startIdx;
            float[] dist = new float[total];
            Array.Fill(dist, float.PositiveInfinity);
            int[] prevNode = new int[total];
            int[] prevRoad = new int[total];
            bool[] prevForward = new bool[total];
            Array.Fill(prevNode, -1);

            var Q = new SortedSet<(float d, int v)>();
            dist[startNode] = 0;
            Q.Add((0, startNode));

            while (Q.Count > 0)
            {
                var (d, u) = Q.Min;
                Q.Remove(Q.Min);
                if (d > dist[u]) continue;
                foreach (var (v, w, roadIdx) in sasiedzi[u])
                {
                    float nd = d + w;
                    if (nd < dist[v])
                    {
                        dist[v] = nd;
                        prevNode[v] = u;
                        prevRoad[v] = roadIdx;
                        bool forward = false;
                        if (u < nP && v < nP)
                        {
                            var droga = mapa.drogi[roadIdx];
                            forward = (u == droga.a && v == droga.b);
                        }
                        else if (u < nP && v >= nP)
                        {
                            var droga = mapa.drogi[roadIdx];
                            forward = (u == droga.a);
                        }
                        else if (u >= nP && v < nP)
                        {
                            var droga = mapa.drogi[roadIdx];
                            forward = (v == droga.b);
                        }
                        else forward = prevForward[u];
                        prevForward[v] = forward;
                        Q.Add((nd, v));
                    }
                }
            }

            for (int endIdx = 0; endIdx < nS; endIdx++)
            {
                int endNode = nP + endIdx;
                CzasyPrzejazdu[startIdx, endIdx] = dist[endNode];
                if (float.IsInfinity(dist[endNode])) continue;

                var path = new List<(int roadIdx, bool forward)>();
                int cur = endNode;
                while (prevNode[cur] != -1)
                {
                    path.Add((prevRoad[cur], prevForward[cur]));
                    cur = prevNode[cur];
                }
                path.Reverse();
                Sciezki[startIdx, endIdx] = path;
            }
        }
    }
}

internal class OptymalizatorRozkladu : Sekcja, ICmdHandler
{
    public static readonly Color[] Paleta = {
        Color.Red, Color.Blue, Color.Green, Color.Orange, Color.Yellow,
        Color.Purple, Color.Pink, Color.SkyBlue, Color.Lime, Color.Gold
    };

    private Mapa mapa;
    private List<Osobnik> populacja = new();
    private Osobnik? najlepszy = null;
    private Osobnik? rekord = null;
    private Random rng = new();
    private bool dziala = false;
    private int generacja = 0;
    private List<float> historiaFitness = new();
    private bool pokazWykres = true;

    private string aktywnaStrategia = "mutation";
    private int generacjeStagnacji = 0;
    private int strategiaKroki = 0;
    private bool autoFazaMutacji = true;
    private int autoLicznikFazy = 0;
    private int oryginalnaElita;
    private int mnoznikPopulacji = 1;
    private float oryginalnaMutacja;
    private int oryginalnyRozmiarPopulacjiDlaElit = 0;

    private const float KOSZT_DUZY_JEDNORAZ = 2_400_000f;
    private const float KOSZT_SREDNI_JEDNORAZ = 900_000f;
    private const float KOSZT_DUZY_DZIEN = 408.70f;
    private const float KOSZT_SREDNI_DZIEN = 315.00f;
    private const float SKALA_KM = 8.0f;

    private int ROZMIAR_POPULACJI = 80;
    private int ELITA = 2;
    private float MUTACJA_BAZOWA = 0.25f;
    private float MUTACJA_SZANSA = 0.25f;
    private float MUTACJA_PRZYROST = 0.05f;
    private int MAKS_LINII = 15;
    private int MIN_PRZYSTANKOW = 2;
    private int MAKS_PRZYSTANKOW = 15;
    private int MIN_ODJAZDOW = 4;
    private int MAKS_ODJAZDOW = 15;
    private float SZANSA_SWAP = 0.1f;
    private float WAGA_DLUGOSC_TRASY = 0.1f;
    private float WAGA_FLOTA = 5f;
    private float KARA_NIESPELNIONE = 1000f;
    private float WAGA_DUPLIKATY_DROG = 100f;
    private float WAGA_ZAWRACANIE = 200f;
    private float WAGA_LICZBA_LINII = 1000f;
    private float WAGA_KOSZT = 0.001f;
    private int POJEMNOSC_DUZY = 60;
    private int POJEMNOSC_SREDNI = 40;
    private int PROG_SREDNI = 5;
    private int CEL_LINII = 7;
    private float MAKS_DYSTANS_KM = 30f;
    private float KARA_ZA_KM_NADMIAR = 50000f;
    private float KARA_NIEKRYCIE_PRZYSTANKU = 500000f;
    private float KARA_BRAK_POLACZENIA = 10000f;
    private float KARA_PRZEKROCZENIE_CZASU_SZKOLA = 5000f;
    private float KARA_PRZEKROCZENIE_CZASU_PRACA = 5000f;



    private float[,] czasyMiedzyPrzystankami = null!;
    private List<(int roadIdx, bool forward)>[,] sciezkiMiedzyPrzystankami = null!;
    private int ostatniaWersjaMapy = -1;

    private List<int> domyId = new();
    private List<int> centraId = new();
    private List<int> szkolyId = new();
    private List<int> pracaId = new();

    private int[,] popytSzkola = null!;
    private int[,] popytPraca = null!;

    private Rectangle[] liniaPrzyciski = Array.Empty<Rectangle>();
    private int podswietlonaLinia = -1;
    private List<(int liniaIdx, bool forward, List<int> czasy)>[] rozklad = null!;

    private int liczbaDuplikatow, liczbaZawracan;
    private float nadmiarKm;
    private int niepokrytePrzystanki, brakPolaczen;

    public override void mouseDown(Punkt point, MouseButton mb) { }
    public override void mouseUp(Punkt point, MouseButton mb) { }
    public override void mouseMove(Punkt delta) { }
    public override void mouseScroll(Punkt point, float delta) { }

    public OptymalizatorRozkladu(Punkt pos, Punkt size, Mapa mapa) : base(pos, size)
    {
        this.mapa = mapa;
        oryginalnaElita = ELITA;
        oryginalnaMutacja = MUTACJA_SZANSA;
        AktualizujSieci();
    }

    private void AktualizujSieci()
    {
        int wersja = mapa.przystanki.Count * 10000 + mapa.drogi.Count;
        if (wersja == ostatniaWersjaMapy) return;

        SiecPrzystankow.Buduj(mapa);
        czasyMiedzyPrzystankami = SiecPrzystankow.CzasyPrzejazdu;
        sciezkiMiedzyPrzystankami = SiecPrzystankow.Sciezki;
        ostatniaWersjaMapy = wersja;

        domyId.Clear(); centraId.Clear(); szkolyId.Clear(); pracaId.Clear();
        for (int i = 0; i < mapa.przystanki.Count; i++)
        {
            switch (mapa.przystanki[i].typ)
            {
                case Mapa.Przystanek.typPrzystanku.normal: domyId.Add(i); break;
                case Mapa.Przystanek.typPrzystanku.center: centraId.Add(i); break;
                case Mapa.Przystanek.typPrzystanku.school: szkolyId.Add(i); break;
                case Mapa.Przystanek.typPrzystanku.work: pracaId.Add(i); break;
            }
        }

        popytSzkola = new int[domyId.Count, szkolyId.Count];
        popytPraca = new int[domyId.Count, pracaId.Count];
        for (int d = 0; d < domyId.Count; d++)
        {
            for (int s = 0; s < szkolyId.Count; s++) popytSzkola[d, s] = 5;
            for (int p = 0; p < pracaId.Count; p++) popytPraca[d, p] = 5;
        }
    }

    public void InicjalizujPopulacje()
    {
        AktualizujSieci();
        if (mapa.przystanki.Count < MIN_PRZYSTANKOW)
        {
            Console.WriteLine($"Za malo przystankow - potrzeba co najmniej {MIN_PRZYSTANKOW}.");
            return;
        }

        populacja.Clear();
        historiaFitness.Clear();
        generacjeStagnacji = 0;
        strategiaKroki = 0;
        autoFazaMutacji = true;
        autoLicznikFazy = 0;
        MUTACJA_SZANSA = MUTACJA_BAZOWA;
        ELITA = oryginalnaElita;
        oryginalnyRozmiarPopulacjiDlaElit = 0;
        var opcje = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        var torba = new ConcurrentBag<Osobnik>();
        Parallel.For(0, ROZMIAR_POPULACJI, opcje, i =>
        {
            var lokalnyRng = new Random(Guid.NewGuid().GetHashCode());
            var os = LosowyOsobnik(lokalnyRng);
            os.Fitness = ObliczFitness(os);
            torba.Add(os);
        });
        populacja = torba.ToList();
        UaktualnijNajlepszego();
    }

    private void InicjalizujPopulacjeZRekordu()
    {
        if (rekord == null) return;
        AktualizujSieci();
        populacja.Clear();
        historiaFitness.Clear();
        generacjeStagnacji = 0;
        strategiaKroki = 0;
        autoFazaMutacji = true;
        autoLicznikFazy = 0;
        MUTACJA_SZANSA = MUTACJA_BAZOWA;
        ELITA = oryginalnaElita;
        oryginalnyRozmiarPopulacjiDlaElit = 0;

        var opcje = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        var torba = new ConcurrentBag<Osobnik>();
        var kopiaRekordu = new Osobnik(rekord.Linie.Select(Klonuj).ToList()) { Fitness = rekord.Fitness };
        torba.Add(kopiaRekordu);
        Parallel.For(1, ROZMIAR_POPULACJI, opcje, i =>
        {
            var lokalnyRng = new Random(Guid.NewGuid().GetHashCode());
            var os = new Osobnik(rekord.Linie.Select(Klonuj).ToList());
            Mutuj(os, lokalnyRng, 0.4f);
            os.Fitness = ObliczFitness(os);
            torba.Add(os);
        });
        populacja = torba.ToList();
        najlepszy = kopiaRekordu;
        Console.WriteLine("Populacja odtworzona z rekordu.");
        AktualizujRozklad();
    }

    private void UaktualnijNajlepszego()
    {
        var nowy = populacja.MaxBy(o => o.Fitness);
        if (nowy != null)
        {
            bool poprawa = (najlepszy != null && nowy.Fitness > najlepszy.Fitness);
            if (poprawa)
            {
                generacjeStagnacji = 0;
                strategiaKroki = 0;
                autoFazaMutacji = true;
                autoLicznikFazy = 0;
                mnoznikPopulacji = 1;
                MUTACJA_SZANSA = MUTACJA_BAZOWA;
                ELITA = oryginalnaElita;
                oryginalnyRozmiarPopulacjiDlaElit = 0;

            }
            else
            {
                generacjeStagnacji++;
                if (generacjeStagnacji >= 5 && strategiaKroki == 0)
                {
                    ZastosujStrategie();
                }
                if (generacjeStagnacji >= 50 && aktywnaStrategia == "auto")
                {
                    mnoznikPopulacji = 4;
                }
                if (generacjeStagnacji >= 90 && aktywnaStrategia == "auto")
                {
                    mnoznikPopulacji = 8;
                    MUTACJA_SZANSA = MUTACJA_BAZOWA*1.5f;
                }
                if (generacjeStagnacji >= 100 && aktywnaStrategia == "auto")
                {
                    if (ELITA != 0)
                        oryginalnaElita = ELITA;
                    ELITA = 0;
                }
            }

            if (strategiaKroki > 0)
            {
                strategiaKroki--;
                if (strategiaKroki == 0)
                {
                    ELITA = oryginalnaElita;
                    MUTACJA_SZANSA = MUTACJA_BAZOWA;
                    if (oryginalnyRozmiarPopulacjiDlaElit != 0)
                    {
                        ROZMIAR_POPULACJI = oryginalnyRozmiarPopulacjiDlaElit;
                        oryginalnyRozmiarPopulacjiDlaElit = 0;
                    }
                }
            }

            najlepszy = nowy;
            if (rekord == null || najlepszy.Fitness > rekord.Fitness)
                rekord = new Osobnik(najlepszy.Linie.Select(Klonuj).ToList()) { Fitness = najlepszy.Fitness };
        }
        if (najlepszy != null) historiaFitness.Add(najlepszy.Fitness);
        if (historiaFitness.Count > 2000) historiaFitness.RemoveAt(0);
        AktualizujRozklad();
    }

    private void ZastosujStrategie()
    {
        switch (aktywnaStrategia)
        {
            case "brak": break;
            case "elit":
                oryginalnyRozmiarPopulacjiDlaElit = ROZMIAR_POPULACJI;
                ROZMIAR_POPULACJI *= 4;
                ELITA = 0;
                strategiaKroki = 5;
                break;
            case "mutation":
                MUTACJA_SZANSA = 0.6f;
                strategiaKroki = 5;
                break;
            case "auto":
                if (autoFazaMutacji)
                {
                    MUTACJA_SZANSA = Math.Min(0.6f, MUTACJA_BAZOWA + autoLicznikFazy * 0.05f);
                    strategiaKroki = 3;
                    autoLicznikFazy++;
                    if (autoLicznikFazy >= 10)
                    {
                        autoFazaMutacji = false;
                        autoLicznikFazy = 0;
                    }
                }
                else
                {
                    // ELITA = 0;
                    strategiaKroki = 3;
                    autoLicznikFazy++;
                    if (autoLicznikFazy >= 5)
                    {
                        autoFazaMutacji = true;
                        autoLicznikFazy = 0;
                    }
                }
                break;
        }
    }

    public Osobnik? GetAktywnyOsobnik() => dziala ? najlepszy : rekord;

    private void AktualizujRozklad()
    {
        var os = GetAktywnyOsobnik();
        if (os == null) { rozklad = null; return; }
        int n = mapa.przystanki.Count;
        rozklad = new List<(int, bool, List<int>)>[n];
        for (int i = 0; i < n; i++) rozklad[i] = new();

        for (int l = 0; l < os.Linie.Count; l++)
        {
            var lin = os.Linie[l];
            float czasT = CzasTrasy(lin.Przystanki);
            foreach (int t in lin.Odjazdy)
            {
                float czas = t;
                for (int i = 0; i < lin.Przystanki.Count; i++)
                {
                    int s = lin.Przystanki[i];
                    int minuta = ((int)czas % 1440 + 1440) % 1440;
                    if (i == 0) DodajOdjazd(s, l, true, minuta);
                    else
                    {
                        czas += czasyMiedzyPrzystankami[lin.Przystanki[i - 1], s];
                        minuta = ((int)czas % 1440 + 1440) % 1440;
                        DodajOdjazd(s, l, true, minuta);
                    }
                }
                int postoj = rng.Next(0, 10);
                czas = t + czasT + postoj;
                var odwrocona = Enumerable.Reverse(lin.Przystanki).ToList();
                for (int i = 0; i < odwrocona.Count; i++)
                {
                    int s = odwrocona[i];
                    int minuta = ((int)czas % 1440 + 1440) % 1440;
                    if (i == 0) DodajOdjazd(s, l, false, minuta);
                    else
                    {
                        czas += czasyMiedzyPrzystankami[odwrocona[i - 1], s];
                        minuta = ((int)czas % 1440 + 1440) % 1440;
                        DodajOdjazd(s, l, false, minuta);
                    }
                }
            }
        }
        for (int i = 0; i < n; i++)
            foreach (var grupa in rozklad[i])
                grupa.czasy.Sort();
    }

    private void DodajOdjazd(int stopIdx, int liniaIdx, bool forward, int czas)
    {
        var lista = rozklad[stopIdx];
        foreach (var grupa in lista)
        {
            if (grupa.liniaIdx == liniaIdx && grupa.forward == forward)
            {
                if (!grupa.czasy.Contains(czas)) grupa.czasy.Add(czas);
                return;
            }
        }
        lista.Add((liniaIdx, forward, new List<int> { czas }));
    }

    private Osobnik LosowyOsobnik(Random rng)
    {
        int n = rng.Next(1, MAKS_LINII + 1);
        var linie = new List<Linia>();
        for (int i = 0; i < n; i++) linie.Add(LosowaLinia(rng));
        return new Osobnik(linie);
    }

    private float DystansTrasy(List<int> przystanki)
    {
        float czas = 0;
        for (int i = 0; i < przystanki.Count - 1; i++)
            czas += czasyMiedzyPrzystankami[przystanki[i], przystanki[i + 1]];
        return czas / 60f * 25f;
    }

    private Linia LosowaLinia(Random rng)
    {
        int dostepne = mapa.przystanki.Count;
        int obecny = rng.Next(dostepne);
        var odwiedzone = new HashSet<int> { obecny };
        var trasa = new List<int> { obecny };
        float limitDystansu = MAKS_DYSTANS_KM * 0.8f;
        int celDlugosc = rng.Next(MIN_PRZYSTANKOW, Math.Min(MAKS_PRZYSTANKOW, dostepne) + 1);

        while (trasa.Count < celDlugosc && DystansTrasy(trasa) < limitDystansu)
        {
            var kandydaci = Enumerable.Range(0, dostepne)
                .Where(x => !odwiedzone.Contains(x) && !float.IsInfinity(czasyMiedzyPrzystankami[obecny, x]))
                .ToList();
            if (kandydaci.Count == 0) break;
            int najlepszyKandydat = kandydaci.OrderBy(x => czasyMiedzyPrzystankami[obecny, x]).First();
            obecny = najlepszyKandydat;
            odwiedzone.Add(obecny);
            trasa.Add(obecny);
        }
        int odj = rng.Next(MIN_ODJAZDOW, MAKS_ODJAZDOW + 1);
        var odjazdy = new List<int>();
        for (int i = 0; i < odj; i++) odjazdy.Add(rng.Next(0, 1440));
        odjazdy.Sort();
        var linia = new Linia { Przystanki = trasa, Odjazdy = odjazdy };
        linia.Rozmiar = trasa.Count > PROG_SREDNI ? RozmiarAutobusu.Duzy : RozmiarAutobusu.Sredni;
        return linia;
    }

    private float ObliczFitness(Osobnik os)
    {
        if (os.Linie.Count == 0) return float.MinValue;

        var kursy = GenerujKursy(os);
        var direct = BuildDirectConnections(kursy);

        float karaTrasa = 0;
        float dzienneKm = 0;
        float karaMaxDystans = 0;
        liczbaDuplikatow = 0;
        liczbaZawracan = 0;
        nadmiarKm = 0;

        foreach (var lin in os.Linie)
        {
            float czas = 0;
            float dystans = 0;
            for (int i = 0; i < lin.Przystanki.Count - 1; i++)
            {
                float segCzas = czasyMiedzyPrzystankami[lin.Przystanki[i], lin.Przystanki[i + 1]];
                czas += segCzas;
                dystans += segCzas / 60f * 25f;
            }
            karaTrasa += czas * WAGA_DLUGOSC_TRASY;
            dzienneKm += dystans;
            if (dystans > MAKS_DYSTANS_KM)
            {
                float nadm = dystans - MAKS_DYSTANS_KM;
                nadmiarKm += nadm;
                karaMaxDystans += nadm * KARA_ZA_KM_NADMIAR;
            }

            var drogi = new List<(int roadIdx, bool forward)>();
            for (int i = 0; i < lin.Przystanki.Count - 1; i++)
            {
                int s = lin.Przystanki[i], e = lin.Przystanki[i + 1];
                drogi.AddRange(sciezkiMiedzyPrzystankami[s, e]);
            }
            var licznik = new Dictionary<int, int>();
            foreach (var (r, _) in drogi)
                licznik[r] = licznik.TryGetValue(r, out int v) ? v + 1 : 1;
            foreach (var c in licznik.Values)
                if (c > 1) liczbaDuplikatow += (c - 1);
            karaTrasa += liczbaDuplikatow * WAGA_DUPLIKATY_DROG;

            for (int i = 0; i < drogi.Count - 1; i++)
                if (drogi[i].roadIdx == drogi[i + 1].roadIdx && drogi[i].forward != drogi[i + 1].forward)
                    liczbaZawracan++;
            karaTrasa += liczbaZawracan * WAGA_ZAWRACANIE;
        }

        var pokryte = new HashSet<int>();
        foreach (var lin in os.Linie) foreach (int p in lin.Przystanki) pokryte.Add(p);
        niepokrytePrzystanki = 0;
        for (int i = 0; i < mapa.przystanki.Count; i++) if (!pokryte.Contains(i)) niepokrytePrzystanki++;
        float karaNiepokryte = niepokrytePrzystanki * KARA_NIEKRYCIE_PRZYSTANKU;

        int totalStops = mapa.przystanki.Count;
        brakPolaczen = 0;
        for (int i = 0; i < totalStops; i++)
        {
            for (int j = i + 1; j < totalStops; j++)
            {
                bool ok = false;
                if (direct.ContainsKey((i, j)) || direct.ContainsKey((j, i))) { ok = true; }
                else
                {
                    foreach (var (from, to) in direct.Keys)
                    {
                        if (from == i && direct.ContainsKey((to, j))) { ok = true; break; }
                        if (from == j && direct.ContainsKey((to, i))) { ok = true; break; }
                    }
                }
                if (!ok) brakPolaczen++;
            }
        }
        float karaPolaczenia = brakPolaczen * KARA_BRAK_POLACZENIA;

        int flotaDuzych = 0, flotaSrednich = 0;
        foreach (var lin in os.Linie)
        {
            float obieg = 2 * CzasTrasy(lin.Przystanki) + 2;
            if (lin.Odjazdy.Count == 0) continue;
            var zajete = new List<float>();
            int max = 0;
            foreach (int t in lin.Odjazdy)
            {
                zajete.RemoveAll(x => x <= t);
                zajete.Add(t + obieg);
                if (zajete.Count > max) max = zajete.Count;
            }
            if (lin.Rozmiar == RozmiarAutobusu.Duzy) flotaDuzych += max;
            else flotaSrednich += max;
        }
        float kosztJednoraz = flotaDuzych * KOSZT_DUZY_JEDNORAZ + flotaSrednich * KOSZT_SREDNI_JEDNORAZ;
        float kosztDzienny = dzienneKm / 100f * (flotaDuzych * KOSZT_DUZY_DZIEN + flotaSrednich * KOSZT_SREDNI_DZIEN);
        float kosztRoczny = kosztDzienny * 365f;
        float karaFlota = flotaDuzych * WAGA_FLOTA + flotaSrednich * WAGA_FLOTA * 0.7f;

        float karaObsluga = 0;
        float calkowityCzas = 0;
        foreach (int dIdx in domyId)
        {
            for (int sIdx = 0; sIdx < szkolyId.Count; sIdx++)
            {
                int szkola = szkolyId[sIdx];
                int popyt = popytSzkola[domyId.IndexOf(dIdx), sIdx];
                var (czas, przesiadki) = BestTravelTime(direct, dIdx, szkola, 0, 1440);
                if (czas == null || przesiadki > 1)
                    karaObsluga += popyt * KARA_NIESPELNIONE;
                else
                {
                    calkowityCzas += popyt * czas.Value;
                    if (przesiadki == 1) karaObsluga += popyt * 10;
                    if (czas.Value > 60) karaObsluga += popyt * KARA_PRZEKROCZENIE_CZASU_SZKOLA;
                }
            }
            for (int pIdx = 0; pIdx < pracaId.Count; pIdx++)
            {
                int praca = pracaId[pIdx];
                int popyt = popytPraca[domyId.IndexOf(dIdx), pIdx];
                var (czas, przesiadki) = BestTravelTime(direct, dIdx, praca, 0, 1440);
                if (czas == null || przesiadki > 1)
                    karaObsluga += popyt * KARA_NIESPELNIONE;
                else
                {
                    calkowityCzas += popyt * czas.Value;
                    if (przesiadki == 1) karaObsluga += popyt * 10;
                    if (czas.Value > 60) karaObsluga += popyt * KARA_PRZEKROCZENIE_CZASU_PRACA;
                }
            }
        }

        float karaLiczbaLinii = 0;
        if (os.Linie.Count > CEL_LINII)
        {
            karaLiczbaLinii = WAGA_LICZBA_LINII * (os.Linie.Count - CEL_LINII);
        }
        if (os.Linie.Count < CEL_LINII)
        {
            karaLiczbaLinii = WAGA_LICZBA_LINII * (CEL_LINII - os.Linie.Count);
        }


        float karaKoszt = (kosztJednoraz + kosztRoczny) * WAGA_KOSZT;

        OstatniKosztJednoraz = kosztJednoraz;
        OstatniKosztDzienny = kosztDzienny;

        return -(karaTrasa + karaFlota + karaObsluga + calkowityCzas * 0.01f + karaLiczbaLinii + karaKoszt
                + karaMaxDystans + karaNiepokryte + karaPolaczenia);
    }

    public static float OstatniKosztJednoraz = 0;
    public static float OstatniKosztDzienny = 0;

    private (float? czas, int przesiadki) BestTravelTime(
        Dictionary<(int, int), List<(float dep, float arr)>> direct,
        int from, int to, int minStart, int maxEnd)
    {
        if (direct.TryGetValue((from, to), out var list))
        {
            float? best = null;
            foreach (var (dep, arr) in list)
                if (dep >= minStart && arr <= maxEnd)
                    if (!best.HasValue || arr - dep < best.Value - dep) best = arr - dep;
            if (best.HasValue) return (best, 0);
        }
        float? bestOverall = null;
        foreach (var (f, t) in direct.Keys)
        {
            if (f == from)
            {
                int transfer = t;
                if (direct.TryGetValue((transfer, to), out var list2))
                {
                    foreach (var (dep1, arr1) in direct[(from, transfer)])
                    {
                        if (arr1 > maxEnd) continue;
                        foreach (var (dep2, arr2) in list2)
                        {
                            if (dep2 >= arr1 && arr2 <= maxEnd)
                            {
                                float total = arr2 - dep1;
                                if (!bestOverall.HasValue || total < bestOverall.Value)
                                    bestOverall = total;
                            }
                        }
                    }
                }
            }
        }
        return bestOverall.HasValue ? (bestOverall, 1) : (null, 0);
    }

    private Dictionary<(int, int), List<(float dep, float arr)>> BuildDirectConnections(
        List<List<(int stop, float arr, float dep)>> kursy)
    {
        var dict = new Dictionary<(int, int), List<(float, float)>>();
        foreach (var k in kursy)
            for (int i = 0; i < k.Count - 1; i++)
            {
                var key = (k[i].stop, k[i + 1].stop);
                if (!dict.ContainsKey(key)) dict[key] = new List<(float, float)>();
                dict[key].Add((k[i].dep, k[i + 1].arr));
            }
        return dict;
    }

    private float CzasTrasy(List<int> przystanki)
    {
        float c = 0;
        for (int i = 0; i < przystanki.Count - 1; i++)
            c += czasyMiedzyPrzystankami[przystanki[i], przystanki[i + 1]];
        return c;
    }


    private List<List<(int stop, float arr, float dep)>> GenerujKursy(Osobnik os)
    {
        var kursy = new List<List<(int, float, float)>>();
        foreach (var lin in os.Linie)
        {
            float czasT = CzasTrasy(lin.Przystanki);
            foreach (int t in lin.Odjazdy)
            {
                // Przód
                var stops = new List<(int, float, float)>();
                float czas = t;
                for (int i = 0; i < lin.Przystanki.Count; i++)
                {
                    int s = lin.Przystanki[i];
                    int minuta = ((int)czas % 1440 + 1440) % 1440;
                    if (i == 0) stops.Add((s, minuta, minuta));
                    else
                    {
                        czas += czasyMiedzyPrzystankami[lin.Przystanki[i - 1], s];
                        minuta = ((int)czas % 1440 + 1440) % 1440;
                        stops.Add((s, minuta, minuta));
                    }
                }
                kursy.Add(stops);

                // Powrót ze stałym postojem 2 minuty
                var revStops = new List<(int, float, float)>();
                czas = t + czasT + 2;
                var odwrocona = Enumerable.Reverse(lin.Przystanki).ToList();
                for (int i = 0; i < odwrocona.Count; i++)
                {
                    int s = odwrocona[i];
                    int minuta = ((int)czas % 1440 + 1440) % 1440;
                    if (i == 0) revStops.Add((s, minuta, minuta));
                    else
                    {
                        czas += czasyMiedzyPrzystankami[odwrocona[i - 1], s];
                        minuta = ((int)czas % 1440 + 1440) % 1440;
                        revStops.Add((s, minuta, minuta));
                    }
                }
                kursy.Add(revStops);
            }
        }
        return kursy;
    }
    private void PrzeliczFitnessPopulacji()
    {
        if (populacja.Count == 0) return;

        var opcje = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        Parallel.ForEach(populacja, opcje, os =>
        {
            os.Fitness = ObliczFitness(os);
        });

        UaktualnijNajlepszego();
    }

    public void WykonajKrokEwolucji(bool force = false)
    {
        if (!dziala && !force) return;
        if (populacja.Count == 0) { Console.WriteLine("Populacja pusta - wykonaj 'opt reset'"); return; }

        var nowa = new ConcurrentBag<Osobnik>();
        var posort = populacja.OrderByDescending(o => o.Fitness).ToList();
        for (int i = 0; i < ELITA; i++) nowa.Add(posort[i]);

        int doWygenerowania = ROZMIAR_POPULACJI*mnoznikPopulacji - ELITA;
        var opcje = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        float obecnaMutacja = MUTACJA_SZANSA;
        Parallel.For(0, doWygenerowania, opcje, i =>
        {
            var lokalnyRng = new Random(Guid.NewGuid().GetHashCode());
            var r1 = SelekcjaRankingowa(lokalnyRng);
            var r2 = SelekcjaRankingowa(lokalnyRng);
            var pot = Krzyzuj(r1, r2, lokalnyRng);

            Mutuj(pot, lokalnyRng, obecnaMutacja);


            while (rng.NextDouble() < obecnaMutacja)
            {
                Mutuj(pot, lokalnyRng, obecnaMutacja);
            }


            pot.Fitness = ObliczFitness(pot);
            nowa.Add(pot);
        });

        populacja = nowa.ToList();
        generacja++;
        UaktualnijNajlepszego();
    }

    private Osobnik SelekcjaRankingowa(Random rng)
    {
        var posort = populacja.OrderByDescending(o => o.Fitness).ToList();
        int n = posort.Count;
        double total = (double)n * (n + 1) / 2;
        double los = rng.NextDouble() * total;
        double suma = 0;
        for (int i = 0; i < n; i++)
        {
            suma += (n - i);
            if (los <= suma) return posort[i];
        }
        return posort.Last();
    }

    private Osobnik Krzyzuj(Osobnik a, Osobnik b, Random rng)
    {
        var noweLinie = new List<Linia>();
        int ile = Math.Min(a.Linie.Count, b.Linie.Count);
        for (int i = 0; i < ile; i++)
            noweLinie.Add(rng.NextDouble() < 0.5 ? Klonuj(a.Linie[i]) : Klonuj(b.Linie[i]));
        var reszta = a.Linie.Count > b.Linie.Count ? a.Linie.Skip(ile) : b.Linie.Skip(ile);
        foreach (var l in reszta) if (noweLinie.Count < MAKS_LINII) noweLinie.Add(Klonuj(l));
        return new Osobnik(noweLinie);
    }

    private Linia Klonuj(Linia l) => new() { Przystanki = new(l.Przystanki), Odjazdy = new(l.Odjazdy), Rozmiar = l.Rozmiar };

    private void Mutuj(Osobnik os, Random rng, float mutacja)
    {
        //nakierowujemy algorytm na cel przystanków
        float prob = 0.5f;
        if (os.Linie.Count > CEL_LINII)
        {
            prob -= ((float)(CEL_LINII - os.Linie.Count) / (float)(MAKS_PRZYSTANKOW - CEL_LINII)) / 2.0f;
        }
        if (os.Linie.Count < CEL_LINII)
        {
            prob += ((float)os.Linie.Count / (float)CEL_LINII) / 2.0f;
        }

        if (rng.NextDouble() < mutacja)//dodanie/usunięcie lini
        {
            if (os.Linie.Count < MAKS_LINII && rng.NextDouble() < prob)
                os.Linie.Add(LosowaLinia(rng));
            else if (os.Linie.Count > 1)
                os.Linie.RemoveAt(rng.Next(os.Linie.Count));
        }
        if (rng.NextDouble() < SZANSA_SWAP)//podmianka lini
        {
            os.Linie.Add(LosowaLinia(rng));
            os.Linie.RemoveAt(rng.Next(os.Linie.Count));
        }
        foreach (var lin in os.Linie)
        {
            if (rng.NextDouble() < mutacja)//dodanie/usinięcie przystanku
            {

                if ((lin.Przystanki.Count > MIN_PRZYSTANKOW && rng.NextDouble() < mutacja) || DystansTrasy(lin.Przystanki) > MAKS_DYSTANS_KM * 0.8)
                    UsunPrzystanek(lin, rng);
                else if (lin.Przystanki.Count < MAKS_PRZYSTANKOW)
                    DodajPrzystanek(lin, rng);
                lin.Rozmiar = lin.Przystanki.Count > PROG_SREDNI ? RozmiarAutobusu.Duzy : RozmiarAutobusu.Sredni;
            }
            if (rng.NextDouble() < mutacja)//przesunięcie odjazdu
            {
                int n = rng.Next(lin.Odjazdy.Count);
                lin.Odjazdy[n] += (rng.Next(200) - 100) + 1440;

                lin.Odjazdy[n] = lin.Odjazdy[n] % 1440;
                lin.Odjazdy.Sort();
            }
            if (rng.NextDouble() < mutacja)//dodanie/usunięcie odjazdu
            {

                if (lin.Odjazdy.Count > 1 && rng.NextDouble() < 0.5f)
                    lin.Odjazdy.RemoveAt(rng.Next(lin.Odjazdy.Count));
                else if (lin.Odjazdy.Count < MAKS_ODJAZDOW)
                {
                    int n = rng.Next(1440);
                    if (!lin.Odjazdy.Contains(n)) { lin.Odjazdy.Add(n); lin.Odjazdy.Sort(); }
                }
            }
            if (rng.NextDouble() < SZANSA_SWAP && lin.Przystanki.Count >= 4)//zamiana przystanków
            {
                int i1 = rng.Next(1, lin.Przystanki.Count - 2), i2 = rng.Next(i1 + 1, lin.Przystanki.Count - 1);
                int a = lin.Przystanki[i1 - 1], b = lin.Przystanki[i1], c = lin.Przystanki[i1 + 1];
                int d = lin.Przystanki[i2 - 1], e = lin.Przystanki[i2], f = lin.Przystanki[i2 + 1];
                if (!float.IsInfinity(czasyMiedzyPrzystankami[a, e]) && !float.IsInfinity(czasyMiedzyPrzystankami[e, c]) &&
                    !float.IsInfinity(czasyMiedzyPrzystankami[d, b]) && !float.IsInfinity(czasyMiedzyPrzystankami[b, f]))
                { int tmp = lin.Przystanki[i1]; lin.Przystanki[i1] = lin.Przystanki[i2]; lin.Przystanki[i2] = tmp; }
            }

        }
    }

    private void UsunPrzystanek(Linia lin, Random rng)
    {
        if (lin.Przystanki.Count <= 2) return;
        var wewn = Enumerable.Range(1, lin.Przystanki.Count - 2).OrderBy(_ => rng.Next()).ToList();
        foreach (int idx in wewn)
        {
            int prev = lin.Przystanki[idx - 1], next = lin.Przystanki[idx + 1];
            if (!float.IsInfinity(czasyMiedzyPrzystankami[prev, next])) { lin.Przystanki.RemoveAt(idx); return; }
        }
    }

    private void DodajPrzystanek(Linia lin, Random rng)
    {
        if (lin.Przystanki.Count >= MAKS_PRZYSTANKOW) return;
        var istnieje = new HashSet<int>(lin.Przystanki);
        float aktualnyDystans = DystansTrasy(lin.Przystanki);

        var kandydaci = new List<(int idx, float dodatkowyDystans)>();
        foreach (int s in lin.Przystanki)
        {
            for (int i = 0; i < mapa.przystanki.Count; i++)
            {
                if (istnieje.Contains(i) || float.IsInfinity(czasyMiedzyPrzystankami[s, i])) continue;
                float dodatkowy = czasyMiedzyPrzystankami[s, i] / 60f * 25f;
                if (aktualnyDystans + dodatkowy <= MAKS_DYSTANS_KM)
                    kandydaci.Add((i, dodatkowy));
            }
        }
        if (kandydaci.Count == 0) return;

        int nowy = kandydaci[rng.Next(kandydaci.Count)].idx;
        int najlepszaPozycja = -1; float najlepszyKoszt = float.PositiveInfinity;
        for (int poz = 0; poz <= lin.Przystanki.Count; poz++)
        {
            int przed = poz > 0 ? lin.Przystanki[poz - 1] : -1, po = poz < lin.Przystanki.Count ? lin.Przystanki[poz] : -1;
            float koszt = 0; bool ok = true;
            if (przed >= 0)
            {
                if (float.IsInfinity(czasyMiedzyPrzystankami[przed, nowy])) ok = false;
                else koszt += czasyMiedzyPrzystankami[przed, nowy] / 60f * 25f;
            }
            if (po >= 0)
            {
                if (float.IsInfinity(czasyMiedzyPrzystankami[nowy, po])) ok = false;
                else koszt += czasyMiedzyPrzystankami[nowy, po] / 60f * 25f;
            }
            if (ok && koszt < najlepszyKoszt) { najlepszyKoszt = koszt; najlepszaPozycja = poz; }
        }
        if (najlepszaPozycja >= 0 && aktualnyDystans + najlepszyKoszt <= MAKS_DYSTANS_KM)
            lin.Przystanki.Insert(najlepszaPozycja, nowy);
    }


    public override void Draw()
    {
        var os = GetAktywnyOsobnik();
        if (os == null || os.Linie.Count == 0) return;

        float szerokoscLiniiEkran = mapa.scale / 120f;

        podswietlonaLinia = -1;
        Vector2 mouse = Raylib.GetMousePosition();
        for (int i = 0; i < liniaPrzyciski.Length; i++)
        {
            if (Raylib.CheckCollisionPointRec(mouse, liniaPrzyciski[i]))
            {
                podswietlonaLinia = i;
                break;
            }
        }

        var drogaUzycie = new Dictionary<int, List<(Color kolor, Vector2 startMapa, Vector2 endMapa, bool forward, int liniaIdx)>>();
        int startL = podswietlonaLinia >= 0 && podswietlonaLinia < os.Linie.Count ? podswietlonaLinia : 0;
        int endL = podswietlonaLinia >= 0 && podswietlonaLinia < os.Linie.Count ? podswietlonaLinia + 1 : os.Linie.Count;

        for (int l = startL; l < endL; l++)
        {
            var lin = os.Linie[l];
            Color kolor = Paleta[l % Paleta.Length];
            for (int i = 0; i < lin.Przystanki.Count - 1; i++)
            {
                int skad = lin.Przystanki[i], dokad = lin.Przystanki[i + 1];
                var sciezka = sciezkiMiedzyPrzystankami[skad, dokad];
                for (int step = 0; step < sciezka.Count; step++)
                {
                    var (roadIdx, fwd) = sciezka[step];
                    var droga = mapa.drogi[roadIdx];
                    Vector2 A = mapa.punkty[droga.a], B = mapa.punkty[droga.b];
                    Vector2 start = (step == 0) ? mapa.przystanki[skad].pos : (fwd ? A : B);
                    Vector2 end = (step == sciezka.Count - 1) ? mapa.przystanki[dokad].pos : (fwd ? B : A);
                    if (!drogaUzycie.ContainsKey(roadIdx))
                        drogaUzycie[roadIdx] = new List<(Color, Vector2, Vector2, bool, int)>();
                    drogaUzycie[roadIdx].Add((kolor, start, end, fwd, l));
                }
            }
        }

        foreach (var kv in drogaUzycie)
        {
            var grupy = kv.Value.GroupBy(x => x.Item4);
            foreach (var grupa in grupy)
            {
                var linie = grupa.OrderBy(x => x.Item5).ToList();
                for (int i = 0; i < linie.Count; i++)
                {
                    var (kolor, startMapa, endMapa, _, _) = linie[i];
                    Vector2 s = mapa.point2screen(startMapa).v2();
                    Vector2 e = mapa.point2screen(endMapa).v2();
                    Vector2 dir = Vector2.Normalize(e - s);
                    Vector2 normal = new(-dir.Y, dir.X);
                    float off = i * szerokoscLiniiEkran;
                    Vector2 przes = normal * off;
                    Raylib.DrawLineEx(s + przes, e + przes, szerokoscLiniiEkran, kolor);
                }
            }
        }

        RysujPanelLinii(os);
        RysujStatystyki();
        if (podswietlonaLinia >= 0 && podswietlonaLinia < os.Linie.Count)
            RysujStatystykiLinii(os.Linie[podswietlonaLinia], podswietlonaLinia);
        if (pokazWykres && historiaFitness.Count > 1)
            RysujWykresFitness();
    }

    private void RysujPanelLinii(Osobnik os)
    {
        if (os.Linie.Count == 0) return;
        float kafelek = 20;
        float odstep = 4;
        float panelWys = kafelek + 8;
        float panelSzer = os.Linie.Count * (kafelek + odstep) + 8;
        float panelX = pos.x + (size.x - panelSzer) / 2;
        float panelY = pos.y + size.y - panelWys - 10;

        Raylib.DrawRectangle((int)panelX, (int)panelY, (int)panelSzer, (int)panelWys, new Color(0, 0, 0, 200));
        liniaPrzyciski = new Rectangle[os.Linie.Count];

        for (int i = 0; i < os.Linie.Count; i++)
        {
            float x = panelX + 4 + i * (kafelek + odstep);
            float y = panelY + 4;
            var rect = new Rectangle(x, y, kafelek, kafelek);
            liniaPrzyciski[i] = rect;
            Color kol = Paleta[i % Paleta.Length];
            if (i == podswietlonaLinia && podswietlonaLinia < os.Linie.Count) kol = Color.White;
            Raylib.DrawRectangleRec(rect, kol);
            Raylib.DrawText(i.ToString(), (int)x + 5, (int)y + 2, 12, Color.Black);
        }
    }

    private void RysujStatystyki()
    {
        int x = 10, y = 10, odstep = 18;
        Raylib.DrawText($"Generacja: {generacja}", x, y, 18, Color.White); y += odstep;
        Raylib.DrawText($"Symulacja: {(dziala ? "ON" : "OFF")}", x, y, 18, dziala ? Color.Lime : Color.Gray); y += odstep;
        string elitaNote = aktywnaStrategia=="auto"?$"(Elita : {ELITA} Stag : {generacjeStagnacji})":"";
        Raylib.DrawText($"Strategia: {aktywnaStrategia} {elitaNote}" , x, y, 16, Color.Gold); y += odstep;
        var os = GetAktywnyOsobnik();
        if (os == null) return;

        float bestFit = os.Fitness;
        float avgFit = populacja.Count > 0 ? populacja.Average(o => o.Fitness) : 0;
        int liczbaLinii = os.Linie.Count;
        int flotaD = 0, flotaS = 0; float calkowityCzas = 0;

        foreach (var lin in os.Linie)
        {
            calkowityCzas += CzasTrasy(lin.Przystanki);
            float obieg = 2 * CzasTrasy(lin.Przystanki) + 2;
            if (lin.Odjazdy.Count > 0)
            {
                var zajeteDo = new List<float>(); int max = 0;
                foreach (int t in lin.Odjazdy)
                { zajeteDo.RemoveAll(czas2 => czas2 <= t); zajeteDo.Add(t + obieg); if (zajeteDo.Count > max) max = zajeteDo.Count; }
                if (lin.Rozmiar == RozmiarAutobusu.Duzy) flotaD += max; else flotaS += max;
            }
        }

        float kosztJednoraz = flotaD * KOSZT_DUZY_JEDNORAZ + flotaS * KOSZT_SREDNI_JEDNORAZ;
        float kosztDzienny = flotaD * KOSZT_DUZY_DZIEN + flotaS * KOSZT_SREDNI_DZIEN;
        float kosztRoczny = kosztDzienny * 365;

        y += 5;
        Raylib.DrawText($"Najlepszy fitness: {bestFit:F2}", x, y, 18, Color.Yellow); y += odstep;
        Raylib.DrawText($"Sredni fitness:   {avgFit:F2}", x, y, 18, Color.SkyBlue); y += odstep;
        Raylib.DrawText($"Liczba linii:     {liczbaLinii}", x, y, 18, Color.White); y += odstep;
        Raylib.DrawText($"Autobusy D/S:     {flotaD}/{flotaS}", x, y, 18, Color.Orange); y += odstep;
        Raylib.DrawText($"Koszt jednoraz:   {kosztJednoraz / 1e6:F1} mln zl", x, y, 18, Color.Orange); y += odstep;
        Raylib.DrawText($"Koszt roczny:     {kosztRoczny / 1e6:F1} mln zl/rok", x, y, 18, Color.Orange); y += odstep;
        Raylib.DrawText($"Czas tras:        {calkowityCzas:F1} min", x, y, 18, Color.White); y += odstep;

        float dupPen = liczbaDuplikatow * WAGA_DUPLIKATY_DROG;
        float zawPen = liczbaZawracan * WAGA_ZAWRACANIE;
        float dystPen = nadmiarKm * KARA_ZA_KM_NADMIAR;
        float niekPen = niepokrytePrzystanki * KARA_NIEKRYCIE_PRZYSTANKU;
        float polPen = brakPolaczen * KARA_BRAK_POLACZENIA;

        Raylib.DrawText($"Duplikaty drog:   {dupPen:F0} ({liczbaDuplikatow})", x, y, 16, Color.Red); y += odstep - 2;
        Raylib.DrawText($"Zawracania:       {zawPen:F0} ({liczbaZawracan})", x, y, 16, Color.Pink); y += odstep - 2;
        Raylib.DrawText($"Nadmiar dystansu: {dystPen:F0} ({nadmiarKm:F1} km)", x, y, 16, Color.Orange); y += odstep - 2;
        Raylib.DrawText($"Niepokryte przyst: {niekPen:F0} ({niepokrytePrzystanki})", x, y, 16, Color.Magenta); y += odstep - 2;
        Raylib.DrawText($"Brak polaczen:    {polPen:F0} ({brakPolaczen})", x, y, 16, Color.DarkBlue); y += odstep - 2;
    }

    private void RysujStatystykiLinii(Linia lin, int idx)
    {
        int x = (int)size.x - 200, y = 10, odstep = 18;
        float czas = 0;
        for (int i = 0; i < lin.Przystanki.Count - 1; i++)
            czas += czasyMiedzyPrzystankami[lin.Przystanki[i], lin.Przystanki[i + 1]];
        float dystans = czas / 60f * 25f;
        int flota = 0;
        float obieg = 2 * czas + 2;
        if (lin.Odjazdy.Count > 0)
        {
            var zajete = new List<float>(); int max = 0;
            foreach (int t in lin.Odjazdy)
            { zajete.RemoveAll(czas2 => czas2 <= t); zajete.Add(t + obieg); if (zajete.Count > max) max = zajete.Count; }
            flota = max;
        }

        Raylib.DrawText($"Linia {idx}", x, y, 18, Color.Yellow); y += odstep;
        Raylib.DrawText($"Przystanki: {lin.Przystanki.Count}", x, y, 18, Color.White); y += odstep;
        Raylib.DrawText($"Czas: {czas:F1} min", x, y, 18, Color.White); y += odstep;
        Raylib.DrawText($"Dystans: {dystans:F1} km", x, y, 18, Color.White); y += odstep;
        Raylib.DrawText($"Autobusy: {flota} ({lin.Rozmiar})", x, y, 18, Color.White); y += odstep;
        if (dystans > MAKS_DYSTANS_KM)
        {
            float nadmiar = dystans - MAKS_DYSTANS_KM;
            Raylib.DrawText($"NADMIAR: {nadmiar:F1} km!", x, y, 18, Color.Red);
        }
    }

    private void RysujWykresFitness()
    {
        int wykresX = (int)size.x - 220;
        int wykresY = (int)size.y - 120;
        int szer = 200, wys = 100;
        Raylib.DrawRectangle(wykresX, wykresY, szer, wys, new Color(0, 0, 0, 180));
        if (historiaFitness.Count < 2) return;

        float minFit = historiaFitness.Min();
        float maxFit = historiaFitness.Max();
        if (maxFit - minFit < 0.0001f) maxFit = minFit + 1;
        int count = Math.Min(historiaFitness.Count, 2000);
        float startIdx = historiaFitness.Count - count;
        for (int i = 1; i < count; i++)
        {
            float x1 = wykresX + (i - 1) * szer / (float)(count - 1);
            float y1 = wykresY + wys - (historiaFitness[(int)startIdx + i - 1] - minFit) / (maxFit - minFit) * wys;
            float x2 = wykresX + i * szer / (float)(count - 1);
            float y2 = wykresY + wys - (historiaFitness[(int)startIdx + i] - minFit) / (maxFit - minFit) * wys;
            Raylib.DrawLineV(new Vector2(x1, y1), new Vector2(x2, y2), Color.Lime);
        }
        Raylib.DrawText($"Fitness", wykresX + 2, wykresY + 2, 12, Color.White);
    }

    public void RysujRozkladPrzystanku(int indeksPrzystanku)
    {
        if (rozklad == null || indeksPrzystanku < 0 || indeksPrzystanku >= rozklad.Length) return;
        var grupy = rozklad[indeksPrzystanku];
        if (grupy.Count == 0) return;

        Vector2 pos = mapa.point2screen(mapa.przystanki[indeksPrzystanku].pos).v2();
        float fontSize = 12;
        int lineHeight = (int)(fontSize * 1.4f);
        var wiersze = new List<(string text, Color kolor)>();

        foreach (var (liniaIdx, forward, czasy) in grupy)
        {
            if (czasy.Count == 0) continue;
            var czasyStr = string.Join(" ", czasy.Select(c =>
            {
                int mm = ((c % 1440) + 1440) % 1440;
                return $"{mm / 60:D2}:{mm % 60:D2}";
            }));
            string kierunek = forward ? "->" : "<-";
            string text = $"L{liniaIdx}{kierunek} {czasyStr}";
            Color kolor = Paleta[liniaIdx % Paleta.Length];
            wiersze.Add((text, kolor));
        }
        if (wiersze.Count == 0) return;

        float maxWidth = 0;
        foreach (var (text, _) in wiersze)
        {
            float w = Raylib.MeasureText(text, (int)fontSize);
            if (w > maxWidth) maxWidth = w;
        }

        float boxX = pos.X + 10;
        float boxY = pos.Y - (wiersze.Count * lineHeight) * 0.5f;
        float padding = 3;
        Raylib.DrawRectangle((int)(boxX - padding), (int)(boxY - padding),
            (int)(maxWidth + 2 * padding), (int)(wiersze.Count * lineHeight + 2 * padding),
            new Color(0, 0, 0, 180));

        float y = boxY;
        foreach (var (text, kolor) in wiersze)
        {
            Raylib.DrawText(text, (int)boxX, (int)y, (int)fontSize, kolor);
            y += lineHeight;
        }
    }

    public void EksportZKolorowymiLiniami(string nazwa, int szerokosc, bool rekordowy = false)
    {
        var os = rekordowy ? rekord : GetAktywnyOsobnik();
        if (os == null) return;
        int wysokosc = (int)(szerokosc / mapa.aspectRatioV.X * mapa.aspectRatioV.Y);
        RenderTexture2D target = Raylib.LoadRenderTexture(szerokosc, wysokosc);
        Raylib.BeginTextureMode(target);
        Raylib.ClearBackground(new Color(0x14, 0x14, 0x14));
        mapa.simpleRender(new Vector2(szerokosc, wysokosc));

        Vector2 PunktDoTekstury(Vector2 p) => ((p + Vector2.One) * 0.5f) * new Vector2(szerokosc, wysokosc);
        float szer = (szerokosc / mapa.aspectRatioV.X) / 120f;
        var drogaUzycie = new Dictionary<int, List<(Color, Vector2, Vector2, bool, int)>>();
        for (int l = 0; l < os.Linie.Count; l++)
        {
            var lin = os.Linie[l]; Color kolor = Paleta[l % Paleta.Length];
            for (int i = 0; i < lin.Przystanki.Count - 1; i++)
            {
                int skad = lin.Przystanki[i], dokad = lin.Przystanki[i + 1];
                var sciezka = sciezkiMiedzyPrzystankami[skad, dokad];
                for (int step = 0; step < sciezka.Count; step++)
                {
                    var (roadIdx, fwd) = sciezka[step];
                    var droga = mapa.drogi[roadIdx]; Vector2 A = mapa.punkty[droga.a], B = mapa.punkty[droga.b];
                    Vector2 start = (step == 0) ? mapa.przystanki[skad].pos : (fwd ? A : B);
                    Vector2 end = (step == sciezka.Count - 1) ? mapa.przystanki[dokad].pos : (fwd ? B : A);
                    if (!drogaUzycie.ContainsKey(roadIdx)) drogaUzycie[roadIdx] = new List<(Color, Vector2, Vector2, bool, int)>();
                    drogaUzycie[roadIdx].Add((kolor, start, end, fwd, l));
                }
            }
        }
        foreach (var kv in drogaUzycie)
        {
            var grupy = kv.Value.GroupBy(x => x.Item4);
            foreach (var grupa in grupy)
            {
                var linie = grupa.OrderBy(x => x.Item5).ToList();
                for (int i = 0; i < linie.Count; i++)
                {
                    var (kolor, startMapa, endMapa, _, _) = linie[i];
                    Vector2 s = PunktDoTekstury(startMapa), e = PunktDoTekstury(endMapa);
                    Vector2 dir = Vector2.Normalize(e - s), normal = new(-dir.Y, dir.X);
                    float off = i * szer;
                    Raylib.DrawLineEx(s + normal * off, e + normal * off, szer, kolor);
                }
            }
        }
        Raylib.EndTextureMode();
        Image img = Raylib.LoadImageFromTexture(target.Texture);
        Raylib.ImageFlipVertical(ref img);
        if (Raylib.ExportImage(img, nazwa)) Console.WriteLine($"Zapisano {nazwa}");
        else Console.WriteLine("Blad zapisu obrazu");
        Raylib.UnloadImage(img);
    }

    private void EksportWszystkieLinieDoFolderu(string folder, int szerokosc)
    {
        var os = GetAktywnyOsobnik();
        if (os == null || os.Linie.Count == 0)
        {
            Console.WriteLine("Brak linii do eksportu.");
            return;
        }

        if (!System.IO.Directory.Exists(folder))
        {
            System.IO.Directory.CreateDirectory(folder);
            Console.WriteLine($"Utworzono folder: {folder}");
        }

        for (int i = 0; i < os.Linie.Count; i++)
        {
            string plik = System.IO.Path.Combine(folder, $"linia_{i}.png");
            EksportLinieDoObrazka(os.Linie[i], i, plik, szerokosc);
        }
        Console.WriteLine($"Eksport zakonczony - {os.Linie.Count} plikow.");
    }

    private void EksportLinieDoObrazka(Linia lin, int idx, string plik, int szerokosc)
    {
        int wysokosc = (int)(szerokosc / mapa.aspectRatioV.X * mapa.aspectRatioV.Y);
        RenderTexture2D target = Raylib.LoadRenderTexture(szerokosc, wysokosc);
        Raylib.BeginTextureMode(target);
        Raylib.ClearBackground(Color.Black);

        mapa.simpleRender(new Vector2(szerokosc, wysokosc));

        Vector2 PunktDoTekstury(Vector2 p) => ((p + Vector2.One) * 0.5f) * new Vector2(szerokosc, wysokosc);
        Color kolor = Paleta[idx % Paleta.Length];
        float szer = (szerokosc / mapa.aspectRatioV.X) / 120f;

        for (int i = 0; i < lin.Przystanki.Count - 1; i++)
        {
            int skad = lin.Przystanki[i], dokad = lin.Przystanki[i + 1];
            var sciezka = sciezkiMiedzyPrzystankami[skad, dokad];
            for (int step = 0; step < sciezka.Count; step++)
            {
                var (roadIdx, fwd) = sciezka[step];
                var droga = mapa.drogi[roadIdx];
                Vector2 A = mapa.punkty[droga.a], B = mapa.punkty[droga.b];
                Vector2 start = (step == 0) ? mapa.przystanki[skad].pos : (fwd ? A : B);
                Vector2 end = (step == sciezka.Count - 1) ? mapa.przystanki[dokad].pos : (fwd ? B : A);
                Vector2 s = PunktDoTekstury(start), e = PunktDoTekstury(end);
                Raylib.DrawLineEx(s, e, szer, kolor);
            }
        }

        float radius = 4f;
        foreach (int stopIdx in lin.Przystanki)
        {
            Vector2 pos = PunktDoTekstury(mapa.przystanki[stopIdx].pos);
            Raylib.DrawCircleV(pos, radius, kolor);
            Raylib.DrawText(stopIdx.ToString(), (int)pos.X + 5, (int)pos.Y - 10, 10, Color.White);
        }

        float czas = 0;
        for (int i = 0; i < lin.Przystanki.Count - 1; i++)
            czas += czasyMiedzyPrzystankami[lin.Przystanki[i], lin.Przystanki[i + 1]];
        float dystans = czas / 60f * 25f;
        string info = $"Linia {idx}  |  {lin.Przystanki.Count} przyst.  |  {czas:F1} min  |  {dystans:F1} km";
        Raylib.DrawText(info, 10, wysokosc - 30, 16, kolor);

        Raylib.EndTextureMode();
        Image img = Raylib.LoadImageFromTexture(target.Texture);
        Raylib.ImageFlipVertical(ref img);
        if (Raylib.ExportImage(img, plik))
            Console.WriteLine($"Zapisano {plik}");
        else
            Console.WriteLine($"Blad zapisu {plik}");
        Raylib.UnloadImage(img);
    }

    private void Zapisz(string plik)
    {
        var ser = new XmlSerializer(typeof(Stan));
        using var w = new StreamWriter(plik);
        ser.Serialize(w, new Stan { Rekord = rekord, Generacja = generacja });
        Console.WriteLine("Zapisano stan (rekord)");
    }

    private void Wczytaj(string plik)
    {
        var ser = new XmlSerializer(typeof(Stan));
        using var r = new StreamReader(plik);
        var s = (Stan)ser.Deserialize(r)!;
        if (s.Rekord != null)
        {
            rekord = s.Rekord;
            generacja = s.Generacja;
            InicjalizujPopulacjeZRekordu();
            Console.WriteLine("Wczytano rekord i odtworzono populacje.");
        }
        else
        {
            Console.WriteLine("Brak zapisanego rekordu.");
        }
    }
    public void Clear()
{
    dziala = false;
    populacja.Clear();
    najlepszy = null;
    rekord = null;
    generacja = 0;
    historiaFitness.Clear();
    rozklad = null;
    ostatniaWersjaMapy = -1;
    domyId.Clear(); centraId.Clear(); szkolyId.Clear(); pracaId.Clear();
    popytSzkola = null!;
    popytPraca = null!;
}


    public void cmd(string[] args)
    {
        switch (args[0])
        {
            case "clear":Clear();Console.WriteLine("Wyczyszczono");break;
            case "start": dziala = true; Console.WriteLine("Symulacja start"); break;
            case "stop": dziala = false; Console.WriteLine("Symulacja stop"); break;
            case "step": WykonajKrokEwolucji(true); break;
            case "reset": InicjalizujPopulacje(); Console.WriteLine("Reset populacji"); break;
            case "save": if (args.Length > 1) Zapisz(args[1]); break;
            case "load": if (args.Length > 1) Wczytaj(args[1]); break;

            case "opcje":
                if (args.Length == 1)
                {
                    Console.WriteLine("=== Opcje ===");
                    Console.WriteLine($"ROZMIAR_POPULACJI = {ROZMIAR_POPULACJI}");
                    Console.WriteLine($"ELITA             = {ELITA}");
                    Console.WriteLine($"MUTACJA_BAZOWA    = {MUTACJA_BAZOWA:F2}");
                    Console.WriteLine($"MUTACJA_PRZYROST  = {MUTACJA_PRZYROST:F2}");
                    Console.WriteLine($"MAKS_LINII        = {MAKS_LINII}");
                    Console.WriteLine($"MIN_PRZYSTANKOW   = {MIN_PRZYSTANKOW}");
                    Console.WriteLine($"MAKS_PRZYSTANKOW  = {MAKS_PRZYSTANKOW}");
                    Console.WriteLine($"MIN_ODJAZDOW      = {MIN_ODJAZDOW}");
                    Console.WriteLine($"MAKS_ODJAZDOW     = {MAKS_ODJAZDOW}");
                    Console.WriteLine($"SZANSA_SWAP       = {SZANSA_SWAP:F2}");
                    Console.WriteLine($"PROG_SREDNI       = {PROG_SREDNI}");
                    Console.WriteLine($"POJEMNOSC_DUZY    = {POJEMNOSC_DUZY}");
                    Console.WriteLine($"POJEMNOSC_SREDNI  = {POJEMNOSC_SREDNI}");
                    Console.WriteLine($"MAKS_DYSTANS_KM   = {MAKS_DYSTANS_KM}");
                    Console.WriteLine($"KARA_ZA_KM_NADMIAR= {KARA_ZA_KM_NADMIAR}");
                    Console.WriteLine($"CEL_LINII= {CEL_LINII}");
                }
                else if (args.Length >= 3)
                {
                    UstawOpcje(args[1], args[2]);
                    if (rekord != null) rekord.Fitness = ObliczFitness(rekord);
                }
                else
                {
                    Console.WriteLine("Uzycie: opt opcje - lista, opt opcje <nazwa> <wartosc> - ustaw");
                }
                break;

            case "wagi":
                if (args.Length == 1)
                {
                    Console.WriteLine("=== Wagi ===");
                    Console.WriteLine($"WAGA_DLUGOSC_TRASY = {WAGA_DLUGOSC_TRASY:F2}");
                    Console.WriteLine($"WAGA_FLOTA         = {WAGA_FLOTA:F2}");
                    Console.WriteLine($"KARA_NIESPELNIONE  = {KARA_NIESPELNIONE:F2}");
                    Console.WriteLine($"WAGA_DUPLIKATY_DROG= {WAGA_DUPLIKATY_DROG:F2}");
                    Console.WriteLine($"WAGA_ZAWRACANIE    = {WAGA_ZAWRACANIE:F2}");
                    Console.WriteLine($"WAGA_LICZBA_LINII  = {WAGA_LICZBA_LINII:F2}");
                    Console.WriteLine($"WAGA_KOSZT         = {WAGA_KOSZT:F6}");
                    Console.WriteLine($"SZANSA_SWAP        = {SZANSA_SWAP:F2}");
                    Console.WriteLine($"KARA_NIEKRYCIE     = {KARA_NIEKRYCIE_PRZYSTANKU:F2}");
                    Console.WriteLine($"KARA_BRAK_POLACZ   = {KARA_BRAK_POLACZENIA:F2}");
                    Console.WriteLine($"KARA_SZKOLA_CZAS   = {KARA_PRZEKROCZENIE_CZASU_SZKOLA:F2}");
                    Console.WriteLine($"KARA_PRACA_CZAS    = {KARA_PRZEKROCZENIE_CZASU_PRACA:F2}");
                }
                else if (args.Length >= 3)
                {
                    UstawWage(args[1], args[2]);
                    if (rekord != null) rekord.Fitness = ObliczFitness(rekord);
                    PrzeliczFitnessPopulacji();
                }
                else
                {
                    Console.WriteLine("Uzycie: opt wagi - lista, opt wagi <nazwa> <wartosc> - ustaw");
                }
                break;

            case "strategy":
                if (args.Length < 2) { Console.WriteLine("Uzycie: opt strategy <brak|elit|mutation|auto>"); break; }
                var s = args[1].ToLower();
                if (s == "brak" || s == "elit" || s == "mutation" || s == "auto")
                {
                    aktywnaStrategia = s;
                    Console.WriteLine($"Strategia ustawiona na: {aktywnaStrategia}");
                }
                else Console.WriteLine("Nieznana strategia");
                break;

            case "wykres":
                pokazWykres = !pokazWykres;
                Console.WriteLine($"Wykres fitness: {(pokazWykres ? "ON" : "OFF")}");
                break;

            case "bulkexport":
                if (args.Length < 3) { Console.WriteLine("Uzycie: opt bulkexport <folder> <szerokosc>"); break; }
                if (int.TryParse(args[2], out int szer2))
                    EksportWszystkieLinieDoFolderu(args[1], szer2);
                break;

            case "export":
                if (args.Length < 3) { Console.WriteLine("Uzycie: export <plik> <szerokosc>"); break; }
                if (int.TryParse(args[2], out int szer))
                    EksportZKolorowymiLiniami(args[1], szer, true);
                break;

            default: Console.WriteLine("Nieznana komenda"); break;
        }
    }

    private void UstawOpcje(string nazwa, string wartoscStr)
    {
        try
        {
            switch (nazwa.ToUpper())
            {//dodaj CEL_PRZYSTANKOw
                case "ROZMIAR_POPULACJI": if (int.TryParse(wartoscStr, out int v) && v > 0) ROZMIAR_POPULACJI = v; else throw new Exception("Dodatnia liczba calkowita"); break;
                case "ELITA": if (int.TryParse(wartoscStr, out int e) && e >= 0) { ELITA = e; oryginalnaElita = e; } else throw new Exception("Niepoprawna wartosc"); break;
                case "MUTACJA_BAZOWA": if (float.TryParse(wartoscStr, out float mb) && mb >= 0 && mb <= 1) { MUTACJA_BAZOWA = mb; MUTACJA_SZANSA = mb; oryginalnaMutacja = mb; } else throw new Exception("Wartosc [0,1]"); break;
                case "MUTACJA_PRZYROST": if (float.TryParse(wartoscStr, out float mp) && mp >= 0) MUTACJA_PRZYROST = mp; else throw new Exception(">=0"); break;
                case "MAKS_LINII": if (int.TryParse(wartoscStr, out int ml) && ml >= 1) MAKS_LINII = ml; else throw new Exception(">=1"); break;
                case "MIN_PRZYSTANKOW": if (int.TryParse(wartoscStr, out int minp) && minp >= 2) MIN_PRZYSTANKOW = minp; else throw new Exception(">=2"); break;
                case "MAKS_PRZYSTANKOW": if (int.TryParse(wartoscStr, out int maxp) && maxp >= MIN_PRZYSTANKOW) MAKS_PRZYSTANKOW = maxp; else throw new Exception($">= {MIN_PRZYSTANKOW}"); break;
                case "MIN_ODJAZDOW": if (int.TryParse(wartoscStr, out int mino) && mino >= 1) MIN_ODJAZDOW = mino; else throw new Exception(">=1"); break;
                case "MAKS_ODJAZDOW": if (int.TryParse(wartoscStr, out int maxo) && maxo >= MIN_ODJAZDOW) MAKS_ODJAZDOW = maxo; else throw new Exception($">= {MIN_ODJAZDOW}"); break;
                case "SZANSA_SWAP": if (float.TryParse(wartoscStr, out float sw) && sw >= 0 && sw <= 1) SZANSA_SWAP = sw; else throw new Exception("Wartosc [0,1]"); break;
                case "CEL_LINII": if (int.TryParse(wartoscStr, out int cp) && cp >= MIN_PRZYSTANKOW && cp <= MAKS_PRZYSTANKOW) CEL_LINII = cp; else throw new Exception($"Wartosc [{MIN_ODJAZDOW},{MAKS_PRZYSTANKOW}]"); break;
                case "PROG_SREDNI": if (int.TryParse(wartoscStr, out int ps) && ps > 0) PROG_SREDNI = ps; else throw new Exception("Dodatnia liczba calkowita"); break;
                case "POJEMNOSC_DUZY": if (int.TryParse(wartoscStr, out int pd) && pd > 0) POJEMNOSC_DUZY = pd; else throw new Exception("Dodatnia liczba calkowita"); break;
                case "POJEMNOSC_SREDNI": if (int.TryParse(wartoscStr, out int ps2) && ps2 > 0) POJEMNOSC_SREDNI = ps2; else throw new Exception("Dodatnia liczba calkowita"); break;
                case "MAKS_DYSTANS_KM": if (float.TryParse(wartoscStr, out float md) && md > 0) MAKS_DYSTANS_KM = md; else throw new Exception(">0"); break;
                case "KARA_ZA_KM_NADMIAR": if (float.TryParse(wartoscStr, out float kd)) KARA_ZA_KM_NADMIAR = kd; else throw new Exception("Niepoprawna liczba"); break;
                default: Console.WriteLine($"Nieznana opcja: {nazwa}"); return;
            }
            Console.WriteLine($"Ustawiono opcje {nazwa} = {wartoscStr}");
        }
        catch (Exception ex) { Console.WriteLine($"Blad: {ex.Message}"); }
    }

    private void UstawWage(string nazwa, string wartoscStr)
    {
        try
        {
            switch (nazwa.ToUpper())
            {
                case "WAGA_DLUGOSC_TRASY": if (float.TryParse(wartoscStr, out float wd)) WAGA_DLUGOSC_TRASY = wd; else throw new Exception("Niepoprawna liczba"); break;
                case "WAGA_FLOTA": if (float.TryParse(wartoscStr, out float wf)) WAGA_FLOTA = wf; else throw new Exception("Niepoprawna liczba"); break;
                case "KARA_NIESPELNIONE": if (float.TryParse(wartoscStr, out float kn)) KARA_NIESPELNIONE = kn; else throw new Exception("Niepoprawna liczba"); break;
                case "WAGA_DUPLIKATY_DROG": if (float.TryParse(wartoscStr, out float wdd)) WAGA_DUPLIKATY_DROG = wdd; else throw new Exception("Niepoprawna liczba"); break;
                case "WAGA_ZAWRACANIE": if (float.TryParse(wartoscStr, out float wz)) WAGA_ZAWRACANIE = wz; else throw new Exception("Niepoprawna liczba"); break;
                case "WAGA_LICZBA_LINII": if (float.TryParse(wartoscStr, out float wl)) WAGA_LICZBA_LINII = wl; else throw new Exception("Niepoprawna liczba"); break;
                case "WAGA_KOSZT": if (float.TryParse(wartoscStr, out float wk)) WAGA_KOSZT = wk; else throw new Exception("Niepoprawna liczba"); break;
                case "SZANSA_SWAP": if (float.TryParse(wartoscStr, out float sw) && sw >= 0 && sw <= 1) SZANSA_SWAP = sw; else throw new Exception("Wartosc [0,1]"); break;
                case "KARA_NIEKRYCIE": if (float.TryParse(wartoscStr, out float knk)) KARA_NIEKRYCIE_PRZYSTANKU = knk; else throw new Exception("Niepoprawna liczba"); break;
                case "KARA_BRAK_POLACZ": if (float.TryParse(wartoscStr, out float kbp)) KARA_BRAK_POLACZENIA = kbp; else throw new Exception("Niepoprawna liczba"); break;
                case "KARA_SZKOLA_CZAS": if (float.TryParse(wartoscStr, out float ksc)) KARA_PRZEKROCZENIE_CZASU_SZKOLA = ksc; else throw new Exception("Niepoprawna liczba"); break;
                case "KARA_PRACA_CZAS": if (float.TryParse(wartoscStr, out float kpc)) KARA_PRZEKROCZENIE_CZASU_PRACA = kpc; else throw new Exception("Niepoprawna liczba"); break;
                default: Console.WriteLine($"Nieznana waga: {nazwa}"); return;
            }
            Console.WriteLine($"Ustawiono wage {nazwa} = {wartoscStr}");
        }
        catch (Exception ex) { Console.WriteLine($"Blad: {ex.Message}"); }
    }

    public Osobnik? GetAktywny() => GetAktywnyOsobnik();
    public float[,] GetCzasyMiedzyPrzystankami() => czasyMiedzyPrzystankami;
    public List<(int roadIdx, bool forward)>[,] GetSciezkiMiedzyPrzystankami() => sciezkiMiedzyPrzystankami;
}

[Serializable]
public struct Stan
{
    public Osobnik? Rekord;
    public int Generacja;
}

internal class SymulatorDnia : Ctx, ICmdHandler
{
    private Mapa mapa;
    private OptymalizatorRozkladu optymalizator;
    private Texture2D texBusDuzy, texBusSredni;

    private bool dziala = false;
    private float czasMinuty = 0f;
    private float predkoscMnoznik = 1f;

    private bool pokazAutobusy = true;
    private bool pokazHud = true;

    private List<(List<(Vector2 start, Vector2 end, float tStart, float tEnd)> segmenty, int liniaIdx, RozmiarAutobusu rozmiar)> przejazdy = new();

    public SymulatorDnia(Punkt pos, Punkt size, Mapa mapa, OptymalizatorRozkladu opt) : base(pos, size)
    {
        this.mapa = mapa;
        this.optymalizator = opt;
        texBusDuzy = Raylib.LoadTexture("./assets/bus.png");
        texBusSredni = Raylib.LoadTexture("./assets/bus.png");
        BudujKursy();
    }

    public void BudujKursy()
    {
        przejazdy.Clear();
        var os = optymalizator.GetAktywny();
        if (os == null || os.Linie.Count == 0) { Console.WriteLine("Brak danych o trasach."); return; }

        var czasyMiedzy = optymalizator.GetCzasyMiedzyPrzystankami();
        var sciezki = optymalizator.GetSciezkiMiedzyPrzystankami();
        if (czasyMiedzy == null || sciezki == null) return;

        int totalKursow = 0;
        var localRng = new Random();
        foreach (var lin in os.Linie)
        {
            float czasT = CzasTrasy(lin.Przystanki, czasyMiedzy);
            int lIdx = os.Linie.IndexOf(lin);

            foreach (int t in lin.Odjazdy)
            {
                var segmenty = GenerujSegmenty(lin.Przystanki, sciezki, t);
                przejazdy.Add((segmenty, lIdx, lin.Rozmiar));
                totalKursow++;

                var odwrocona = Enumerable.Reverse(lin.Przystanki).ToList();
                int postoj = localRng.Next(0, 10);
                float czasPowrot = t + czasT + postoj;
                var segmentyPowrot = GenerujSegmenty(odwrocona, sciezki, czasPowrot);
                przejazdy.Add((segmentyPowrot, lIdx, lin.Rozmiar));
                totalKursow++;
            }
        }
        Console.WriteLine($"Zbudowano {totalKursow} kursow.");
    }

    private float CzasTrasy(List<int> przystanki, float[,] czasy)
    {
        float c = 0;
        for (int i = 0; i < przystanki.Count - 1; i++) c += czasy[przystanki[i], przystanki[i + 1]];
        return c;
    }

    private List<(Vector2 start, Vector2 end, float tStart, float tEnd)> GenerujSegmenty(
        List<int> przystanki, List<(int roadIdx, bool forward)>[,] sciezki, float czasStart)
    {
        var segmenty = new List<(Vector2, Vector2, float, float)>();
        float aktCzas = czasStart;
        const float MIN_PER_UNIT = 19.2f;

        for (int i = 0; i < przystanki.Count - 1; i++)
        {
            int skad = przystanki[i];
            int dokad = przystanki[i + 1];
            var sciezka = sciezki[skad, dokad];

            for (int step = 0; step < sciezka.Count; step++)
            {
                var (rIdx, fwd) = sciezka[step];
                var dr = mapa.drogi[rIdx];
                Vector2 A = mapa.punkty[dr.a];
                Vector2 B = mapa.punkty[dr.b];

                // Początek i koniec odcinka
                Vector2 segStart, segEnd;

                if (step == 0)
                    segStart = mapa.przystanki[skad].pos; // pierwszy krok - zaczynamy na przystanku
                else
                    segStart = fwd ? A : B;                // kontynuacja - punkt węzłowy (zależny od kierunku)

                if (step == sciezka.Count - 1)
                    segEnd = mapa.przystanki[dokad].pos;   // ostatni krok - kończymy na przystanku
                else
                    segEnd = fwd ? B : A;                  // pośredni - drugi punkt drogi

                float dist = Vector2.Distance(segStart, segEnd);
                float czasSegmentu = dist * MIN_PER_UNIT;
                segmenty.Add((segStart, segEnd, aktCzas, aktCzas + czasSegmentu));
                aktCzas += czasSegmentu;
            }
        }
        return segmenty;
    }
    public void Draw()
    {
        if (dziala) { float deltaSek = Raylib.GetFrameTime(); czasMinuty += deltaSek * predkoscMnoznik; if (czasMinuty >= 1440f) czasMinuty -= 1440f; }
        if (pokazAutobusy) RysujAutobusy();
        if (pokazHud) RysujHud();
    }

    private void RysujAutobusy()
    {
        Color[] paleta = OptymalizatorRozkladu.Paleta;
        foreach (var (segmenty, liniaIdx, rozmiar) in przejazdy)
        {
            for (int i = 0; i < segmenty.Count; i++)
            {
                var (start, end, tStart, tEnd) = segmenty[i];
                if (czasMinuty >= tStart && czasMinuty <= tEnd)
                {
                    float t = (czasMinuty - tStart) / (tEnd - tStart);
                    Vector2 posSwiat = Vector2.Lerp(start, end, Math.Clamp(t, 0, 1));
                    Vector2 posEkran = mapa.point2screen(posSwiat).v2() + pos.v2();
                    Vector2 dir = Vector2.Normalize(end - start);
                    float kat = MathF.Atan2(dir.Y, dir.X) * (180f / MathF.PI) - 90f;

                    float busWidth = mapa.scale * 0.1f;
                    if (rozmiar == RozmiarAutobusu.Sredni) busWidth *= 0.66f;
                    float busHeight = busWidth * texBusDuzy.Height / texBusDuzy.Width;
                    Rectangle source = new Rectangle(0, 0, texBusDuzy.Width, texBusDuzy.Height);
                    Rectangle dest = new Rectangle(posEkran.X, posEkran.Y, busWidth, busHeight);
                    Vector2 origin = new Vector2(busWidth / 2, busHeight / 2);
                    Raylib.DrawTexturePro(texBusDuzy, source, dest, origin, kat, Color.White);

                    int fontSize = (int)(busHeight * 0.4f);
                    string txt = liniaIdx.ToString();
                    int tw = Raylib.MeasureText(txt, fontSize);
                    float textY = posEkran.Y - busHeight / 2 - fontSize - 2;
                    Raylib.DrawText(txt, (int)(posEkran.X - tw / 2), (int)textY, fontSize, paleta[liniaIdx % paleta.Length]);
                    break;
                }
            }
        }
    }

    private void RysujHud()
    {
        int godz = (int)czasMinuty / 60;
        int min = (int)czasMinuty % 60;
        string tekst = $"Czas: {godz:D2}:{min:D2}";
        Raylib.DrawText(tekst, pos.x + 10, pos.y + size.y - 30, 24, Color.White);
    }
    public void Clear()
    {
        dziala = false;
        przejazdy.Clear();
        czasMinuty = 0;
    }

    public void cmd(string[] args)
    {
        switch (args[0])
        {
            case "clear":Clear();Console.WriteLine("Wyczyszczono");break;
            case "start": dziala = true; break;
            case "stop": dziala = false; break;
            case "reset": czasMinuty = 0; dziala = false; BudujKursy(); break;
            case "speed": if (args.Length > 1 && float.TryParse(args[1], out float sp)) predkoscMnoznik = sp; break;
            case "goto": if (args.Length > 1) { var p = args[1].Split(':'); if (p.Length == 2 && int.TryParse(p[0], out int h) && int.TryParse(p[1], out int m)) czasMinuty = h * 60 + m; } break;
            case "hud": pokazHud = !pokazHud; break;
            case "buses": pokazAutobusy = !pokazAutobusy; break;
            default: Console.WriteLine("Nieznana komenda symulatora"); break;
        }
    }
}