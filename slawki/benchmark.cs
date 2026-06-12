using System.Numerics;
using Raylib_cs;
using System.Xml.Serialization;
using System.Collections.Concurrent;
using System.IO;

namespace Slawki
{
    public class Benchmark : ICmdHandler
    {
        public void cmd(string[] args)
        {
            if (args[0] == "start")
            {
                string mapFile = args.Length > 1 ? args[1] : "slawki.xml";
                Run(mapFile);
            }
            else
            {
                Console.WriteLine("Użycie: bench start [mapa.xml]");
            }
        }

        private void Run(string mapFile)
        {
            Console.WriteLine("=== Rozpoczynanie benchmarku ===");
            Console.WriteLine($"Ładowanie mapy: {mapFile}");

            // Własna instancja mapy i optymalizatora
            Mapa mapa = new Mapa(new Punkt(0, 0), new Punkt(800, 600), 1.3f);
            mapa.load(mapFile);
            var opt = new OptymalizatorRozkladu(new Punkt(0, 0), new Punkt(800, 600), mapa);

            string[] strategie = { "brak", "mutation", "auto" };
            int iteracji = 5;
            int pokolen = 10000;
            int probkowanieCo = 1;   // 100 punktów na wykresie
            int liczbaProbek = pokolen / probkowanieCo;

            var wyniki = new Dictionary<string, List<float[]>>();
            var czasy = new Dictionary<string, List<double>>();
            foreach (var s in strategie)
            {
                wyniki[s] = new List<float[]>();
                czasy[s] = new List<double>();
            }

            for (int iter = 0; iter < iteracji; iter++)
            {
                Console.WriteLine($"\n--- Iteracja {iter + 1}/{iteracji} ---");
                Console.WriteLine("Generowanie populacji początkowej...");
                opt.InicjalizujPopulacje();
                string plikStartowy = $"pop_init_{iter}.xml";
                opt.EksportPopulacje(plikStartowy);
                Console.WriteLine($"Zapisano populację początkową: {plikStartowy}");

                foreach (var strategia in strategie)
                {
                    Console.WriteLine($"  Testowanie strategii: {strategia}");
                    opt.WczytajPopulacje(plikStartowy);
                    opt.UstawAktywnaStrategie(strategia);
                    opt.ResetujParametryStrategii();   // przywraca domyślne ELITA, mnoznik, MUTACJA_SZANSA itd.
                    opt.PrzeliczFitnessPopulacji();
                    opt.ResetujGeneracje();

                    DateTime startCzas = DateTime.Now;
                    var fitnessPrzebiegu = new float[liczbaProbek];
                    for (int gen = 0; gen < pokolen; gen++)
                    {
                        if (gen % (pokolen / 100) == 0)
                        {
                            Console.WriteLine($"Pokolenie: {gen}/{pokolen}");
                        }
                        opt.WykonajKrokEwolucji(true);
                        if ((gen + 1) % probkowanieCo == 0)
                        {
                            int idx = (gen + 1) / probkowanieCo - 1;
                            fitnessPrzebiegu[idx] = opt.GetNajlepszyFitness();
                        }
                    }
                    DateTime koniecCzas = DateTime.Now;
                    double czasTrwania = (koniecCzas - startCzas).TotalSeconds;
                    czasy[strategia].Add(czasTrwania);
                    wyniki[strategia].Add(fitnessPrzebiegu);
                    Console.WriteLine($"    Zakończono. Fitness końcowy: {opt.GetNajlepszyFitness():F2}, Czas: {czasTrwania:F2} s");
                }
            }

            // Wykresy dla każdej strategii
            foreach (var strategia in strategie)
            {
                Console.WriteLine($"Generowanie wykresu dla strategii: {strategia}");
                GenerujWykresStrategii(strategia, wyniki[strategia], probkowanieCo, pokolen);
            }

            // Wykres podsumowujący (średnie)
            Console.WriteLine("Generowanie wykresu podsumowującego...");
            GenerujWykresPodsumowania(wyniki, strategie, probkowanieCo, pokolen);

            // Wykres czasów
            Console.WriteLine("Generowanie wykresu czasów...");
            GenerujWykresCzasow(czasy);

            // Raporty
            Console.WriteLine("Zapisywanie podsumowania CSV...");
            ZapiszCsv(wyniki, czasy, strategie);

            Console.WriteLine("Zapisywanie podsumowania JSON...");
            ZapiszJson(wyniki, czasy, strategie);

            Console.WriteLine("=== Benchmark zakończony ===");
        }

        private void GenerujWykresStrategii(string nazwa, List<float[]> dane, int krok, int maxGen)
        {
            int szer = 1200, wys = 800;
            RenderTexture2D target = Raylib.LoadRenderTexture(szer, wys);
            Raylib.BeginTextureMode(target);
            Raylib.ClearBackground(Color.Black);

            int marginLewy = 120, marginDolny = 100, marginGorny = 70, marginPrawy = 60;
            int wykresSzer = szer - marginLewy - marginPrawy;
            int wykresWys = wys - marginGorny - marginDolny;

            // Osie
            Raylib.DrawLine(marginLewy, marginGorny, marginLewy, wys - marginDolny, Color.White);
            Raylib.DrawLine(marginLewy, wys - marginDolny, szer - marginPrawy, wys - marginDolny, Color.White);

            // Tytuł
            Raylib.DrawText($"Strategia: {nazwa}", szer / 2 - 100, 10, 24, Color.Yellow);

            // Skala
            float minFit = dane.SelectMany(r => r).Min();
            float maxFit = dane.SelectMany(r => r).Max();
            if (maxFit - minFit < 0.01f) maxFit = minFit + 1;

            Color kolor = nazwa == "brak" ? Color.Red : (nazwa == "mutation" ? Color.Lime : Color.SkyBlue);

            // Pojedyncze przebiegi (półprzezroczyste)
            foreach (var przebieg in dane)
            {
                Color c = kolor;
                c.A = 100;
                for (int p = 1; p < przebieg.Length; p++)
                {
                    float x1 = marginLewy + (p - 1) * (float)wykresSzer / (przebieg.Length - 1);
                    float y1 = marginGorny + wykresWys - (przebieg[p - 1] - minFit) / (maxFit - minFit) * wykresWys;
                    float x2 = marginLewy + p * (float)wykresSzer / (przebieg.Length - 1);
                    float y2 = marginGorny + wykresWys - (przebieg[p] - minFit) / (maxFit - minFit) * wykresWys;
                    Raylib.DrawLineV(new Vector2(x1, y1), new Vector2(x2, y2), c);
                }
            }

            // Średnia (gruba linia)
            int probek = dane[0].Length;
            float[] srednia = new float[probek];
            for (int p = 0; p < probek; p++)
            {
                float suma = 0;
                foreach (var przebieg in dane) suma += przebieg[p];
                srednia[p] = suma / dane.Count;
            }
            for (int p = 1; p < probek; p++)
            {
                float x1 = marginLewy + (p - 1) * (float)wykresSzer / (probek - 1);
                float y1 = marginGorny + wykresWys - (srednia[p - 1] - minFit) / (maxFit - minFit) * wykresWys;
                float x2 = marginLewy + p * (float)wykresSzer / (probek - 1);
                float y2 = marginGorny + wykresWys - (srednia[p] - minFit) / (maxFit - minFit) * wykresWys;
                Raylib.DrawLineV(new Vector2(x1, y1), new Vector2(x2, y2), kolor);
            }

            // Etykiety
            Raylib.DrawText($"{maxFit:F0}", marginLewy - 80, marginGorny - 10, 16, Color.White);
            Raylib.DrawText($"{minFit:F0}", marginLewy - 80, marginGorny + wykresWys - 15, 16, Color.White);
            Raylib.DrawText("Fitness", 20, wys / 2 - 15, 20, Color.White);
            Raylib.DrawText("0", marginLewy - 20, marginGorny + wykresWys + 10, 16, Color.White);
            Raylib.DrawText(maxGen.ToString(), marginLewy + wykresSzer - 40, marginGorny + wykresWys + 10, 16, Color.White);
            Raylib.DrawText("Generacja", szer / 2 - 50, wys - 40, 20, Color.White);

            Raylib.EndTextureMode();
            Image img = Raylib.LoadImageFromTexture(target.Texture);
            Raylib.ImageFlipVertical(ref img);
            Raylib.ExportImage(img, $"benchmark_{nazwa}.png");
            Raylib.UnloadImage(img);
            Console.WriteLine($"Zapisano benchmark_{nazwa}.png");
        }

        private void GenerujWykresPodsumowania(Dictionary<string, List<float[]>> dane, string[] strategie, int krok, int maxGen)
        {
            int szer = 1200, wys = 800;
            RenderTexture2D target = Raylib.LoadRenderTexture(szer, wys);
            Raylib.BeginTextureMode(target);
            Raylib.ClearBackground(Color.Black);

            int marginLewy = 120, marginDolny = 100, marginGorny = 70, marginPrawy = 60;
            int wykresSzer = szer - marginLewy - marginPrawy;
            int wykresWys = wys - marginGorny - marginDolny;

            Raylib.DrawLine(marginLewy, marginGorny, marginLewy, wys - marginDolny, Color.White);
            Raylib.DrawLine(marginLewy, wys - marginDolny, szer - marginPrawy, wys - marginDolny, Color.White);
            Raylib.DrawText("Podsumowanie – średnie fitness", szer / 2 - 150, 10, 24, Color.Yellow);

            Color[] kolory = { Color.Red, Color.Lime, Color.SkyBlue };
            for (int i = 0; i < strategie.Length; i++)
                Raylib.DrawText(strategie[i], marginLewy + 10 + i * 120, 35, 18, kolory[i]);

            float minFit = dane.Values.SelectMany(runs => runs.SelectMany(r => r)).Min();
            float maxFit = dane.Values.SelectMany(runs => runs.SelectMany(r => r)).Max();
            if (maxFit - minFit < 0.01f) maxFit = minFit + 1;

            int probek = dane[strategie[0]][0].Length;

            for (int si = 0; si < strategie.Length; si++)
            {
                string strategia = strategie[si];
                var runs = dane[strategia];
                float[] srednia = new float[probek];
                for (int p = 0; p < probek; p++)
                {
                    float suma = 0;
                    foreach (var przebieg in runs) suma += przebieg[p];
                    srednia[p] = suma / runs.Count;
                }
                for (int p = 1; p < probek; p++)
                {
                    float x1 = marginLewy + (p - 1) * (float)wykresSzer / (probek - 1);
                    float y1 = marginGorny + wykresWys - (srednia[p - 1] - minFit) / (maxFit - minFit) * wykresWys;
                    float x2 = marginLewy + p * (float)wykresSzer / (probek - 1);
                    float y2 = marginGorny + wykresWys - (srednia[p] - minFit) / (maxFit - minFit) * wykresWys;
                    Raylib.DrawLineV(new Vector2(x1, y1), new Vector2(x2, y2), kolory[si]);
                }
            }

            Raylib.DrawText($"{maxFit:F0}", marginLewy - 80, marginGorny - 10, 16, Color.White);
            Raylib.DrawText($"{minFit:F0}", marginLewy - 80, marginGorny + wykresWys - 15, 16, Color.White);
            Raylib.DrawText("Fitness", 20, wys / 2 - 15, 20, Color.White);
            Raylib.DrawText("0", marginLewy - 20, marginGorny + wykresWys + 10, 16, Color.White);
            Raylib.DrawText(maxGen.ToString(), marginLewy + wykresSzer - 40, marginGorny + wykresWys + 10, 16, Color.White);
            Raylib.DrawText("Generacja", szer / 2 - 50, wys - 40, 20, Color.White);

            Raylib.EndTextureMode();
            Image img = Raylib.LoadImageFromTexture(target.Texture);
            Raylib.ImageFlipVertical(ref img);
            Raylib.ExportImage(img, "benchmark_podsumowanie.png");
            Raylib.UnloadImage(img);
            Console.WriteLine("Zapisano benchmark_podsumowanie.png");
        }

        private void GenerujWykresCzasow(Dictionary<string, List<double>> czasy)
        {
            int szer = 1200, wys = 800;
            RenderTexture2D target = Raylib.LoadRenderTexture(szer, wys);
            Raylib.BeginTextureMode(target);
            Raylib.ClearBackground(Color.Black);

            int marginLewy = 120, marginDolny = 100, marginGorny = 70, marginPrawy = 60;
            int wykresSzer = szer - marginLewy - marginPrawy;
            int wykresWys = wys - marginGorny - marginDolny;

            Raylib.DrawLine(marginLewy, marginGorny, marginLewy, wys - marginDolny, Color.White);
            Raylib.DrawLine(marginLewy, wys - marginDolny, szer - marginPrawy, wys - marginDolny, Color.White);
            Raylib.DrawText("Średni czas wykonania (sekundy)", szer / 2 - 130, 10, 24, Color.Yellow);

            var strategie = czasy.Keys.ToArray();
            Color[] kolory = { Color.Red, Color.Lime, Color.SkyBlue };
            double maxCzas = czasy.Values.SelectMany(x => x).Max();
            if (maxCzas < 0.01) maxCzas = 1;
            float barWidth = (float)wykresSzer / (strategie.Length * 2 + 1);

            for (int i = 0; i < strategie.Length; i++)
            {
                string s = strategie[i];
                double avg = czasy[s].Average();
                float x = marginLewy + (i * 2 + 1) * barWidth;
                float barHeight = (float)(avg / maxCzas * wykresWys);
                Rectangle rect = new Rectangle(x, marginGorny + wykresWys - barHeight, barWidth, barHeight);
                Raylib.DrawRectangleRec(rect, kolory[i % kolory.Length]);
                Raylib.DrawText($"{s}\n{avg:F1}s", (int)x, (int)(marginGorny + wykresWys + 5), 16, kolory[i % kolory.Length]);
            }

            Raylib.EndTextureMode();
            Image img = Raylib.LoadImageFromTexture(target.Texture);
            Raylib.ImageFlipVertical(ref img);
            Raylib.ExportImage(img, "benchmark_czasy.png");
            Raylib.UnloadImage(img);
            Console.WriteLine("Zapisano benchmark_czasy.png");
        }

        private void ZapiszCsv(Dictionary<string, List<float[]>> dane, Dictionary<string, List<double>> czasy, string[] strategie)
        {
            using (var writer = new StreamWriter("benchmark_podsumowanie.csv"))
            {
                writer.WriteLine("Strategia,Przebieg,SredniFitness,KoncowyFitness,Polepszenie,CzasSekundy");
                foreach (var s in strategie)
                {
                    var runs = dane[s];
                    var timeList = czasy[s];
                    for (int i = 0; i < runs.Count; i++)
                    {
                        float avg = runs[i].Average();
                        float last = runs[i].Last();
                        float first = runs[i].First();
                        float improvement = first != 0 ? (last - first) / Math.Abs(first) * 100 : 0;
                        double czas = timeList[i];
                        writer.WriteLine($"{s},{i + 1},{avg:F4},{last:F4},{improvement:F2}%,{czas:F2}");
                    }
                }
            }
            Console.WriteLine("Zapisano benchmark_podsumowanie.csv");
        }

        private void ZapiszJson(Dictionary<string, List<float[]>> dane, Dictionary<string, List<double>> czasy, string[] strategie)
        {
            var json = new System.Text.StringBuilder();
            json.AppendLine("[");
            bool firstStrategy = true;
            foreach (var s in strategie)
            {
                if (!firstStrategy) json.AppendLine(",");
                firstStrategy = false;
                json.AppendLine("  {");
                json.AppendLine($"    \"strategia\": \"{s}\",");
                json.AppendLine("    \"przebiegi\": [");
                var runs = dane[s];
                var timeList = czasy[s];
                for (int i = 0; i < runs.Count; i++)
                {
                    json.AppendLine("      {");
                    json.AppendLine($"        \"nr\": {i + 1},");
                    json.AppendLine($"        \"sredniFitness\": {runs[i].Average():F4},");
                    json.AppendLine($"        \"koncowyFitness\": {runs[i].Last():F4},");
                    float first = runs[i].First();
                    float last = runs[i].Last();
                    float improvement = first != 0 ? (last - first) / Math.Abs(first) * 100 : 0;
                    json.AppendLine($"        \"polepszenieProcent\": {improvement:F2},");
                    json.AppendLine($"        \"czasSekundy\": {timeList[i]:F2}");
                    json.Append("      }");
                    if (i < runs.Count - 1) json.AppendLine(",");
                    else json.AppendLine();
                }
                json.AppendLine("    ]");
                json.Append("  }");
            }
            json.AppendLine();
            json.AppendLine("]");
            File.WriteAllText("benchmark_podsumowanie.json", json.ToString());
            Console.WriteLine("Zapisano benchmark_podsumowanie.json");
        }
    }
}