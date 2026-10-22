// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
internal static class IconBuilder
{
    // Native ICO container with PNG frames; alpha is preserved at every size.
    public static void Main(string[] args)
    {
        int[] sizes={16,24,32,48,64,128,256};var frames=new byte[sizes.Length][];
        using(var original=Image.FromFile(args[0]))
        for(int i=0;i<sizes.Length;i++)
        {
            int size=sizes[i];using(var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb))
            {
                using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.CompositingQuality=CompositingQuality.HighQuality;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.DrawImage(original,new Rectangle(0,0,size,size));}
                using(var stream=new MemoryStream()){bitmap.Save(stream,ImageFormat.Png);frames[i]=stream.ToArray();}
            }
        }
        using(var stream=File.Create(args[1]))using(var writer=new BinaryWriter(stream))
        {
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++)
            {
                writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);
                writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;
            }
            foreach(var frame in frames)writer.Write(frame);
        }
        Console.WriteLine("ICO: 16, 24, 32, 48, 64, 128, 256 px");
    }
}
