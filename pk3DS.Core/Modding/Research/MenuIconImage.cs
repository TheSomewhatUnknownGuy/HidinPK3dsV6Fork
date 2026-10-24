using System;
using System.Drawing;
using System.IO;
using System.Text;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Encode support for `menuicon` (a/0/6/2) - unlike `partyicon`, this GARC's members ARE real,
/// standard <see cref="BFLIM"/> files (confirmed live: a genuine "FLIM" footer, Format=RGB5A1,
/// 64x32, tile-swizzled with <see cref="XLIMOrientation.Rotate90"/>), so decoding an EXISTING member
/// needs nothing new - `new BFLIM(decompressedBytes).GetBitmap(crop: false)` already works, same as
/// every other real BFLIM this codebase already reads. This class only adds ENCODING a fresh
/// replacement, since nothing in this codebase previously needed to write a NEW menuicon-shaped
/// BFLIM. Freshly-encoded members here deliberately use <see cref="XLIMOrientation.None"/> (matching
/// `PartyIconImage`'s own choice) rather than replicating the real files' Rotate90 - a file's own
/// footer records whatever orientation its own pixel bytes were swizzled with, so any decoder
/// (including this codebase's) reads it back correctly regardless of which orientation the ORIGINAL
/// file happened to use; there's no need to match it on write, only to be internally consistent
/// between this class's own Encode and however the result gets decoded later.
/// </summary>
public static class MenuIconImage
{
    public const int Width = PartyIconImage.Width;
    public const int Height = PartyIconImage.Height;
    public const int FooterLength = 40; // FLIMHeader.SIZE
    public const int PixelDataLength = Width * Height * 2; // RGB5A1 = 2 bytes/pixel = 4096

    /// <summary>Encodes a 64x32 (already a full sheet) or 32x32 (single pose, auto-centered) Bitmap into a fresh, uncompressed menuicon member.</summary>
    public static byte[] Encode(Bitmap img)
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

            byte[] footer = new byte[FooterLength];
            Encoding.ASCII.GetBytes("FLIM").CopyTo(footer, 0x00);
            BitConverter.GetBytes((ushort)0xFEFF).CopyTo(footer, 0x04);
            BitConverter.GetBytes((ushort)0x14).CopyTo(footer, 0x06);
            BitConverter.GetBytes(0x07020100).CopyTo(footer, 0x08); // version bytes - constant on every real sample, meaning not needed
            BitConverter.GetBytes((uint)(pixelData.Length + FooterLength)).CopyTo(footer, 0x0C);
            BitConverter.GetBytes((uint)1).CopyTo(footer, 0x10);
            Encoding.ASCII.GetBytes("imag").CopyTo(footer, 0x14);
            BitConverter.GetBytes((uint)0x10).CopyTo(footer, 0x18);
            BitConverter.GetBytes((ushort)Width).CopyTo(footer, 0x1C);
            BitConverter.GetBytes((ushort)Height).CopyTo(footer, 0x1E);
            BitConverter.GetBytes((short)0x80).CopyTo(footer, 0x20); // alignment - constant on every real sample
            footer[0x22] = (byte)XLIMEncoding.RGB5A1;
            footer[0x23] = (byte)XLIMOrientation.None;
            BitConverter.GetBytes((uint)pixelData.Length).CopyTo(footer, 0x24);

            using var ms = new MemoryStream();
            ms.Write(pixelData);
            ms.Write(footer);
            return ms.ToArray();
        }
        finally
        {
            if (!ReferenceEquals(sheet, img)) sheet.Dispose();
        }
    }

    /// <summary>Same auto-fit rule as <see cref="PartyIconImage"/>'s own MakeSheet - see its doc comment (centers a single pose instead of duplicating it into both halves).</summary>
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
