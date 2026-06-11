using System.Numerics;
using Raylib_cs;


namespace Slawki;

internal class Miasto : Sekcja,ICmdHandler
{
    public Mapa mapa;
    public OptymalizatorRozkladu opt;
    public SymulatorDnia sym;

    enum Modes
    {
        point,
        road,
        
        busStop,
        house,
        delete,

        
        _count//kinda hack ale co ja mogę
    }
    static Texture2D[] ikony = new Texture2D[] 
    {
        Raylib.LoadTexture("./assets/point.png"),
        Raylib.LoadTexture("./assets/road.png"),
        
        Raylib.LoadTexture("./assets/bus-stop.png"),
        Raylib.LoadTexture("./assets/house.png"),
        Raylib.LoadTexture("./assets/delete.png"),

        

    };

    Modes mode;

    public Miasto(Punkt pos,Punkt size):base(pos,size)
    {
        mapa = new Mapa(pos,size,1.3f);
        opt = new OptymalizatorRozkladu(pos,size,mapa);
        sym = new SymulatorDnia(pos,size,mapa,opt);
    }
    public void DrawCursorAction()
    {
        Punkt p = Punkt.from(Raylib.GetMousePosition());
        DrawImage(ikony[(int)mode],(p+new Punkt(12,-12)).v2(),24);

    }

    public int startRoadPoint = -1;
    
    public override void Draw()
    {   
        Fill(Color.Black);
        mapa.Draw();
        mapa.findClosestPoint();//przeliczamy najbliższe punkty
        
        opt.WykonajKrokEwolucji();
        opt.Draw();
        
        mapa.DrawExtras();

        // sym.Update(1/60);
        sym.Draw();

        DrawCursorAction();
        
        if(mode == Modes.point)
        {
            mapa.DrawPointsId();
        }

        if(mode == Modes.road)
        {
            
            if (startRoadPoint != -1)
            {
                mapa.drawFromPointTo(startRoadPoint,Punkt.from(Raylib.GetMousePosition()),Color.Red);
            }
            mapa.hightlightClosestPoint(Color.Green,10);
            mapa.DrawRoadsId();
        }

        if (mode == Modes.busStop)
        {
            mapa.hightlightClosestRoadPoint(Color.Purple,20);
            mapa.DrawStopsId();
        }
            
        if(mode == Modes.delete)
        {
            
            mapa.handleClosest(
                (int point) =>
                {
                    mapa.highlightPoint(point,Color.Red);
                },
                (int road) =>
                {
                    mapa.highlightRoad(road,Color.Red);
                },
                (int stop) =>
                {
                    mapa.highlightPoint(mapa.przystanki[stop].pos,Color.Red);
                }
            );
            
        }
        if (mode == Modes.house)
        {
            if (mapa.closestStop.distance < 20)
            {
                mapa.highlightPoint(mapa.przystanki[mapa.closestStop.index].pos,Color.Beige);
                opt.RysujRozkladPrzystanku(mapa.closestStop.index);
            }
        }
            
        
        

        
        
    }
    public void cmd(String[] args)
    {
        switch (args[0])
        {
            case "punkty":
                Console.WriteLine("Punkty : ");
                Console.WriteLine(String.Join(',',mapa.punkty.Select(p=>p.ToString())));
            break;
            default:
                Console.WriteLine("nieznana komęda");
            break;
        }
    }

    
    
    public override void mouseScroll(Punkt point, float delta)
    {
        mapa.scale += mapa.scale*delta*0.3f;
    }
    bool middle = false;
    public override void mouseUp(Punkt point, MouseButton mb)
    {
        
        if (mb == MouseButton.Middle)
        {
            middle = false;

            Raylib.SetMouseCursor(MouseCursor.Default);

            return;
        }
            
    }
    public override void mouseDown(Punkt point,MouseButton mb)
    {
        if (mb == MouseButton.Middle)
        {
            middle = true;

            Raylib.SetMouseCursor(MouseCursor.PointingHand);

            return;
        }
        if(mb == MouseButton.Right)
        {
            mode++;
            if (mode == Modes._count)
            {
                mode = 0;
            }

            if(mode==Modes.road)
                startRoadPoint = -1;//usuwamy zaznaczenie z poprzedniego razu

            return;
        }
        //left click zone

        if(mode == Modes.point)
        {
            mapa.punkty.Add(mapa.screen2point(point));
        }

        if(mode == Modes.road)
        {
            if (startRoadPoint == -1)
            {
                mapa.findClosestPoint();
                if(mapa.closestPoint.distance<10)
                    startRoadPoint = mapa.closestPoint.index;
            }
            else
            {
                if (mapa.closestPoint.distance < 10)
                {
                    if(mapa.closestPoint.index==startRoadPoint)
                        return;
                    
                    mapa.drogi.Add(new Mapa.Road(startRoadPoint,mapa.closestPoint.index));
                }

                startRoadPoint = -1;
            }
        }

        if (mode == Modes.busStop)
        {
            if (mapa.bstopDist(mapa.closestRoadPoint) < 20)
            {
                mapa.przystanki.Add(mapa.closestRoadPoint);
            }
        }

        if(mode == Modes.delete)
        {
            mapa.handleClosest(
                (int point) =>
                {
                    mapa.deletePoint(point);
                },
                (int road) =>
                {
                    mapa.deleteRoad(road);
                },
                (int stop) =>
                {
                    mapa.deleteStop(stop);
                }
            );
        }
        
        if (mode == Modes.house)
        {
            if (mapa.closestStop.distance < 20)
            {
                Mapa.Przystanek p = mapa.przystanki[mapa.closestStop.index];
                p.typ++;
                if(p.typ==Mapa.Przystanek.typPrzystanku._count)
                    p.typ = Mapa.Przystanek.typPrzystanku.normal;

                mapa.przystanki[mapa.closestStop.index] = p;
            }
        }
        
        
            
    }
    public override void mouseMove(Punkt delta)
    {
        if (middle)
        {
            mapa.offset -= mapa.offset2local(delta);
            
        }
    }
}

