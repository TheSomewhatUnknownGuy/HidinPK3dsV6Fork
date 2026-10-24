using System;
using System.Collections.Generic;

namespace pk3DS.Core.Structures
{
    public class OWTrainerFile
    {
        public byte[] RawData;
        public List<List<OWTrainerEntry>> Areas;
        public int AreaCount;

        public OWTrainerFile(byte[] data)
        {
            RawData = (byte[])data.Clone();
            Areas = new List<List<OWTrainerEntry>>();

            ushort magic = BitConverter.ToUInt16(RawData, 0x00);
            if (magic != 0x5445) // 'E', 'T' (Little Endian -> 0x5445)
            {
                return;
            }

            AreaCount = RawData[0x02];

            int offsetIndex = 0x04;
            uint[] sectionOffsets = new uint[AreaCount];
            for (int i = 0; i < AreaCount; i++)
            {
                sectionOffsets[i] = BitConverter.ToUInt32(RawData, offsetIndex);
                offsetIndex += 4;
            }

            uint totalSize = BitConverter.ToUInt32(RawData, offsetIndex);
            offsetIndex += 4;

            for (int a = 0; a < AreaCount; a++)
            {
                var areaList = new List<OWTrainerEntry>();
                if (sectionOffsets[a] + 4 <= (uint)RawData.Length)
                {
                    uint entryCount = BitConverter.ToUInt32(RawData, (int)sectionOffsets[a]);
                    for (int i = 0; i < entryCount; i++)
                    {
                        int currentOffset = (int)sectionOffsets[a] + 4 + i * OWTrainerEntry.SIZE;
                        if (currentOffset + OWTrainerEntry.SIZE > RawData.Length) break;

                        var entry = new OWTrainerEntry(RawData, currentOffset);
                        if (entry.IsValid())
                        {
                            areaList.Add(entry);
                        }
                    }
                }

                Areas.Add(areaList);
            }
        }

        public byte[] Write()
        {
            byte[] newData = (byte[])RawData.Clone();

            foreach (var area in Areas)
            {
                foreach (var entry in area)
                {
                    Array.Copy(entry.RawData, 0, newData, entry.Offset, OWTrainerEntry.SIZE);
                }
            }

            return newData;
        }

        public byte[] AppendEntry(int areaIndex, OWTrainerEntry newEntry)
        {
            if (areaIndex < 0 || areaIndex >= AreaCount)
                throw new ArgumentOutOfRangeException(nameof(areaIndex));

            int offsetIndex = 0x04;
            var sectionOffsets = new uint[AreaCount];
            for (int i = 0; i < AreaCount; i++)
            {
                sectionOffsets[i] = BitConverter.ToUInt32(RawData, offsetIndex);
                offsetIndex += 4;
            }
            uint totalSize = BitConverter.ToUInt32(RawData, offsetIndex);

            uint areaStart = sectionOffsets[areaIndex];
            uint areaEnd = areaIndex + 1 < AreaCount ? sectionOffsets[areaIndex + 1] : totalSize;
            uint areaLen = areaEnd - areaStart;

            uint count = BitConverter.ToUInt32(RawData, (int)areaStart);
            uint entriesEndRel = 4 + count * (uint)OWTrainerEntry.SIZE;
            uint subDataLen = areaLen > entriesEndRel ? areaLen - entriesEndRel : 0;

            // Sub-data size for a new stationary trainer (12 + 28 + 28 + 28 = 96 bytes)
            const uint NEW_SUBDATA_SIZE = 96;
            uint addedAreaBytes = (uint)OWTrainerEntry.SIZE + NEW_SUBDATA_SIZE; // 180 bytes

            byte[] newData = new byte[RawData.Length + addedAreaBytes];

            // 1. Copy everything before the new entry insertion position (headers, prior areas, area's existing entries)
            uint entryInsertPos = areaStart + entriesEndRel;
            Array.Copy(RawData, 0, newData, 0, (int)entryInsertPos);

            // 2. Prepare sub-data blob (96 bytes) cloned from template (or default stationary bounding boxes)
            byte[] subDataBlob = new byte[NEW_SUBDATA_SIZE];
            if (newEntry.MoveDataOffset != 0 && newEntry.MoveDataOffset != 0xFFFFFFFF &&
                areaStart + newEntry.MoveDataOffset + 12 <= RawData.Length)
            {
                Array.Copy(RawData, (int)(areaStart + newEntry.MoveDataOffset), subDataBlob, 0, 12);
            }
            if (newEntry.TalkCollisionOffset != 0 && newEntry.TalkCollisionOffset != 0xFFFFFFFF &&
                areaStart + newEntry.TalkCollisionOffset + 28 <= RawData.Length)
            {
                Array.Copy(RawData, (int)(areaStart + newEntry.TalkCollisionOffset), subDataBlob, 12, 28);
            }
            if (newEntry.CollisionOffset != 0 && newEntry.CollisionOffset != 0xFFFFFFFF &&
                areaStart + newEntry.CollisionOffset + 28 <= RawData.Length)
            {
                Array.Copy(RawData, (int)(areaStart + newEntry.CollisionOffset), subDataBlob, 40, 28);
            }
            if (newEntry.SignalOffset != 0 && newEntry.SignalOffset != 0xFFFFFFFF &&
                areaStart + newEntry.SignalOffset + 28 <= RawData.Length)
            {
                Array.Copy(RawData, (int)(areaStart + newEntry.SignalOffset), subDataBlob, 68, 28);
            }

            // 3. Configure the new entry's relative sub-data offsets
            uint newEntryMoveOffset = entriesEndRel + (uint)OWTrainerEntry.SIZE + subDataLen;
            newEntry.MoveDataOffset = newEntryMoveOffset;
            newEntry.TalkCollisionOffset = newEntryMoveOffset + 12;
            newEntry.CollisionOffset = newEntryMoveOffset + 40;
            newEntry.SignalOffset = newEntryMoveOffset + 68;
            newEntry.PathCoreOffset = 0;
            newEntry.PathPointOffset = 0;

            // Insert new entry (84 bytes)
            Array.Copy(newEntry.RawData, 0, newData, (int)entryInsertPos, OWTrainerEntry.SIZE);

            // 4. Copy existing sub-data of this area (shifted by +84)
            if (subDataLen > 0)
            {
                Array.Copy(RawData, (int)(areaStart + entriesEndRel), newData, (int)(entryInsertPos + OWTrainerEntry.SIZE), (int)subDataLen);
            }

            // 5. Copy new sub-data blob (96 bytes) right after existing sub-data
            Array.Copy(subDataBlob, 0, newData, (int)(areaStart + newEntryMoveOffset), (int)NEW_SUBDATA_SIZE);

            // 6. Copy remainder of the file (subsequent areas)
            uint restStart = areaEnd;
            uint restNewStart = areaEnd + addedAreaBytes;
            if (RawData.Length > restStart)
            {
                Array.Copy(RawData, (int)restStart, newData, (int)restNewStart, RawData.Length - (int)restStart);
            }

            // 7. Update header: count, sectionOffsets, totalSize
            BitConverter.GetBytes(count + 1).CopyTo(newData, (int)areaStart);

            int hdrOff = 0x04;
            for (int i = 0; i < AreaCount; i++)
            {
                uint off = sectionOffsets[i];
                if (i > areaIndex) off += addedAreaBytes;
                BitConverter.GetBytes(off).CopyTo(newData, hdrOff);
                hdrOff += 4;
            }
            BitConverter.GetBytes(totalSize + addedAreaBytes).CopyTo(newData, hdrOff);

            // 8. Shift sub-data offsets of all existing entries in Area `areaIndex` by +84
            for (int i = 0; i < (int)count; i++)
            {
                int ePos = (int)areaStart + 4 + i * OWTrainerEntry.SIZE;
                int[] subOffFields = [0x3C, 0x40, 0x44, 0x48, 0x4C, 0x50];
                foreach (int fOff in subOffFields)
                {
                    uint val = BitConverter.ToUInt32(newData, ePos + fOff);
                    if (val != 0 && val != 0xFFFFFFFF)
                    {
                        BitConverter.GetBytes(val + (uint)OWTrainerEntry.SIZE).CopyTo(newData, ePos + fOff);
                    }
                }
            }

            return newData;
        }
    }
}
