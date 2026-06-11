using System.Numerics;
using System.Xml.Serialization;

namespace Slawki;

public struct Punkt
{
    [XmlAttribute]
    public int x,y;
    public Punkt()
    {
        this.x = 0;this.y = 0;
    }
    public Punkt(int x)
    {
        this.x = x;this.y = x;
    }
    public Punkt(int x,int y)
    {
        this.x = x;this.y = y;
    }

    public static Punkt operator -(Punkt a,Punkt b)
    {
        return new Punkt(a.x-b.x,a.y-b.y);
    }
    public static Punkt operator +(Punkt a,Punkt b)
    {
        return new Punkt(a.x+b.x,a.y+b.y);
    }
    public static Punkt operator *(Punkt a,Punkt b)
    {
        return new Punkt(a.x*b.x,a.y*b.y);
    }
    public static Punkt operator /(Punkt a,Punkt b)
    {
        return new Punkt(a.x/b.x,a.y/b.y);
    }
    public static bool operator <(Punkt a,Punkt b)
    {
        return a.x<b.x && a.y<b.y;
    }
    public static bool operator >(Punkt a,Punkt b)
    {
        return a.x>b.x && a.y>b.y;
    }
    public static bool operator <=(Punkt a,Punkt b)
    {
        return a.x<=b.x && a.y<=b.y;
    }
    public static bool operator >=(Punkt a,Punkt b)
    {
        return a.x>=b.x && a.y>=b.y;
    }
    public static bool operator !=(Punkt a,Punkt b)
    {
        return a.x!=b.x || a.y!=b.y;
    }
    public static bool operator ==(Punkt a,Punkt b)
    {
        return a.x==b.x && a.y==b.y;
    }

    public float distance(Punkt p)
    {
        return MathF.Sqrt(MathF.Pow(this.x-p.x,2)+MathF.Pow(this.y-p.y,2));
    }
    public static Punkt from(Vector2 v)
    {
        return new Punkt((int)v.X,(int)v.Y);
    }
    public override bool Equals(object? obj)
    {
        return base.Equals(obj);
    }
    public override int GetHashCode()
    {
        return base.GetHashCode();
    }
    public override string ToString()
    {
        return $"({x},{y})";
    }
    public Punkt copy()
    {
        return new Punkt(this.x,this.y);
    }
    public Vector2 v2()
    {
        return new Vector2(x,y);
    }
}