using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

public static class PartyIconImage
{
    public const int HeaderLength = 0x80;
    public const int FilenameOffset = 0x28;
    public const int FilenameRegionLength = 0x68 - 0x28; // 64 bytes available before the width/height/format field
    public const int DimensionFieldOffset = 0x68;
    public const int Width = 64;
    public const int Height = 32;
    public const int PixelDataLength = Width * Height * 2; // RGB5A1 = 2 bytes/pixel = 4096

    public static Bitmap Decode(byte[] rawMember)
    {
        byte[] data = rawMember[0] == 0x11 ? LZSS.Decompress(rawMember) : rawMember;
        if (data.Length < HeaderLength + PixelDataLength)
            throw new ArgumentException($"partyicon member is too short ({data.Length} bytes) to contain a {HeaderLength}-byte header + {PixelDataLength}-byte RGB5A1 payload.");

        byte[] payload = data[HeaderLength..(HeaderLength + PixelDataLength)];
        var pixels = PixelConverter.GetPixels(payload, XLIMEncoding.RGB5A1).ToArray();
        var orienter = new XLIMOrienter(Width, Height, XLIMOrientation.None);
        var bmp = new Bitmap(Width, Height);
        for (uint i = 0; i < pixels.Length; i++)
        {
            var c = orienter.Get(i);
            if (c.X >= Width || c.Y >= Height) continue;
            uint val = pixels[i];
            bmp.SetPixel((int)c.X, (int)c.Y, Color.FromArgb((int)(val >> 24 & 0xFF), (int)(val >> 16 & 0xFF), (int)(val >> 8 & 0xFF), (int)(val & 0xFF)));
        }
        return bmp;
    }

    public static byte[] Encode(Bitmap img, string embeddedFilename)
    {
        Bitmap sheet = img.Width == Width && img.Height == Height ? img : MakeSheet(img);
        try
        {
            byte[] pixelData = new byte[PixelDataLength];
            var orienter = new XLIMOrienter(Width, Height, XLIMOrientation.None);
            for (uint i = 0; i < (uint)(Width * Height); i++)
            {
                var c = orienter.Get(i);
                Color color = (c.X < Width && c.Y < Height) ? sheet.GetPixel((int)c.X, (int)c.Y) : Color.FromArgb(0, 0, 0, 0);
                ushort val = BCLIM.GetRGBA5551(color);
                pixelData[i * 2] = (byte)(val & 0xFF);
                pixelData[i * 2 + 1] = (byte)(val >> 8);
            }

            byte[] header = new byte[HeaderLength];
            BitConverter.GetBytes((uint)0x15041213).CopyTo(header, 0x00); // tag - constant on every real sample
            BitConverter.GetBytes((uint)1).CopyTo(header, 0x04);
            Encoding.ASCII.GetBytes("texture\0").CopyTo(header, 0x08);
            BitConverter.GetBytes((uint)0x1068).CopyTo(header, 0x10); // constant on every real sample
            BitConverter.GetBytes(uint.MaxValue).CopyTo(header, 0x14); // sentinel
            BitConverter.GetBytes((uint)PixelDataLength).CopyTo(header, 0x18);
            // 0x1C-0x27 (12 bytes): reserved, zero on every real sample - already zero in a fresh array.

            byte[] nameBytes = Encoding.ASCII.GetBytes((string.IsNullOrEmpty(embeddedFilename) ? "poke_icon_custom" : embeddedFilename) + ".tga");
            int nameLen = Math.Min(nameBytes.Length, FilenameRegionLength - 1); // leave room for the null terminator
            Array.Copy(nameBytes, 0, header, FilenameOffset, nameLen);
            // header[FilenameOffset + nameLen] is already 0 (null terminator) and the rest of the
            // filename region is already zero-padded in a fresh array.

            BitConverter.GetBytes((ushort)Width).CopyTo(header, DimensionFieldOffset + 0x00);
            BitConverter.GetBytes((ushort)Height).CopyTo(header, DimensionFieldOffset + 0x02);
            header[DimensionFieldOffset + 0x04] = (byte)(0x10 | (byte)XLIMEncoding.RGB5A1); // high-nibble flag constant on every real sample, low nibble = format
            header[DimensionFieldOffset + 0x05] = 0;
            BitConverter.GetBytes((ushort)1).CopyTo(header, DimensionFieldOffset + 0x06);
            for (int i = DimensionFieldOffset + 0x08; i < HeaderLength; i++)
                header[i] = 0xFF; // matches every real sample's trailing padding

            return header.Concat(pixelData).ToArray();
        }
        finally
        {
            if (!ReferenceEquals(sheet, img)) sheet.Dispose();
        }
    }

    private static Bitmap MakeSheet(Bitmap frame)
    {
        var sheet = new Bitmap(Width, Height);
        using var g = Graphics.FromImage(sheet);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        if (frame.Width >= frame.Height * 1.5)
        {
            g.DrawImage(frame, 0, 0, Width, Height);
        }
        else
        {
            g.DrawImage(frame, (Width - Height) / 2, 0, Height, Height);
        }
        return sheet;
    }
}
