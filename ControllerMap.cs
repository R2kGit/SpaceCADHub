// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

internal sealed class ControllerMap : Control
{
    internal const int ImageWidth=1404,ImageHeight=1120;
    private Image device;
    private readonly GraphicsPath[] keys=CreatePaths();
    private readonly ToolTip tips=new ToolTip{InitialDelay=350,ReshowDelay=100,AutoPopDelay=10000,ShowAlways=true};
    private int hovered=-1;
    internal int Selected,Pressed;
    internal double[] Values=new double[6];
    internal Action<int> SelectKey;
    internal Func<int,string> DescribeKey;
    internal ControllerMap()
    {
        DoubleBuffered=true;BackColor=Color.White;
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets","controller.png");
        if(File.Exists(path))using(var source=Image.FromFile(path))device=new Bitmap(source);
    }
    internal static GraphicsPath[] CreatePaths()
    {
        return new[]{
            Polygon(326,264,367,280,340,362,290,350),
            Polygon(390,173,425,208,384,258,340,236),
            Polygon(422,145,489,102,520,143,455,191),
            Polygon(518,89,605,55,621,102,543,136),
            Polygon(635,47,727,35,728,80,640,91),
            Polygon(758,37,851,58,836,98,754,81),
            Polygon(397,280,462,219,499,263,442,314),
            Polygon(468,215,540,167,562,208,501,253),
            Polygon(548,161,638,123,656,173,574,204),
            Polygon(649,122,747,113,751,162,663,177),
            Polygon(811,659,906,622,922,676,829,709),
            Polygon(910,621,1000,592,1015,646,929,679),
            Polygon(832,718,920,686,937,734,847,766),
            Ellipse(578,678,91,94),
            Polygon(678,768,795,733,812,783,696,822),
            Polygon(1054,112,1150,139,1141,175,1044,147),
            Polygon(1194,152,1293,182,1284,219,1185,193),
            RingHalf(90),RingHalf(-90)
        };
    }
    private static GraphicsPath Polygon(params float[] xy)
    {
        var p=new GraphicsPath();int n=xy.Length/2;
        var inside=new PointF[n];var outside=new PointF[n];var points=new PointF[n];
        for(int i=0;i<n;i++)points[i]=new PointF(xy[i*2],xy[i*2+1]);
        for(int i=0;i<n;i++)
        {
            var v=points[i];var previous=points[(i+n-1)%n];var next=points[(i+1)%n];
            inside[i]=new PointF(v.X+(previous.X-v.X)*.12f,v.Y+(previous.Y-v.Y)*.12f);
            outside[i]=new PointF(v.X+(next.X-v.X)*.12f,v.Y+(next.Y-v.Y)*.12f);
        }
        for(int i=0;i<n;i++)
        {
            var v=points[i];var a=inside[i];var b=outside[i];
            p.AddBezier(a,new PointF(a.X+(v.X-a.X)*.67f,a.Y+(v.Y-a.Y)*.67f),new PointF(b.X+(v.X-b.X)*.67f,b.Y+(v.Y-b.Y)*.67f),b);
            p.AddLine(b,inside[(i+1)%n]);
        }
        p.CloseFigure();return p;
    }
    private static GraphicsPath Ellipse(float x,float y,float w,float h){var p=new GraphicsPath();p.AddEllipse(x,y,w,h);return p;}
    private static GraphicsPath RingHalf(float start)
    {
        var p=new GraphicsPath();p.AddArc(480,235,427,427,start,180);p.AddArc(510,265,367,367,start+180,-180);p.CloseFigure();return p;
    }
    internal RectangleF ImageRect
    {
        get{float scale=Math.Min((Width-12f)/ImageWidth,(Height-12f)/ImageHeight);return new RectangleF((Width-ImageWidth*scale)/2,(Height-ImageHeight*scale)/2,ImageWidth*scale,ImageHeight*scale);}
    }
    internal int HitImage(PointF point){for(int i=0;i<keys.Length;i++)if(keys[i].IsVisible(point))return i;return -1;}
    private int Hit(Point point){var r=ImageRect;return HitImage(new PointF((point.X-r.X)*ImageWidth/r.Width,(point.Y-r.Y)*ImageHeight/r.Height));}
    internal bool IsHighlighted(int index){return (Pressed&(1<<index))!=0 || index==hovered;}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;var r=ImageRect;
        if(device!=null)g.DrawImage(device,r);else TextRenderer.DrawText(g,"Controllerbild fehlt",Font,ClientRectangle,Color.DimGray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        var state=g.Save();float scale=r.Width/ImageWidth;g.TranslateTransform(r.X,r.Y);g.ScaleTransform(scale,scale);
        // Align the drawn face with the inner cap, not the outer wheel.
        HeadVisual.Draw(g,new RectangleF(533,269,322,322),Values,false);
        for(int i=0;i<keys.Length;i++)
        {
            bool down=(Pressed&(1<<i))!=0;if(!IsHighlighted(i))continue;
            Color color=down?Color.FromArgb(0,185,105):Color.FromArgb(30,105,233);
            using(var pen=new Pen(color,2.5f/scale)){pen.LineJoin=LineJoin.Round;g.DrawPath(pen,keys[i]);}
        }
        g.Restore(state);
    }
    internal string TooltipText(int index){return DescribeKey==null?"":DescribeKey(index);}
    internal void RefreshTooltip(){if(hovered>=0)tips.SetToolTip(this,TooltipText(hovered));}
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);int hit=Hit(e.Location);Cursor=hit>=0?Cursors.Hand:Cursors.Default;
        if(hit==hovered)return;hovered=hit;tips.SetToolTip(this,hit<0?null:TooltipText(hit));Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hovered=-1;tips.SetToolTip(this,null);Cursor=Cursors.Default;Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;int hit=Hit(e.Location);if(hit<0)return;
        Selected=hit;Invalidate();if(SelectKey!=null)SelectKey(hit);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){tips.Dispose();foreach(var path in keys)path.Dispose();if(device!=null)device.Dispose();}base.Dispose(disposing);
    }
}

internal sealed class DeviceStatusCard : Control
{
    internal bool Connected;
    internal string Detail="Verbindung wird geprüft";
    internal DeviceStatusCard(){DoubleBuffered=true;}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        g.Clear(Connected?Color.FromArgb(24,62,59):Color.FromArgb(55,44,42));
        Color accent=Connected?Color.FromArgb(86,223,174):Color.FromArgb(255,185,120);
        float sx=Width/178f,sy=Height/98f;
        using(var dot=new SolidBrush(accent))g.FillEllipse(dot,10*sx,20*sy,7*sx,7*sy);
        using(var title=new Font(Font.FontFamily,Font.Size*.9f,FontStyle.Bold))TextRenderer.DrawText(g,Connected?"Verbunden":"Nicht verbunden",title,new Rectangle((int)(23*sx),(int)(11*sy),(int)(149*sx),(int)(28*sy)),accent,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        using(var detail=new Font(Font.FontFamily,Font.Size*.8f))TextRenderer.DrawText(g,Detail,detail,new Rectangle((int)(10*sx),(int)(43*sy),(int)(158*sx),(int)(50*sy)),Connected?Color.FromArgb(170,218,203):Color.FromArgb(210,191,179),TextFormatFlags.Left|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
    }
}
