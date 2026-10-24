using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// Decode/encode for `partyicon`'s (a257) per-species image container - confirmed live against real
/// ROM data to be a bespoke, non-standard wrapper (NOT a real BCLIM/BFLIM - it has no CLIM/FLIM
/// footer at all), built by whatever internal tool produced this asset originally: a fixed 0x80
/// (128) byte header - a `13 12 04 15` tag, a literal ASCII "texture\0" chunk name, a few
/// mostly-constant fields (one of which, at header offset 0x18, IS content-dependent: the pixel
/// payload's own byte length), the embedded original art filename (ASCII, null-terminated, e.g.
/// "poke_icon_001_m_001.tga" or "poke_icon_exp_0982.tga" - see <see cref="PartyIconCatalog"/>,
/// which reads this same filename as its ground truth for species/form identification) starting at
/// offset 0x28, width/height/format fields at 0x68 (confirmed 64/32/RGB5A1 on every real sample
/// checked), then 0xFF padding out to the fixed 0x80 header length - followed by a 4096-byte RGB5A1
/// pixel payload, CTR-tile-swizzled (see <see cref="XLIMOrienter"/>) with
/// <see cref="XLIMOrientation.None"/> (confirmed live: decoding with the SAME swizzle algorithm
/// already proven correct for `menuicon`'s real BFLIM data, but with Orientation=None instead of
/// menuicon's Rotate90, produces a clean, correctly-shaped Bulbasaur - visually confirmed by the
/// user). The whole thing is then usually (every real sample checked) LZ11-compressed on disk, same
/// as `menuicon` and most other per-file GARC members in this ROM.
/// </summary>
public static class PartyIconImage
{
    public const int HeaderLength = 0x80;
    public const int FilenameOffset = 0x28;
    public const int FilenameRegionLength = 0x68 - 0x28; // 64 bytes available before the width/height/format field
    public const int DimensionFieldOffset = 0x68;
    public const int Width = 64;
    public const int Height = 32;
    public const int PixelDataLength = Width * Height * 2; // RGB5A1 = 2 bytes/pixel = 4096

    /// <summary>Decodes a raw (possibly LZ11-compressed) partyicon member to a 64x32 Bitmap - both animation frames side by side, uncropped, matching what <see cref="Encode"/> expects back.</summary>
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

    /// <summary>
    /// Encodes a 64x32 (already a full sheet) or 32x32 (single pose, auto-centered) Bitmap into a
    /// fresh, uncompressed partyicon member - same header shape
    /// as every real entry, with a caller-supplied embedded filename (purely cosmetic metadata as far
    /// as this codebase can tell - nothing reads it back at runtime - but kept realistic:
    /// "poke_icon_&lt;id&gt;_m_001" for a stock-range replacement, matching the real convention).
    /// </summary>
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

    /// <summary>
    /// Auto-fits any source image into the required 64x32 canvas: a wide (~2:1 or wider) source is
    /// assumed to already be an explicit full-canvas sheet and is stretched directly onto it;
    /// anything else (square or tall) is assumed to be a single pose and is centered horizontally at
    /// its natural 32x32 aspect within the 64-wide canvas.
    /// <para>
    /// This used to stretch a single pose into BOTH 32x32 halves identically, on the assumption the
    /// canvas held two independent side-by-side animation frames - confirmed wrong by decoding real
    /// ROM data (Bulbasaur, Marowak, ...): every real party/menu icon is ONE continuous sprite
    /// centered across the full 64px width (spanning roughly x=16-48, not confined to either half),
    /// not two separate 32x32 frames. Duplicating a single-pose import into both halves therefore
    /// visibly rendered it twice side by side in-game - the exact "cloned itself twice" bug report
    /// this centering fix addresses.
    /// </para>
    /// </summary>
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
