using System;
using System.Collections.Generic;

namespace pk3DS.Core.Structures
{
    /// <summary>
    /// The per-zone "EM" (character/NPC) table - one of `ED`'s Mini-packed sub-parts (see
    /// <c>Mini.UnpackMini</c>), specifically the FIRST "EM"-tagged sub-part (a second, unrelated
    /// "EM"-tagged sub-part exists too - a zone-warp/transition table with a different real record
    /// layout despite sharing the same 2-letter tag; this class is only for the NPC one).
    /// <para>
    /// Format, confirmed live this session against real ROM data and a real PawnGarcEditor "pgeif"
    /// placement export: an outer Mini-style header (2-letter magic + u16 count=1 + u32 offset[0]=12
    /// + u32 totalSize, matching the SAME convention `Mini.UnpackMini` itself uses - this sub-part is
    /// always exactly "1 real file" in Mini's own sense, with everything past the header being that
    /// one file's own content), then a real record count (u32 @ 0x0C), a constant `1` (u32 @ 0x10,
    /// purpose unconfirmed - possibly a format version), then that many
    /// <see cref="FieldNpcEntry"/> records (120 bytes each) starting at 0x14, followed by a small,
    /// not-fully-understood "detail pool header" and the shared detail-pool bytes each entry's own
    /// <see cref="FieldNpcEntry.DetailPoolStart"/>/<see cref="FieldNpcEntry.DetailPoolEnd"/> reference
    /// (see that class's remarks for the pool's own confirmed addressing convention).
    /// </para>
    /// </summary>
    public class FieldNpcTable
    {
        public byte[] RawData;
        public List<FieldNpcEntry> Entries = [];

        public FieldNpcTable(byte[] data)
        {
            RawData = (byte[])data.Clone();
            if (RawData.Length < 0x14 || RawData[0] != (byte)'E' || RawData[1] != (byte)'M')
                return;

            uint recordCount = BitConverter.ToUInt32(RawData, 0x0C);
            for (int i = 0; i < recordCount; i++)
            {
                int off = 0x14 + i * FieldNpcEntry.SIZE;
                if (off + FieldNpcEntry.SIZE > RawData.Length) break;
                Entries.Add(new FieldNpcEntry(RawData, off));
            }
        }

        /// <summary>Writes every entry's current bytes back into a same-length clone of <see cref="RawData"/> - use <see cref="AppendEntry"/> instead when the entry count itself needs to change.</summary>
        public byte[] Write()
        {
            byte[] newData = (byte[])RawData.Clone();
            foreach (var e in Entries)
                Array.Copy(e.RawData, 0, newData, e.Offset, FieldNpcEntry.SIZE);
            return newData;
        }

        /// <summary>
        /// Appends a brand-new NPC placement, returning a resized byte array (this instance is not
        /// mutated - re-parse a fresh <see cref="FieldNpcTable"/> from the result to see the new
        /// entry). The new entry is inserted immediately after the last existing record, ahead of the
        /// shared detail pool - which means the pool always shifts back by exactly one record's worth
        /// of bytes (<see cref="FieldNpcEntry.SIZE"/>), for every entry uniformly, not just some past
        /// a threshold (unlike <see cref="OWTrainerFile.AppendEntry"/>, which can insert mid-pool for
        /// a non-last area - this table only ever has one contiguous record run followed by one
        /// pool, so insertion is always at that single boundary).
        /// <para>
        /// The caller is expected to have cloned <paramref name="newEntry"/> from a real, already-valid
        /// entry (the established "borrow a known-working example rather than synthesize sub-data
        /// from scratch" approach) - its <see cref="FieldNpcEntry.DetailPoolStart"/>/
        /// <see cref="FieldNpcEntry.DetailPoolEnd"/> should still hold that template's own PRE-shift
        /// values; this method shifts every entry's pool-pointer fields uniformly by
        /// <see cref="FieldNpcEntry.SIZE"/> AFTER inserting, including the newly-appended entry - so a
        /// straight byte-for-byte clone of the template ends up correctly pointing at the same
        /// (now-shifted) shared detail data as the template it was cloned from, with no special
        /// pre-adjustment required by the caller.
        /// </para>
        /// </summary>
        public byte[] AppendEntry(FieldNpcEntry newEntry)
        {
            uint recordCount = BitConverter.ToUInt32(RawData, 0x0C);
            int insertPos = 0x14 + (int)recordCount * FieldNpcEntry.SIZE;

            var newData = new byte[RawData.Length + FieldNpcEntry.SIZE];
            Array.Copy(RawData, 0, newData, 0, insertPos);
            Array.Copy(newEntry.RawData, 0, newData, insertPos, FieldNpcEntry.SIZE);
            Array.Copy(RawData, insertPos, newData, insertPos + FieldNpcEntry.SIZE, RawData.Length - insertPos);

            BitConverter.GetBytes(recordCount + 1).CopyTo(newData, 0x0C);
            uint oldTotal = BitConverter.ToUInt32(RawData, 0x08);
            BitConverter.GetBytes(oldTotal + (uint)FieldNpcEntry.SIZE).CopyTo(newData, 0x08);

            var reparsed = new FieldNpcTable(newData);
            foreach (var e in reparsed.Entries)
            {
                e.DetailPoolStart += FieldNpcEntry.SIZE;
                e.DetailPoolEnd += FieldNpcEntry.SIZE;
                Array.Copy(e.RawData, 0, newData, e.Offset, FieldNpcEntry.SIZE);
            }

            return newData;
        }
    }
}
