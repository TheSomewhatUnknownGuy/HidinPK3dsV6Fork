using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

using pk3DS.Core.Modding;

namespace pk3DS.Core.CTR
{
    public class AuditReport
    {
        public bool IsExpanded { get; set; }
        public bool HashValid { get; set; }
        public bool RelocationsIntact { get; set; }
        public string Details { get; set; } = "";
    }

    public static class CROUtil
    {
        public const byte FreeSpaceFill = 0xCC;

        public static uint ReadU32(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);
        public static void WriteU32(byte[] data, uint value, int offset) => BitConverter.GetBytes(value).CopyTo(data, offset);

        public static void UpdateOffsetPointer(byte[] data, int pointerLocation, int change, uint skipValue = 0, bool ignoreZero = true)
        {
            if (pointerLocation < 0 || pointerLocation + 4 > data.Length) return;
            uint temp = ReadU32(data, pointerLocation);

            if (temp < skipValue) return;
            if (ignoreZero && temp == 0) return;

            WriteU32(data, (uint)(temp + change), pointerLocation);
        }

        public static byte[] InjectSandboxPatch(byte[] data, uint originalOffset, byte[] patch)
        {
            // Always expand to ensure safe space
            int patchSize = (patch.Length + 11) & ~3; // Align to 4 bytes
            byte[] expanded = ExpandSegment(data, 'c', patchSize);
            uint[] starts = GetSegmentStartIndices(expanded);
            
            // The segment table offset is always at 0xC8
            uint segmentTableOffset = ReadU32(expanded, 0xC8);
            // .code length is at SegmentTable + 4
            uint oldCodeLen = ReadU32(data, (int)segmentTableOffset + 4);
            uint newSpaceAbsolute = starts[0] + oldCodeLen; 

            // 1. Write the patch to new space
            Array.Copy(patch, 0, expanded, newSpaceAbsolute, patch.Length);

            // 2. Write the Bridge (Branch) from originalOffset to newSpace
            uint bridgeOffset = originalOffset + starts[0];
            uint relAddr = (newSpaceAbsolute - bridgeOffset - 8) >> 2;
            uint branchInstr = 0xEA000000 | (relAddr & 0xFFFFFF);
            WriteU32(expanded, branchInstr, (int)bridgeOffset);

            return expanded;
        }

        public static byte[] ExpandSegment(byte[] data, char section, int bytesToAdd, int insertionPointRequested = -1, byte fill = FreeSpaceFill)
        {
            int fileSize = data.Length;
            if (ReadU32(data, 0x80) != 0x304F5243) // "CRO0" magic
                return data; // Not a CRO file, do not expand.

            // Use inline header fields (0xB0-0xBC) for segment boundaries.
            // The segment table pointer at 0xC8 can point into string data on some CROs.
            uint codeStart = ReadU32(data, 0xB0);
            uint codeSize  = ReadU32(data, 0xB4);
            uint dataStart = ReadU32(data, 0xB8);
            uint dataSize  = ReadU32(data, 0xBC);

            uint segmentEnd = 0;
            if (section == 'c') segmentEnd = codeStart + codeSize;
            else if (section == 'r') segmentEnd = dataStart; // rodata sits between code and data
            else if (section == 'd') segmentEnd = dataStart + dataSize;
            else segmentEnd = (uint)data.Length;

            // Validate segment boundaries before proceeding
            if (segmentEnd > (uint)fileSize || segmentEnd == 0)
                segmentEnd = (uint)fileSize;

            // Use insertion point if provided and valid, otherwise append to end of segment
            uint skipCheck = (insertionPointRequested >= 0) ? (uint)insertionPointRequested : segmentEnd;
            int skip = (int)skipCheck;
            if (skip < 0 || skip > fileSize) skip = fileSize; // Safety clamp

            byte[] newData = new byte[fileSize + Math.Abs(bytesToAdd)];
            if (bytesToAdd > 0)
            {
                Array.Copy(data, 0, newData, 0, skip);
                // The whole inserted run is filler; callers that place code/data here overwrite
                // the head of it and leave the tail as the visible free arena.
                for (int i = 0; i < bytesToAdd; i++) newData[skip + i] = fill;
                Array.Copy(data, skip, newData, skip + bytesToAdd, fileSize - skip);
            }
            else // Deletion
            {
                int del = Math.Abs(bytesToAdd);
                Array.Copy(data, 0, newData, 0, skip);
                Array.Copy(data, skip + del, newData, skip, fileSize - skip - del);
            }

            int freePaddingBytes = 0;
            if (section == 'd')
            {
                freePaddingBytes = (int)(ReadU32(data, 0x90) - (dataStart + dataSize));
                if (freePaddingBytes < 0) freePaddingBytes = 0;
            }

            // 1. Update inline segment fields in the header
            if (section == 'c') UpdateOffsetPointer(newData, 0xB4, bytesToAdd); // Code size
            else if (section == 'r') { /* rodata size not stored inline; skip */ }
            else if (section == 'd') UpdateOffsetPointer(newData, 0xBC, bytesToAdd + freePaddingBytes); // Data size includes padding

            {
                uint tableAt = ReadU32(data, 0xC8);
                if (tableAt >= (uint)skip) tableAt += (uint)bytesToAdd;

                uint segCount = ReadU32(data, 0xCC);
                for (uint i = 0; i < segCount && tableAt + (i * 12) + 12 <= newData.Length; i++)
                {
                    int entry = (int)(tableAt + (i * 12));
                    uint segOff = ReadU32(newData, entry);
                    uint segSize = ReadU32(newData, entry + 4);

                    // BSS has no file position; it is sized but never located, so leave it alone.
                    if (segOff == 0) continue;

                    if (segOff >= skipCheck)
                        WriteU32(newData, (uint)(segOff + bytesToAdd), entry);
                    else if (skipCheck <= segOff + segSize)
                        WriteU32(newData, (uint)(segSize + bytesToAdd), entry + 4);
                }
            }


            // Update inline segment start offsets that shifted
            UpdateOffsetPointer(newData, 0xB0, bytesToAdd, skipCheck); // Code start
            UpdateOffsetPointer(newData, 0xB8, bytesToAdd, skipCheck); // Data start

            // 2. Global Header Pointers
            UpdateOffsetPointer(newData, 0x84, bytesToAdd, skipCheck); // Name Offset
            WriteU32(newData, (uint)(fileSize + bytesToAdd), 0x90); // File Size
            for (int x = 0; x < 15; x++)
                UpdateOffsetPointer(newData, 0xC0 + x * 8, bytesToAdd, skipCheck);

            // 3. Pointer Tables (Import, Export, etc.)
            int[][] updateTables = [[0xD0, 0x0, 0x8], [0xF0, 0x0, 0x4, 0xC, 0x14], [0x100, 0x0, 0x4, 0x8], [0x110, 0x4, 0x8]];
            foreach (var table in updateTables)
            {
                uint pointerPointer = ReadU32(newData, table[0]);
                uint entryCount = ReadU32(newData, table[0] + 4);
                int entrySize = table.Last();
                if (pointerPointer == 0) continue;

                for (int i = 0; i < entryCount; i++)
                {
                    for (int s = 1; s < table.Length - 1; s++)
                    {
                        UpdateOffsetPointer(newData, (int)(i * entrySize + table[s] + pointerPointer), bytesToAdd, skipCheck);
                    }
                }
            }

            // 4. Relocation Patches
            // 0x128 is 0xC0 + 13*8, so the loop above has already advanced it. Advancing it again
            // here pointed the relocation table 2 * bytesToAdd past where it actually sits, which
            // made every shop list in Shop.cro unresolvable the moment anything was expanded.
            uint patchTableOffset = ReadU32(newData, 0x128);
            uint patchTableCount = ReadU32(newData, 0x12C);
            if (patchTableCount > 0)
            {

                uint[] newStarts = GetSegmentStartIndices(newData);
                for (int i = 0; i < (int)patchTableCount; i++)
                {
                    int entryOfs = (int)(patchTableOffset + i * 0x0C);
                    if (entryOfs + 12 > newData.Length) break; // Bounds safety

                    uint writingInfo = ReadU32(newData, entryOfs);
                    int writeSeg = (int)(writingInfo & 0xF);
                    uint writeOff = writingInfo >> 4;
                    uint pointedAt = ReadU32(newData, entryOfs + 8);
                    int targetSeg = newData[entryOfs + 5];

                    if (writeSeg > 3 || targetSeg > 3) continue;

                    uint absWrite = writeOff + newStarts[writeSeg];
                    if (absWrite >= skipCheck + bytesToAdd) 
                    {
                        uint nOff = (uint)(absWrite - newStarts[writeSeg]);
                        WriteU32(newData, (nOff << 4) | (uint)writeSeg, entryOfs);
                    }
                    
                    uint absTarget = pointedAt + newStarts[targetSeg];
                    if (absTarget >= skipCheck + bytesToAdd)
                    {
                        uint nAdd = (uint)(absTarget - newStarts[targetSeg]);
                        WriteU32(newData, nAdd, entryOfs + 8);
                    }
                }
            }

            // 5. SHA-256 Integrity
            byte[] hashes = RecalculateSegmentHashes(newData);
            Array.Copy(hashes, 0, newData, 0x00, hashes.Length);

            return newData;
        }

        public static void SaveCro(string path, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            if (IsCro(data))
                UpdateHashes(data);

            System.IO.File.WriteAllBytes(path, data);
        }

        public static bool IsCro(byte[] data) =>
            data is { Length: >= 0x84 } &&
            data[0x80] == (byte)'C' && data[0x81] == (byte)'R' && data[0x82] == (byte)'O' && data[0x83] == (byte)'0';

        public static void UpdateHashes(byte[] data)
        {
            // CRO format: 4×SHA-256 hashes occupy 0x00–0x7F, CRO0 magic starts at 0x80.
            // Slot 0 = CRO0 header (0x80–0x17F)
            // Slot 1 = .code segment
            // Slot 2 = .rodata segment
            // Slot 3 = .data segment
            byte[] hashes = RecalculateSegmentHashes(data);
            Array.Copy(hashes, 0, data, 0x00, hashes.Length); // hashes start at file offset 0x00
        }

        private static byte[] RecalculateSegmentHashes(byte[] data)
        {
            // In USUM Battle.cro, 0xC8 is string data. Use inline headers instead.
            uint codeStart = ReadU32(data, 0xB0);
            uint dataStart = ReadU32(data, 0xB8);
            uint codeSize  = ReadU32(data, 0xB4);
            uint dataSize  = ReadU32(data, 0xBC);

            byte[] hashes = new byte[0x20 * 4];
            using (var sha = SHA256.Create())
            {
                // Slot 0: CRO0 header block (0x80 to start of .code)
                uint headerEnd = codeStart > 0x80 ? codeStart : 0x180;
                if (headerEnd <= data.Length)
                {
                    byte[] h = sha.ComputeHash(data, 0x80, (int)(headerEnd - 0x80));
                    Array.Copy(h, 0, hashes, 0 * 0x20, 0x20);
                }

                // Slot 1: .code
                if (codeSize > 0 && codeStart + codeSize <= data.Length)
                {
                    byte[] h = sha.ComputeHash(data, (int)codeStart, (int)codeSize);
                    Array.Copy(h, 0, hashes, 1 * 0x20, 0x20);
                }
                
                // Slot 2: .rodata (between code and data)
                uint rodataStart = codeStart + codeSize;
                uint rodataSize = dataStart > rodataStart ? dataStart - rodataStart : 0;
                if (rodataSize > 0 && rodataStart + rodataSize <= data.Length)
                {
                    byte[] h = sha.ComputeHash(data, (int)rodataStart, (int)rodataSize);
                    Array.Copy(h, 0, hashes, 2 * 0x20, 0x20);
                }
                
                // Slot 3: .data
                if (dataSize > 0 && dataStart + dataSize <= data.Length)
                {
                    byte[] h = sha.ComputeHash(data, (int)dataStart, (int)dataSize);
                    Array.Copy(h, 0, hashes, 3 * 0x20, 0x20);
                }
            }
            return hashes;
        }

        public static uint[] GetSegmentStartIndices(byte[] data)
        {
            // The real segment table at 0xC8 IS valid for USUM Battle.cro - directly verified this
            // session by dumping it (id0=.text @0x180 size 0x11C7F4, id1=.rodata @0x11D000 size
            // 0xE694, id2=.data, id3=.bss, matching the inline header fields exactly). The old
            // "0xC8 can point into string data" comment here was never re-checked against real
            // bytes; GetSegmentForAddress already reads the real table directly and has been
            // correct throughout this session's testing. The bug was elsewhere: this function's own
            // "starts[1] = codeStart + codeSize" formula assumes rodata starts exactly where the
            // inline codeSize field ends, but that field covers code+rodata+alignment padding
            // combined - real rodata (segment 1) actually starts ~0xF000 bytes earlier. Any caller
            // computing a segment-1 absolute address through this function (ExpandSegment's
            // relocation-table fixup among them) inherited that same skew. Prefer the real table;
            // fall back to the inline-derived guess only if the table doesn't look like segments at
            // all (sanity-checked against the inline fields, which are a separately-sourced value
            // that should agree with a genuine table).
            uint segTableOffset = ReadU32(data, 0xC8);
            uint segCount = ReadU32(data, 0xCC);
            uint codeStart = ReadU32(data, 0xB0);
            uint dataStart = ReadU32(data, 0xB8);
            if (segCount >= 4 && segTableOffset + 4u * 12u <= (uint)data.Length)
            {
                uint seg0Off = ReadU32(data, (int)segTableOffset + 0 * 12);
                uint seg2Off = ReadU32(data, (int)segTableOffset + 2 * 12);
                if (seg0Off == codeStart && seg2Off == dataStart)
                {
                    return
                    [
                        ReadU32(data, (int)segTableOffset + 0 * 12),
                        ReadU32(data, (int)segTableOffset + 1 * 12),
                        ReadU32(data, (int)segTableOffset + 2 * 12),
                        ReadU32(data, (int)segTableOffset + 3 * 12),
                    ];
                }
            }

            // Fallback: derive from inline header fields. Only correct when rodata truly starts
            // right where code ends, with no gap - not the case for USUM Battle.cro, but kept for
            // whatever CRO this table genuinely doesn't describe.
            uint codeSize  = ReadU32(data, 0xB4);
            uint dataSize  = ReadU32(data, 0xBC);
            return
            [
                codeStart,                // .code
                codeStart + codeSize,     // .rodata (immediately after code - the unverified guess)
                dataStart,                 // .data
                dataStart + dataSize,      // .bss
            ];
        }

        public static int GetSegmentForAddress(uint address, byte[] data)
        {
            uint segmentTableOffset = ReadU32(data, 0xC8);
            uint codeStart = ReadU32(data, (int)segmentTableOffset);
            uint codeLen = ReadU32(data, (int)segmentTableOffset + 4);
            uint rodataStart = ReadU32(data, (int)segmentTableOffset + 0xC);
            uint rodataLen = ReadU32(data, (int)segmentTableOffset + 0x10);
            uint dataStart = ReadU32(data, (int)segmentTableOffset + 0x18);
            uint dataLen = ReadU32(data, (int)segmentTableOffset + 0x1C);

            if (address >= dataStart + dataLen) return 3; // BSS
            if (address < codeStart + codeLen) return 0; // Code
            // Rodata only up to its own declared end - NOT everything up to dataStart. Between
            // real rodata's end and dataStart sits CRO structural metadata (the segment table,
            // pointer tables, the relocation table itself) plus alignment padding; treating all of
            // that as "segment 1" let CodeRelocator register relocations at offsets past segment
            // 1's real size, which Citra's loader rejects (see DecompiledCRO.CodeEnd for the full
            // story - that gap is stock Battle.cro's alignment padding, not free rodata).
            if (address >= rodataStart && address < rodataStart + rodataLen) return 1; // Rodata
            if (address >= dataStart && address < dataStart + dataLen) return 2; // Data
            return -1; // Structural metadata / padding - not a writable/relocatable segment
        }

        public static void RelocateTable(byte[] data, uint oldRelativeAddend, int oldSegment, uint newAbsoluteOffset, int tableLengthBytes)
        {
            uint[] starts = GetSegmentStartIndices(data);
            uint oldAbsoluteOffset = oldRelativeAddend + starts[oldSegment];
            int newSegment = GetSegmentForAddress(newAbsoluteOffset, data);

            if (oldAbsoluteOffset + tableLengthBytes <= data.Length && 
                newAbsoluteOffset + tableLengthBytes <= data.Length)
            {
                Array.Copy(data, oldAbsoluteOffset, data, newAbsoluteOffset, tableLengthBytes);
                for (int i = 0; i < tableLengthBytes; i++) data[oldAbsoluteOffset + i] = FreeSpaceFill;
            }

            uint patchTableOffset = ReadU32(data, 0x128);
            uint patchTableCount = ReadU32(data, 0x12C);
            if (patchTableCount == 0) return;

            for (int i = 0; i < (int)patchTableCount; i++)
            {
                int entryOfs = (int)(patchTableOffset + i * 0x0C);
                uint writingInfo = ReadU32(data, entryOfs);
                int writeSeg = (int)(writingInfo & 0xF);
                uint writeOff = writingInfo >> 4;
                uint pointedAt = ReadU32(data, entryOfs + 8);
                int targetSeg = data[entryOfs + 5];

                if (targetSeg == oldSegment && pointedAt >= oldRelativeAddend && pointedAt < oldRelativeAddend + tableLengthBytes)
                {
                    data[entryOfs + 5] = (byte)newSegment;
                    uint newRelativeAddend = newAbsoluteOffset - starts[newSegment] + (pointedAt - oldRelativeAddend);
                    WriteU32(data, newRelativeAddend, entryOfs + 8);
                }

                if (writeSeg == oldSegment && writeOff >= oldRelativeAddend && writeOff < oldRelativeAddend + tableLengthBytes)
                {
                    uint newWriteOff = newAbsoluteOffset + (writeOff - oldRelativeAddend) - starts[newSegment];
                    WriteU32(data, (newWriteOff << 4) | (uint)newSegment, entryOfs);
                }
            }
        }

        public static void RelocateFunction(byte[] data, uint oldRelativeAddend, int oldSegment, uint newAbsoluteOffset)
        {
            uint[] starts = GetSegmentStartIndices(data);
            int newSegment = GetSegmentForAddress(newAbsoluteOffset, data);

            uint patchTableOffset = ReadU32(data, 0x128);
            uint patchTableCount = ReadU32(data, 0x12C);
            if (patchTableCount == 0) return;

            for (int i = 0; i < (int)patchTableCount; i++)
            {
                int entryOfs = (int)(patchTableOffset + i * 0x0C);
                uint pointedAt = ReadU32(data, entryOfs + 8);
                int targetSeg = data[entryOfs + 5];

                if (pointedAt == oldRelativeAddend && targetSeg == oldSegment)
                {
                    data[entryOfs + 5] = (byte)newSegment;
                    uint newRelativeAddend = newAbsoluteOffset - starts[newSegment];
                    WriteU32(data, newRelativeAddend, entryOfs + 8);
                }
            }
        }

        public static int FindRelocationPatchIndex(byte[] data, uint writeToAbsolute)
        {
            uint[] starts = GetSegmentStartIndices(data);
            int seg = GetSegmentForAddress(writeToAbsolute, data);
            uint off = writeToAbsolute - starts[seg];
            uint info = (off << 4) | (uint)seg;

            uint patchTableOffset = ReadU32(data, 0x128);
            uint patchTableCount = ReadU32(data, 0x12C);

            for (int i = 0; i < (int)patchTableCount; i++)
            {
                if (ReadU32(data, (int)(patchTableOffset + i * 0x0C)) == info)
                    return i;
            }
            return -1;
        }

        public static RelocationEntry GetRelocationEntry(byte[] data, int patchIndex)
        {
            uint patchTableOffset = ReadU32(data, 0x128);
            int entryOfs = (int)(patchTableOffset + patchIndex * 0x0C);
            uint[] starts = GetSegmentStartIndices(data);

            uint info = ReadU32(data, entryOfs);
            int writeSeg = (int)(info & 0xF);
            uint writeOff = info >> 4;
            int targetSeg = data[entryOfs + 5];
            uint addend = ReadU32(data, entryOfs + 8);

            var entry = new RelocationEntry
            {
                WriteTo = writeOff + starts[writeSeg],
                PatchAddr = (uint)entryOfs,
                Addend = addend,
                TargetSeg = targetSeg,
                Note = $"Patch #{patchIndex} (Seg {targetSeg})"
            };
            Array.Copy(data, entryOfs, entry.RawPatch, 0, 12);
            return entry;
        }

        public static void UpdatePatchCount(byte[] data, uint newCount) => WriteU32(data, newCount, 0x12C);

        public static AuditReport AuditIntegrity(byte[] data)
        {
            var report = new AuditReport();
            try
            {
                uint segmentTableOffset = ReadU32(data, 0xC8);
                uint codeLen = ReadU32(data, (int)segmentTableOffset + 4);
                
                // 1. Check for Expansion (Heuristic: USUM code.bin/battle.cro typical sizes)
                // A better check: is the current length > standard?
                // For now, let's look for large gaps of 0x00 or 0xCC at the end of text
                report.IsExpanded = codeLen > 0x14B000; // Battle.cro stock is typically around this
                
                // 2. Verify Hashes
                byte[] currentHashes = new byte[0x80];
                Array.Copy(data, 0x00, currentHashes, 0, 0x80);
                byte[] freshHashes = RecalculateSegmentHashes(data);
                report.HashValid = currentHashes.SequenceEqual(freshHashes);
                
                // 3. Relocation Table Health
                uint patchTableOffset = ReadU32(data, 0x128);
                uint patchTableCount = ReadU32(data, 0x12C);
                report.RelocationsIntact = patchTableOffset != 0 && patchTableOffset < data.Length && patchTableCount < 50000;
                
                report.Details = $"Segments: {codeLen:X} bytes code. {(report.IsExpanded ? "Expansion sandbox active." : "Standard layout.")}";
                if (!report.HashValid) report.Details += " WARNING: SHA-256 Hash mismatch detected.";
            }
            catch (Exception ex) { report.Details = "Audit Failed: " + ex.Message; }
            return report;
        }

        public static string GetCROHeaderSummary(byte[] data)
        {
            uint[] starts = GetSegmentStartIndices(data);
            uint patchTbl = ReadU32(data, 0x128);
            uint patchCnt = ReadU32(data, 0x12C);
            uint nameOfs = ReadU32(data, 0x84);
            uint fileSize = ReadU32(data, 0x90);
            uint segmentTbl = ReadU32(data, 0xC8);

            return $"--- CRO Header Summary ---\r\n" +
                   $"File Size: 0x{fileSize:X}\r\n" +
                   $"Name Offset: 0x{nameOfs:X}\r\n" +
                   $"Segment Table: 0x{segmentTbl:X}\r\n" +
                   $"Patch Table: 0x{patchTbl:X} (Count: {patchCnt})\r\n" +
                   $"--- Segments ---\r\n" +
                   $".code:  0x{starts[0]:X}\r\n" +
                   $".rodata: 0x{starts[1]:X}\r\n" +
                   $".data:   0x{starts[2]:X}\r\n" +
                   $".bss:    0x{starts[3]:X}";
        }
    }
}
