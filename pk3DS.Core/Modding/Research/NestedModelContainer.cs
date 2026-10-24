using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

/// <summary>
/// The nested container format inside `a/0/8/2` (overworld model placement) - reverse-engineered
/// and verified directly against real ROM data this session (a GARC member decompresses to an "AC"
/// container of 23 real "CP" entries, each wrapping exactly one "CM" entry, each holding several raw
/// model sub-files - decoded 3 levels deep and matched byte-for-byte against a real, user-supplied
/// description of the same structure obtained by hand).
/// <para>
/// Every level shares one format: a 2-letter ASCII tag, a `u16` entry count, a `u32` "base offset"
/// (always <c>0x80</c> in every real sample checked - kept as read, not assumed), then `count`
/// `u32` sub-offsets (relative to this container's own start) - each entry spans
/// <c>[offset[i], offset[i+1])</c>, with the LAST declared offset acting purely as an end marker
/// (so there are really <c>count-1</c> real entries, confirmed live: `AC`'s own `count=24` yields
/// exactly 23 real `CP` children).
/// </para>
/// <para>
/// Recursion is capped at the three confirmed real levels (AC -&gt; CP -&gt; CM) rather than
/// recursing wherever the 2-letter-tag heuristic happens to match - `CM`'s own children are raw
/// model data (mesh/texture/animation blobs) that this class deliberately treats as opaque, since
/// swapping models never needs to understand them, only copy them intact; recursing further would
/// risk misreading arbitrary binary model data that happens to start with two uppercase letters and
/// a small, in-range count as a fourth, non-existent container level.
/// </para>
/// <para>
/// <b>The prefix gap, and why it's preserved rather than understood.</b> `offset[0]` never sits
/// right after the raw header (magic+count+baseOffset+offset table) - there's always a real gap
/// before the first entry, confirmed at every level. At the AC level in a real sample this session,
/// that gap (~255KB) contained mostly zero padding plus one small "CP"-shaped stub - nearly
/// byte-identical to the REAL `CP[0]` header that follows much later, differing only in its own
/// trailing offset field by exactly the amount later content growth would explain. That reads as a
/// stale, orphaned leftover from before this ROM's own Expansion Pack modifications shifted things
/// further out, not live data - the file's own authoritative offset table (the one actually parsed
/// here) never points into it. At the CP level, the same kind of gap was confirmed to be pure zero
/// padding. Either way - stale leftover or intentionally reserved future-expansion space, both
/// patterns this session has already seen elsewhere (AMX scripts' 4096-byte headroom, the models
/// GARC's 62 reserved empty species slots) - the safe move is the same: never try to regenerate or
/// interpret this region, just preserve it byte-for-byte. A swap only ever changes a CM's own
/// content further into the file, so the prefix's size and content need never change at all.
/// </para>
/// </summary>
public sealed class NestedModelContainer
{
    public const int MaxDepth = 3; // AC (depth 1) -> CP (depth 2) -> CM (depth 3); CM's own children are opaque leaves

    public string Magic = "";
    public uint BaseOffsetField; // the u32 at header bytes [4,8) - always 0x80 in every real sample, preserved rather than assumed
    /// <summary>The unexplained byte range between the raw header (magic+count+baseOffset+offsets) and where <c>offset[0]</c> says the first real entry begins - preserved verbatim, never regenerated (see class remarks).</summary>
    public byte[] Prefix = [];
    /// <summary>Each real child's own raw bytes - a container's own encoded form if <see cref="MaxDepth"/> hasn't been reached yet, otherwise an opaque leaf blob.</summary>
    public List<byte[]> Children = [];

    /// <summary>Decodes a top-level area file (already decompressed, if it was LZ11-compressed) as an "AC" container.</summary>
    public static NestedModelContainer Decode(byte[] data) => DecodeAt(data, 0, data.Length, 1);

    private static NestedModelContainer DecodeAt(byte[] data, int start, int len, int depth)
    {
        if (!TryReadHeader(data, start, len, out string magic, out uint baseOffsetField, out uint[] offsets))
            throw new InvalidDataException($"Data at offset {start} (length {len}) doesn't look like a valid container header.");

        var node = new NestedModelContainer { Magic = magic, BaseOffsetField = baseOffsetField };

        int headerLen = 8 + offsets.Length * 4;
        int prefixLen = (int)offsets[0] - headerLen;
        node.Prefix = prefixLen > 0 ? data.Skip(start + headerLen).Take(prefixLen).ToArray() : [];

        int realCount = offsets.Length - 1;
        for (int i = 0; i < realCount; i++)
        {
            int childStart = start + (int)offsets[i];
            int childLen = (int)(offsets[i + 1] - offsets[i]);
            byte[] childBytes = data.Skip(childStart).Take(childLen).ToArray();

            if (depth < MaxDepth && TryReadHeader(data, childStart, childLen, out _, out _, out _))
            {
                var childNode = DecodeAt(data, childStart, childLen, depth + 1);
                node.Children.Add(childNode.Encode());
                node._decodedChildren ??= [];
                node._decodedChildren.Add(childNode);
            }
            else
            {
                node.Children.Add(childBytes);
                node._decodedChildren ??= [];
                node._decodedChildren.Add(null);
            }
        }
        return node;
    }

    /// <summary>Parallel to <see cref="Children"/> - the decoded sub-container for a child that is one (null if that child is an opaque leaf). Kept alongside the raw bytes so a caller can navigate down (e.g. AC -&gt; CP -&gt; CM) without re-decoding.</summary>
    private List<NestedModelContainer> _decodedChildren;
    public NestedModelContainer DecodedChild(int index) => _decodedChildren?[index];

    private static bool TryReadHeader(byte[] data, int start, int len, out string magic, out uint baseOffsetField, out uint[] offsets)
    {
        magic = ""; baseOffsetField = 0; offsets = [];
        if (len < 8) return false;
        char c0 = (char)data[start], c1 = (char)data[start + 1];
        if (c0 < 'A' || c0 > 'Z' || c1 < 'A' || c1 > 'Z') return false;
        ushort count = BitConverter.ToUInt16(data, start + 2);
        if (count < 1 || count > 4096) return false;
        if (8 + count * 4 > len) return false;

        uint baseOff = BitConverter.ToUInt32(data, start + 4);
        var offs = new uint[count];
        for (int i = 0; i < count; i++) offs[i] = BitConverter.ToUInt32(data, start + 8 + i * 4);
        for (int i = 0; i < count; i++) if (offs[i] > (uint)len) return false;
        for (int i = 1; i < count; i++) if (offs[i] < offs[i - 1]) return false;

        magic = $"{c0}{c1}";
        baseOffsetField = baseOff;
        offsets = offs;
        return true;
    }

    /// <summary>Re-encodes this container from its current <see cref="Children"/> (whatever was assigned - the original bytes, a decoded-and-re-encoded sub-container, or a caller's replacement) - offsets are always recomputed fresh, never assumed unchanged, since a replaced child's length can differ from the original. <see cref="Prefix"/> is written back verbatim, unconditionally, right after the header - see the class remarks for why.</summary>
    public byte[] Encode()
    {
        int count = Children.Count + 1; // +1 for the trailing end-marker offset
        int headerLen = 8 + count * 4;
        var childBytesList = Children;

        var offsets = new uint[count];
        uint pos = (uint)(headerLen + Prefix.Length);
        for (int i = 0; i < childBytesList.Count; i++)
        {
            offsets[i] = pos;
            pos += (uint)childBytesList[i].Length;
        }
        offsets[^1] = pos; // end marker

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((byte)Magic[0]);
        bw.Write((byte)Magic[1]);
        bw.Write((ushort)count);
        bw.Write(BaseOffsetField);
        foreach (var o in offsets) bw.Write(o);
        bw.Write(Prefix);
        foreach (var child in childBytesList) bw.Write(child);
        return ms.ToArray();
    }

    /// <summary>Decodes a raw GARC member (transparently decompressing it if it's LZ11-compressed, matching every other decoder in this codebase) as an area file's top-level "AC" container.</summary>
    public static NestedModelContainer DecodeAreaFile(byte[] rawGarcMember)
    {
        byte[] data = rawGarcMember.Length > 0 && rawGarcMember[0] == 0x11 ? LZSS.Decompress(rawGarcMember) : rawGarcMember;
        return Decode(data);
    }

    /// <summary>Re-encodes back to a raw GARC member - always uncompressed, matching this codebase's other encoders (every decoder here already handles reading an uncompressed member transparently).</summary>
    public byte[] EncodeAreaFile() => Encode();
}
