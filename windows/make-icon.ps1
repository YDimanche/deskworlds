# Generates Deskworlds.ico: a dark green rounded square with three wavy sediment layers,
# echoing the menu-bar logo. Sizes 16/32/48 as classic BMP entries plus 256 as a PNG entry.
param([string]$OutPath = "$PSScriptRoot\Deskworlds\Deskworlds.ico")

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing.dll -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class IcoWriter
{
    public static void Write(string path, int[] sizes)
    {
        var entries = new List<Entry>();
        foreach (int size in sizes)
        {
            using (Bitmap bitmap = Make(size))
            {
                entries.Add(new Entry { Width = size, Data = size >= 256 ? PngBytes(bitmap) : BmpBytes(bitmap) });
            }
        }
        using (FileStream stream = new FileStream(path, FileMode.Create))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)entries.Count);
            uint offset = (uint)(6 + 16 * entries.Count);
            foreach (Entry entry in entries)
            {
                byte sizeByte = entry.Width >= 256 ? (byte)0 : (byte)entry.Width;
                writer.Write(sizeByte);
                writer.Write(sizeByte);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)entry.Data.Length);
                writer.Write(offset);
                offset += (uint)entry.Data.Length;
            }
            foreach (Entry entry in entries) writer.Write(entry.Data);
        }
    }

    private sealed class Entry { public int Width; public byte[] Data; }

    private static byte[] PngBytes(Bitmap bitmap)
    {
        using (MemoryStream stream = new MemoryStream())
        {
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }

    private static byte[] BmpBytes(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        Rectangle rect = new Rectangle(0, 0, width, height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = data.Stride;
        byte[] source = new byte[stride * height];
        Marshal.Copy(data.Scan0, source, 0, source.Length);
        bitmap.UnlockBits(data);
        int maskStride = ((width + 31) / 32) * 4;
        int pixelBytes = width * height * 4;
        byte[] buffer = new byte[40 + pixelBytes + maskStride * height];
        using (MemoryStream stream = new MemoryStream(buffer))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(40u);            // BITMAPINFOHEADER size
            writer.Write(width);          // biWidth
            writer.Write(height * 2);     // biHeight: pixels + AND mask
            writer.Write((ushort)1);      // biPlanes
            writer.Write((ushort)32);     // biBitCount
            writer.Write(0u);             // biCompression = BI_RGB
            writer.Write((uint)(pixelBytes + maskStride * height));
            for (int i = 0; i < 4; i++) writer.Write(0u);
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * stride + x * 4;
                    writer.Write(source[i]);     // B
                    writer.Write(source[i + 1]); // G
                    writer.Write(source[i + 2]); // R
                    writer.Write(source[i + 3]); // A
                }
            }
            // AND mask stays zeroed: alpha decides transparency.
        }
        return buffer;
    }

    public static Bitmap Make(int size)
    {
        Bitmap bitmap = new Bitmap(size, size);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            int radius = Math.Max(1, size * 18 / 100);
            GraphicsPath frame = new GraphicsPath();
            frame.AddArc(0, 0, 2 * radius, 2 * radius, 180, 90);
            frame.AddArc(size - 2 * radius, 0, 2 * radius, 2 * radius, 270, 90);
            frame.AddArc(size - 2 * radius, size - 2 * radius, 2 * radius, 2 * radius, 0, 90);
            frame.AddArc(0, size - 2 * radius, 2 * radius, 2 * radius, 90, 90);
            frame.CloseFigure();
            using (LinearGradientBrush background = new LinearGradientBrush(
                new Rectangle(0, 0, size, size),
                Color.FromArgb(255, 13, 31, 26), Color.FromArgb(255, 27, 60, 48), 90f))
            {
                g.FillPath(background, frame);
            }
            Color[] layers = new Color[]
            {
                Color.FromArgb(255, 38, 92, 73),
                Color.FromArgb(255, 56, 124, 97),
                Color.FromArgb(255, 82, 158, 121),
            };
            for (int i = 0; i < 3; i++)
            {
                float y0 = size * (0.52f + 0.145f * i);
                float amp = size * 0.055f;
                float thickness = size * 0.17f;
                GraphicsPath band = new GraphicsPath();
                band.AddBezier(0, y0, size * 0.35f, y0 - amp, size * 0.65f, y0 + amp, size, y0 - amp * 0.6f);
                band.AddLine(size, y0 - amp * 0.6f + thickness, 0, y0 + thickness);
                band.CloseFigure();
                using (SolidBrush brush = new SolidBrush(layers[i]))
                {
                    g.FillPath(brush, band);
                }
                band.Dispose();
            }
            frame.Dispose();
        }
        return bitmap;
    }
}
"@

$dir = Split-Path -Parent $OutPath
if (!(Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
[IcoWriter]::Write($OutPath, @(256, 48, 32, 16))
"Ico written: $OutPath ($((Get-Item $OutPath).Length) bytes)"
