using System.Numerics;
using Raylib_cs;

namespace Slawki;

abstract class Sekcja : Ctx
{
    public Sekcja(Punkt pos,Punkt size):base(pos,size)
    {
    }
    
    public abstract void Draw();
    
    public abstract void mouseDown(Punkt point,MouseButton mb);

    public abstract void mouseUp(Punkt point,MouseButton mb);

    public abstract void mouseScroll(Punkt point,float delta);
    
    public abstract void mouseMove(Punkt delta);
}