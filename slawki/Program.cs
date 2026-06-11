using System.Collections.Concurrent;
using System.Numerics;
using Raylib_cs;

namespace Slawki;

internal static class Program
{
    static ConcurrentQueue<String[]> polecenia = new ConcurrentQueue<string[]>();
    static List<Sekcja> sekcje = new List<Sekcja>();
    

    static Miasto miasto;

    static Dictionary<string,ICmdHandler> cmdHandlers = new Dictionary<string, ICmdHandler>();

    public static void ConsoleHandler()
    {
        Console.Write("Konsola aktywna\n> ");
        while (true)
        {
            String txt = ReadLine.Read();
            if(txt!=null)
                polecenia.Enqueue(txt.Split(' '));
        }
    }

    public static void handleCMD(String[] cmd)
    {
        if (cmdHandlers.ContainsKey(cmd[0]))
        {
            cmdHandlers[cmd[0]].cmd(cmd.Skip(1).ToArray());


        }
        else//brak przypisanych handlerów
        {
            switch (cmd[0])
            {
                case "exit":
                        Console.WriteLine("do zobaczenia");
                        Environment.Exit(0);
                    break;
                default:
                        Console.WriteLine("nieznana komenda");
                    break;
            }
        }

        
        Console.Write("> ");
    }

    static MouseButton[] guzikiMyszki = new MouseButton[]{ MouseButton.Left,MouseButton.Middle,MouseButton.Right};

    public static void handleMouse()
    {
        Punkt p = Punkt.from(Raylib.GetMousePosition());

        foreach(MouseButton mb in guzikiMyszki){
            if (Raylib.IsMouseButtonPressed(mb))
            {
                foreach(Sekcja sec in sekcje)
                {
                    if(sec.isActive(p))
                        sec.mouseDown(p,mb);
                }
            }
            if (Raylib.IsMouseButtonReleased(mb))
            {
                foreach(Sekcja sec in sekcje)
                {
                    sec.mouseUp(p,mb);
                }
            }
            Punkt mouseDelta = Punkt.from(Raylib.GetMouseDelta());
            if (mouseDelta.x != 0 || mouseDelta.y != 0)
            {
                foreach(Sekcja sec in sekcje)
                {
                    sec.mouseMove(mouseDelta);
                }
            }
        }

        float delta = Raylib.GetMouseWheelMove();
        if (delta != 0)
        {
            foreach(Sekcja sec in sekcje)
            {
                if(sec.isActive(p))
                    sec.mouseScroll(p,delta);
            }
        }
    }

    [System.STAThread]
    public static void Main()
    {
        // Punkt rozmiar = new Punkt(1920,1000);
        Punkt rozmiar = new Punkt(1000,800);

        Raylib.InitWindow(rozmiar.x, rozmiar.y, "Slawki");
        Raylib.SetTargetFPS(60);

        Thread handler = new Thread(ConsoleHandler);
        handler.Start();

        miasto = new Miasto(new Punkt(0,0),rozmiar);
        
        cmdHandlers.Add("miasto",miasto);
        cmdHandlers.Add("mapa",miasto.mapa);
        cmdHandlers.Add("opt",miasto.opt);
        cmdHandlers.Add("sym",miasto.sym);
        sekcje.Add(miasto);

        handleCMD(["mapa","load","slawki.xml"]);
        handleCMD(["opt","reset"]);

        while (!Raylib.WindowShouldClose())
        {
            Raylib.BeginDrawing();

            
            
            handleMouse();

            foreach(Sekcja sec in sekcje)
            {
                sec.Draw();
            }

            Raylib.EndDrawing();
            if(polecenia.TryDequeue(out string[] result))//o byle pierdoły warning, jak by był null to zwrócił by false :<
                handleCMD(result);

            GC.Collect();//luka w pamięcie? chyba nie bo jak mu explicite każe czyścić pamięć to nie ma problemu
        }

        Raylib.CloseWindow();
        Environment.Exit(0);
    }
    
}