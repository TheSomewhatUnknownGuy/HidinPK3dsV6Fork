using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using pk3DS.Core.CTR;

namespace pk3DS.Core.Modding.Research;

public sealed class NestedModelContainer
{
    public const int MaxDepth = 3; // AC (depth 1) -> CP (depth 2) -> CM (depth 3); CM's own children are opaque leaves

    public string Magic = "";
    public uint BaseOffsetField; // the u32 at header bytes [4,8) - always 0x80 in every real sample, preserved rather than assumed
    public byte[] Prefix = [];
    public List<byte[]> Children = [];

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

    public static NestedModelContainer DecodeAreaFile(byte[] rawGarcMember)
    {
        byte[] data = rawGarcMember.Length > 0 && rawGarcMember[0] == 0x11 ? LZSS.Decompress(rawGarcMember) : rawGarcMember;
        return Decode(data);
    }

    public byte[] EncodeAreaFile() => Encode();
}
