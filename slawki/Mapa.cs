using System.Numerics;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using Raylib_cs;

namespace Slawki;

public class Mapa : Ctx, ICmdHandler
{
    //dane mapy
    public List<Vector2> punkty = new List<Vector2>();
    public List<Road> drogi = new List<Road>();
    public List<Przystanek> przystanki = new List<Przystanek>();
    //kształt mapy
    public Vector2 aspectRatioV;
    [XmlIgnore]
    public float scale = 30;
    [XmlIgnore]
    public Vector2 offset = new Vector2(0);
    //inne
    [XmlIgnore]
    public Przystanek closestRoadPoint = new Przystanek(-1,0);
    [XmlIgnore]
    public MapPoint closestPoint = new MapPoint(-1, float.PositiveInfinity, new Punkt());
    [XmlIgnore]
    public MapPoint closestStop = new MapPoint(-1, float.PositiveInfinity, new Punkt());


    //kolory
    static Color backgroundC = new Color(0x14, 0x14, 0x14);
    static Color pointC = new Color(0x55, 0x55, 0x55);
    static Color textC = new Color(0xFF, 0xFF, 0xFF);
    static Color homeC = new Color(0xAA, 0xAA, 0xAA);
    static Color stopC = new Color(141,6,209);

    public struct Road
    {
        public int a, b;
        public Road(int a, int b)
        {
            this.a = a;
            this.b = b;
        }

        public static bool operator ==(Road a, Road b)
        {
            return (a.a == b.a && a.b == b.b) || (a.a == b.b && a.b == b.a);
        }
        public static bool operator !=(Road a, Road b)
        {
            return !((a.a == b.a && a.b == b.b) || (a.a == b.b && a.b == b.a));
        }

        public override bool Equals(object? obj)
        {
            return base.Equals(obj);
        }
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }

    public struct Przystanek
    {
        public int index;
        public float offset;
        public enum typPrzystanku
        {
            normal,
            center,
            school,
            work,
            _count
        }
        public static Texture2D[] ikonki = 
        {
            Raylib.LoadTextureFromImage(Raylib.GenImageColor(1,1,new Color(0,0,0,0))),
            Raylib.LoadTexture("./assets/przystanki/shop.png"),
            Raylib.LoadTexture("./assets/przystanki/school.png"),
            Raylib.LoadTexture("./assets/przystanki/work.png")
        };
        static Color[] kolory = new Color[]
        {
            Color.Purple,
            Color.Pink,
            Color.Gold,
            Color.Orange
        };
        public Color kolor()
        {
            return kolory[(int)typ];
        }
        public typPrzystanku typ = typPrzystanku.normal;

        [XmlIgnore]
        public Vector2 pos;//not serialized
        public Przystanek()
        {
            
        }
        public Przystanek(int i, float o)
        {
            index = i;
            offset = o;
        }
        public Przystanek(int i, float o, Vector2 p)
        {
            index = i;
            offset = o;
            pos = p;
        }
    }

    

    public Mapa(Punkt pos, Punkt size, float aspectRatio) : base(pos, size)
    {

        aspectRatioV = new Vector2(aspectRatio, 1);
        if (aspectRatioV.X < 0)
        {
            aspectRatioV.Y /= aspectRatioV.X;
            aspectRatioV.X = 1;

        }

        // punkty.Add(new Vector2(0.3f, 0.5f));
        // punkty.Add(new Vector2(-0.3f, 0.8f));
        // punkty.Add(new Vector2(0.0f, -0.5f));
    }
    public Mapa()
    {
        
    }
    


    public void FillBoundry(Color c)
    {
        Punkt start = point2screen(new Vector2(-1));
        Punkt end = Punkt.from(aspectRatioV * new Vector2(scale * 2));
        Raylib.DrawRectangle(start.x, start.y, end.x, end.y, c);
    }

    public float dist(int index, Punkt p)
    {
        if (index < 0 || index >= punkty.Count)
            return float.PositiveInfinity;
        return (point2screen(punkty[index]).v2() - p.v2()).Length();
    }
    public void highlightRoad(int index,Color c)
    {
        drawRoad(drogi[index],c,60);
    }
    public void drawRoad(Road r,Color c,float iscale = 120)
    {
        DrawLine(point2screen(punkty[r.a]),point2screen(punkty[r.b]),scale/iscale,c);
    }

    public void highlightPoint(int index, Color c)
    {
        if (index < 0 || index >= punkty.Count)
            return;
        highlightPoint(punkty[index], c);
    }
    public void highlightPoint(Vector2 p, Color c)
    {
        DrawPoint(point2screen(p), scale / 60, c);
    }

    public Vector2 screen2point(Punkt p)
    {
        //100x100
        //50,50 => 0,0
        Vector2 rel = p.v2() - (size.v2() / new Vector2(2));
        Vector2 asp = rel / aspectRatioV;
        Vector2 scl = asp / new Vector2(scale);
        Vector2 off = scl + offset;

        return off;

    }
    public Vector2 offset2local(Punkt off)
    {
        Vector2 asp = off.v2() / aspectRatioV;
        Vector2 scl = asp / new Vector2(scale);
        return scl;
    }
    public Punkt point2screen(Vector2 p)
    {

        return point2texture(p, size.v2(), offset,scale);
    }

    public Punkt point2texture(Vector2 p, Vector2 size, Vector2 offset,float scale)
    {
        Vector2 off = p - offset;
        Vector2 scl = off * new Vector2(scale);
        Vector2 asp = scl * aspectRatioV;
        Vector2 rel = asp + (size / new Vector2(2));

        return Punkt.from(rel);
    }
    public void DrawPointsId()
    {
        for (int i = 0; i < punkty.Count; i++)
        {
            Punkt p = point2screen(punkty[i]);
            DrawText(i.ToString(), p, (int)scale / 10, textC);


        }
    }
    public void DrawRoadsId()
    {
        for (int i = 0; i < drogi.Count; i++)
        {
            Punkt p = (point2screen(punkty[drogi[i].a]) + point2screen(punkty[drogi[i].b])) / new Punkt(2);
            DrawText(i.ToString(), p, (int)scale / 10, textC);

            // DrawPoint(point2screen(CloasesRoadPoint(i).pos),scale/60,Color.Purple);
        }
    }
    public void DrawStopsId()
    {
        for (int i = 0; i < przystanki.Count; i++)
        {
            Punkt p = point2screen(przystanki[i].pos);
            DrawText(i.ToString(), p, (int)scale / 10, textC);
        }

    }

    



    public void Draw()
    {
        FillBoundry(backgroundC);

        float radius = scale / 120;



        foreach (Road r in drogi)
        {
            drawRoad(r,pointC);
        }

        foreach (Vector2 p in punkty)
        {
            DrawPoint(point2screen(p), radius, pointC);
        }

        foreach (Przystanek p in przystanki)
        {
            Vector2 point = point2screen(p.pos).v2();
            float size = scale/16;
            DrawPoint(point2screen(p.pos), radius, p.kolor());
            // DrawImage(Przystanek.ikonki[(int)p.typ],point+new Vector2(-size/2,-size),size);
        }
    }
    public void DrawExtras()
    {
        foreach (Przystanek p in przystanki)
        {
            Vector2 point = point2screen(p.pos).v2();
            float size = scale/16;
            DrawImage(Przystanek.ikonki[(int)p.typ],point+new Vector2(-size/2,-size),size);
        }
    }

    Vector2 point2txture(Vector2 p,Vector2 size)
    {
        return ((p+new Vector2(1))/new Vector2(2))*size;
    }

    public void simpleRender(Vector2 size)
    {

        float radius = (float)size.X / aspectRatioV.X / 120 / 2;

        //to-do roads
        foreach (Road r in drogi)
        {
            Raylib.DrawLineEx(point2txture(punkty[r.a], size),point2txture(punkty[r.b], size),(int)radius,pointC);
            // drawRoad(r,pointC);
        }

        foreach (Vector2 p in punkty)
        {
            Vector2 pu = point2txture(p,size);


            Raylib.DrawCircleV( pu, (int)radius, pointC);
        }
        foreach (Przystanek p in przystanki)
        {
            Vector2 pu = point2txture(p.pos,size);
            float s = scale/32;

            Raylib.DrawCircleV( pu, (int)radius, p.kolor());
            DrawImage(Przystanek.ikonki[(int)p.typ],pu+new Vector2(-s/2,-s),s);
        }
    }
    public void simpleExport(int width, string name)
    {
        int height = (int)(((float)width / aspectRatioV.X) * aspectRatioV.Y);

        RenderTexture2D target = Raylib.LoadRenderTexture(width,height);

        Raylib.BeginTextureMode(target);

        Raylib.ClearBackground(backgroundC);
        this.simpleRender(new Vector2(width,height));

        Raylib.EndTextureMode();

        Image img = Raylib.LoadImageFromTexture(target.Texture);
        Raylib.UnloadTexture(target.Texture);

        Raylib.ImageFlipVertical(ref img);

        if (Raylib.ExportImage(img, name))
            Console.WriteLine($"Zapisano do {name} {width}x{height}");
        else
            Console.WriteLine("Coś poszło nie tak");

        Raylib.UnloadImage(img);

    }


    public void cmd(String[] cmd)
    {
        if (cmd.Length < 0)
        {
            Console.WriteLine("Brak komendy");
            return;
        }

        if (cmd[0] == "export")
        {
            try
            {
                if (cmd.Length < 3)
                {
                    Console.WriteLine("Brak argumentów");
                    return;
                }
                simpleExport(int.Parse(cmd[2]), cmd[1]);

            }
            catch
            {
                Console.WriteLine("Błąd zapisu");
            }

        }
        if(cmd[0]== "save")
        {
            try
            {
                if (cmd.Length < 2)
                {
                    Console.WriteLine("Brak argumentów");
                    return;
                }
                sava(cmd[1]);
                Console.WriteLine("Zapisano mapę");
            }
            catch(Exception e)
            {
                Console.WriteLine("Błąd zapisu "+e);
            }
            

        }
        if (cmd[0] == "load")
        {
            try
            {
                if (cmd.Length < 2)
                {
                    Console.WriteLine("Brak argumentów");
                    return;
                }
                load(cmd[1]);
                Console.WriteLine("Wczytano mapę");
            }
            catch(Exception e)
            {
                Console.WriteLine("Błąd odczytu "+e);
            }

        }
        if(cmd[0] == "clear")
        {
            clear();
            Console.WriteLine("Wyczyszczono mapę");
        }
    }


    public struct MapPoint
    {
        public MapPoint(int i, float d, Punkt p)
        {
            index = i;
            distance = d;
            point = p;
        }
        public int index;
        public float distance;
        public Punkt point;
    }
    public void findClosestPoint()
    {
        Punkt p = Punkt.from(Raylib.GetMousePosition());
        Vector2 pv = Raylib.GetMousePosition();

        //tak znam Linq
        if (drogi.Count > 0)
            closestRoadPoint = Enumerable.Range(0, drogi.Count)
                                    .Select(x => CloasesRoadPoint(x))
                                    .MinBy(x => bstopDist(x));
        else
            closestRoadPoint = new Przystanek(-1,0);

        if (punkty.Count > 0)
            closestPoint = Enumerable.Range(0, punkty.Count)
                                    .Select(x => new MapPoint(x, dist(x, p), point2screen(punkty[x])))
                                    .MinBy(x => x.distance);
        else
            closestPoint = new MapPoint(-1, float.PositiveInfinity, new Punkt());
        
        if(przystanki.Count>0)
            closestStop = Enumerable.Range(0, przystanki.Count)
                                    .Select(x => new MapPoint(x,bstopDist(przystanki[x]), point2screen(przystanki[x].pos)))
                                    .MinBy(x => x.distance);
        else
            closestStop = new MapPoint(-1, float.PositiveInfinity, new Punkt());


    }

    public Przystanek CloasesRoadPoint(int index)//projekcja wektorów
    {
        Vector2 p = Raylib.GetMousePosition();
        Vector2 a = point2screen(punkty[drogi[index].a]).v2();
        Vector2 b = point2screen(punkty[drogi[index].b]).v2();

        Vector2 ab = b - a;
        Vector2 ap = p - a;

        float dot = (ab.X * ap.X) + (ab.Y * ap.Y);
        float mul = dot / MathF.Pow(ab.Length(), 2);

        float clamp = MathF.Max(0, MathF.Min(mul, 1));//nie opuszczamy drogi

        Vector2 off = a + ab * clamp;

        // DrawText($"{ab} -> {ap} dot {dot} mul {mul} off {off}",new Punkt(),24,Color.White);
        // DrawPoint(Punkt.from(off),scale/60,Color.Blue);

        return new Przystanek(index, clamp, screen2point(Punkt.from(off)));
    }

    public void hightlightClosestPoint(Color c, int maxDist)
    {
        // findClosestPoint();
        if (closestPoint.distance < maxDist)
            highlightPoint(closestPoint.index, c);
    }

    public float bstopDist(Przystanek p)
    {
        if(p.index==-1)
            return float.PositiveInfinity;
        return (point2screen(p.pos) - Punkt.from(Raylib.GetMousePosition())).v2().Length();
    }
    public void hightlightClosestRoadPoint(Color c, int maxDist)
    {
        // findClosestPoint();

        float crdist = bstopDist(closestRoadPoint);

        if (crdist < maxDist)
            DrawPoint(point2screen(closestRoadPoint.pos), scale / 60, c);
    }
    public void drawFromPointTo(int index, Punkt p, Color c)
    {
        if (index < 0 || index >= punkty.Count)
            return;

        DrawLine(point2screen(punkty[index]), p, 1, c);
    }
    public void deletePoint(int index)
    {
        punkty.RemoveAt(index);
        deleteRoadsForPoint(index);
        
    }
    void deleteRoadsForPoint(int index)
    {
        List<int> toDelete = new List<int>();

        for(int i = 0; i < drogi.Count; i++)
        {
            if(drogi[i].a==index||drogi[i].b==index)
                toDelete.Add(i);
            
            Road droga = drogi[i];

            if(droga.a>index)
                droga.a--;
            if(droga.b>index)
                droga.b--;
            drogi[i] = droga;

        }
        foreach(int i in toDelete.ToArray().Reverse())
        {
            deleteStopsForRoad(i);
            drogi.RemoveAt(i);
        }
    }
    public void deleteRoad(int index)
    {
        drogi.RemoveAt(index);
        deleteStopsForRoad(index);
    }
    public void deleteStopsForRoad(int index)
    {
        List<int> toDelete = new List<int>();

        for(int i = 0; i < przystanki.Count; i++)
        {
            if(przystanki[i].index==index)
                toDelete.Add(i);
            Przystanek przystanek = przystanki[i];
            if(przystanek.index>index)
                przystanek.index--;
            przystanki[i] = przystanek;
        }
        foreach(int i in toDelete.ToArray().Reverse())
        {
            closestRoadPoint.index = -1;
            przystanki.RemoveAt(i);
        }
    }
    public void deleteStop(int index)
    {
        closestRoadPoint.index = -1;
        przystanki.RemoveAt(index);
    }

    public delegate void handlePoint(int i);
    public delegate void handleRoad(int i);
    public delegate void handleStop(int i);
    public void handleClosest(handlePoint hp ,handleRoad hr,handleStop hs)
    {
        if (closestStop.distance < 20)
        {
            hs(closestStop.index);
        }else if (closestPoint.distance < 20)
        {
            hp(closestPoint.index);
        }else if (bstopDist(closestRoadPoint) < 20&&closestRoadPoint.index!=-1)
        {
            hr(closestRoadPoint.index);
        }
    }
    public void recalcPrzystanki()//nie serializujemy pozycji przystanku w świecie więc trzeba ją umieć odtworzyć
    {
        przystanki = przystanki.Select(p =>
        {
            Vector2 dir = punkty[drogi[p.index].b] - punkty[drogi[p.index].a];

            dir*= p.offset;

            p.pos = punkty[drogi[p.index].a] + dir;

            return p;
        }).ToList();
    }

    public void sava(String file)
    {
        XmlSerializer sr =  new XmlSerializer(typeof(Mapa));
        TextWriter writer = new StreamWriter(file);
        sr.Serialize(writer, this);
        writer.Close();
    }
    public void load(String file)
    {
        XmlSerializer sr =  new XmlSerializer(typeof(Mapa));
        TextReader reader = new StreamReader(file);
        Mapa nowa = (Mapa)sr.Deserialize(reader);

        //niestety deserializacja nie może naspisać wartości
        this.punkty = nowa.punkty;
        this.drogi = nowa.drogi;
        this.przystanki = nowa.przystanki;
        this.aspectRatioV = nowa.aspectRatioV;

        recalcPrzystanki();

        reader.Close();
    }
    public void clear()
    {
        this.punkty = new List<Vector2>();
        this.drogi = new List<Road>();
        this.przystanki = new List<Przystanek>();
    }
}