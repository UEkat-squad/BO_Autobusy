using System.Numerics;
using Raylib_cs;

namespace Slawki;

public abstract class Ctx:Box
{
    public Ctx(){
        
    }
    public Ctx(Punkt pos,Punkt size):base(pos,size)
    {
        
    }

    public void Fill(Color c)
    {
        Raylib.DrawRectangle(X,Y,W,H,c);   
    }
    public void DrawPoint(Punkt p,float size,Color c)
    {
        if(!isActive(p)) return;

        Raylib.DrawCircle(p.x+X,p.y+Y,size,c);

    }
    public void DrawLine(Punkt a,Punkt b,float thick,Color c)
    {
        Punkt oa = a+pos;
        Punkt ob = b+pos;

        // if (!isActive(oa))
        // {
        //     Punkt tmp = oa;
        //     oa = ob;
        //     ob = tmp;
        // }

        //couldn't care less about chcecking for stuff
        //it's way too complicated for what it's worth

        Raylib.DrawLineEx(oa.v2(),ob.v2(),thick,c);

    }
    public void DrawImage(Texture2D img,Vector2 pos,float scale)
    {
        
        //nie działa
        Raylib.DrawTextureEx(img,pos,0,scale/img.Width,Color.White);
        
    }

    public void DrawLine(Vector2 a,Vector2 b,float thick,Color c)
    {
        DrawLine(Punkt.from(a),Punkt.from(b),thick,c);

    }

    public void DrawText(String text,Punkt pos,int size,Color c)
    {
        Raylib.DrawText(text,pos.x,pos.y,size,c);
    }

}