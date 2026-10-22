// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// Shared by the photograph overlay and the standalone controller head.
internal static class HeadVisual
{
    internal static float Deflection(double value){return double.IsNaN(value)||double.IsInfinity(value)?0:(float)Math.Max(-1,Math.Min(1,value/500.0));}
    internal static double[] Translation(double[] values){return new[]{values[0],-values[2],values[1]};}
    internal static double[] Rotation(double[] values){return new[]{-values[3],-values[4],values[5]};}
    private static readonly Color[] Colors={Color.FromArgb(89,181,255),Color.FromArgb(73,228,190),Color.FromArgb(255,193,87)};
    internal static Color TranslationColor(int index){return Colors[index];}
    internal static readonly Color[] RotationColors={Color.FromArgb(244,112,177),Color.FromArgb(255,144,79),Color.FromArgb(185,151,255)};
    internal static readonly string[] RotationLabels={"A · Kippen","B · Drehen","C · Rollen"};
    internal static void DrawLegend(Graphics g,Rectangle bounds,Font font)
    {
        var state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
        string[] labels={"X · Seitlich","Y · Vor / zurück","Z · Heben / senken"};
        float cell=bounds.Width/3f,row=bounds.Height/2f;
        using(var text=new SolidBrush(Color.FromArgb(68,83,106)))
        {
            for(int i=0;i<3;i++)
            {
                float x=bounds.X+i*cell+3,y=bounds.Y+row*.5f;
                using(var pen=new Pen(Colors[i],3))g.DrawLine(pen,x,y,x+18,y);
                using(var brush=new SolidBrush(Colors[i]))g.FillEllipse(brush,x+7,y-3,6,6);
                g.DrawString(labels[i],font,text,x+25,y-font.GetHeight(g)/2);
                y+=row;
                using(var pen=new Pen(RotationColors[i],3))g.DrawArc(pen,x+1,y-7,15,14,20,285);
                using(var brush=new SolidBrush(RotationColors[i]))g.FillPolygon(brush,new[]{new PointF(x+17,y-8),new PointF(x+16,y-1),new PointF(x+10,y-6)});
                g.DrawString(RotationLabels[i],font,text,x+25,y-font.GetHeight(g)/2);
            }
        }
        g.Restore(state);
    }
    internal static void Draw(Graphics g,RectangleF bounds,double[] values,bool puck)
    {
        var state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
        float cx=bounds.X+bounds.Width/2,cy=bounds.Y+bounds.Height/2,r=Math.Min(bounds.Width,bounds.Height)/2;
        if(puck)
        {
            using(var ring=new LinearGradientBrush(bounds,Color.FromArgb(64,148,235),Color.FromArgb(22,61,106),45))g.FillEllipse(ring,cx-r,cy-r,2*r,2*r);
        }
        using(var cap=new SolidBrush(Color.Black))g.FillEllipse(cap,cx-r*.92f,cy-r*.92f,r*1.84f,r*1.84f);
        if(puck)using(var outline=new Pen(Color.FromArgb(90,90,90),Math.Max(1,r*.016f)))g.DrawEllipse(outline,cx-r*.92f,cy-r*.92f,r*1.84f,r*1.84f);
        double[] translation=Translation(values);
        double[] rotation=Rotation(values);
        var vectors=new[]{new PointF(.58f,0),new PointF(0,-.58f),new PointF(.42f,-.42f)};
        using(var font=new Font("Segoe UI",Math.Max(6.5f,r*.095f),FontStyle.Bold))
        using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
        {
            for(int i=0;i<3;i++)
            {
                var v=vectors[i];float dx=v.X*r,dy=v.Y*r,d=Deflection(translation[i]);
                using(var track=new Pen(Color.FromArgb(95,Colors[i]),Math.Max(1,r*.014f)))g.DrawLine(track,cx-dx,cy-dy,cx+dx,cy+dy);
                using(var pen=new Pen(Colors[i],Math.Max(2,r*.035f)))g.DrawLine(pen,cx,cy,cx+d*dx,cy+d*dy);
                float dot=Math.Max(2.5f,r*.04f);
                using(var b=new SolidBrush(Colors[i]))
                {
                    g.FillEllipse(b,cx+d*dx-dot,cy+d*dy-dot,2*dot,2*dot);
                    g.DrawString(new[]{"X","Y","Z"}[i],font,b,new PointF(cx+dx*1.22f,cy+dy*1.22f),format);
                }
            }
            for(int i=0;i<3;i++)
            {
                float radius=r*(.69f+i*.095f),d=Deflection(rotation[i]);float start=new[]{35f,155f,275f}[i];
                using(var pen=new Pen(Color.FromArgb(120,RotationColors[i]),Math.Max(1,r*.013f)))g.DrawArc(pen,cx-radius,cy-radius,2*radius,2*radius,start,75);
                if(Math.Abs(d)>.001f)
                {
                    float sweep=d*105;
                    using(var pen=new Pen(RotationColors[i],Math.Max(2,r*.032f)))g.DrawArc(pen,cx-radius,cy-radius,2*radius,2*radius,start,sweep);
                    double angle=(start+sweep)*Math.PI/180;float px=cx+radius*(float)Math.Cos(angle),py=cy+radius*(float)Math.Sin(angle);
                    float tx=-(float)Math.Sin(angle)*Math.Sign(d),ty=(float)Math.Cos(angle)*Math.Sign(d),a=Math.Max(3,r*.065f);
                    using(var brush=new SolidBrush(RotationColors[i]))g.FillPolygon(brush,new[]{new PointF(px+tx*a,py+ty*a),new PointF(px-tx*a-ty*a*.7f,py-ty*a+tx*a*.7f),new PointF(px-tx*a+ty*a*.7f,py-ty*a-tx*a*.7f)});
                }
            }
        }
        g.Restore(state);
    }
}
internal sealed class MotionMonitor : Control
{
    internal double[] Values=new double[6];
    internal MotionMonitor(){DoubleBuffered=true;BackColor=Color.White;}
    internal static float Deflection(double value){return HeadVisual.Deflection(value);}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);float size=Math.Min(Width-30,Height-12);
        HeadVisual.Draw(e.Graphics,new RectangleF((Width-size)/2,5,size,size),Values,true);
    }
}
internal sealed class MotionLegend : Control
{
    internal MotionLegend(){DoubleBuffered=true;BackColor=Color.White;Font=new Font("Segoe UI",8);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);HeadVisual.DrawLegend(e.Graphics,ClientRectangle,Font);}
}
internal sealed class MotionValues : Control
{
    internal double[] Values=new double[6];
    internal MotionValues(){DoubleBuffered=true;BackColor=Color.FromArgb(245,247,251);}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        var t=HeadVisual.Translation(Values);var r=HeadVisual.Rotation(Values);
        using(var heading=new Font("Segoe UI",8,FontStyle.Bold))
        using(var axis=new Font("Segoe UI",9,FontStyle.Bold))
        using(var number=new Font("Consolas",11))
        using(var text=new SolidBrush(Color.FromArgb(50,67,92)))
        using(var subtle=new SolidBrush(Color.FromArgb(105,119,141)))
        using(var right=new StringFormat{Alignment=StringAlignment.Far,LineAlignment=StringAlignment.Center})
        using(var border=new Pen(Color.FromArgb(218,226,237)))
        {
            g.DrawRectangle(border,0,0,Width-1,Height-1);g.DrawString("AKTUELLE WERTE",heading,subtle,10,10);
            for(int i=0;i<6;i++)
            {
                int y=34+i*27;Color color=i<3?HeadVisual.TranslationColor(i):HeadVisual.RotationColors[i-3];
                using(var brush=new SolidBrush(color))g.FillRectangle(brush,10,y+8,5,11);
                g.DrawString(new[]{"X","Y","Z","A","B","C"}[i],axis,text,22,y+5);
                double value=i<3?t[i]:r[i-3];if(double.IsNaN(value)||double.IsInfinity(value))value=0;
                value=Math.Round(value,MidpointRounding.AwayFromZero);
                g.DrawString(value.ToString("+0;−0;0"),number,text,new RectangleF(39,y,Width-47,27),right);
                if(i==2)g.DrawLine(border,10,y+28,Width-10,y+28);
            }
        }
    }
}
