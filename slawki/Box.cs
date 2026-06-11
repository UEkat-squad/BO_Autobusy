using System.Xml.Serialization;
using Raylib_cs;

namespace Slawki;

public abstract class Box
{
    [XmlIgnore]//jedyne do czego używam serializacji to zapis mapy, a to może mi tylko zaszkodzić
    public Punkt pos;
    [XmlIgnore]
    public Punkt size;
    public Box()
    {
        
    }

    public Box(Punkt pos,Punkt size)
    {
        this.pos = pos;
        this.size = size;
    }


    [XmlIgnore]
    public int X
    {
        get
        {
            return pos.x;
        }
        set
        {
            pos.x = value;
        }
    }
    [XmlIgnore]
    public int Y
    {
        get
        {
            return pos.y;
        }
        set
        {
            pos.y = value;
        }
    }
    [XmlIgnore]
    public int W
    {
        get
        {
            return size.x;
        }
        set
        {
            size.x = value;
        }
    }
    [XmlIgnore]
    public int H
    {
        get
        {
            return size.y;
        }
        set
        {
            size.y = value;
        }
    }

    

    public bool isActive()
    {
        Punkt point = Punkt.from(Raylib.GetMousePosition());
        if( point>=pos &&
            point<=size+pos
        ) return true;

        return false;
    }
    public bool isActive(Punkt point)
    {
        if( point>=pos &&
            point<=size+pos
        ) return true;

        return false;
    }
}