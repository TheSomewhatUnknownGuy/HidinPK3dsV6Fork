using System;
using System.Collections.Generic;

namespace pk3DS.Core.Structures
{
    /// <summary>
    /// Represents an overworld ground item entity (pickable Poké Ball) in Pokémon Generation 7 (USUM/SM).
    /// Ground items require synchronous representation in both ED.EI (interaction/item) and ED.ES (visual model).
    /// </summary>
    public class GroundItemEntry
    {
        public const int EI_RECORD_SIZE = 64;
        public const int EI_COLLISION_SIZE = 28;
        public const int ES_RECORD_SIZE = 56;
        public const int ES_SECONDARY_SIZE = 28;

        public int ItemID { get; set; }
        public int Quantity { get; set; } = 1;
        public ushort PickupType { get; set; } = 0;
        public uint Unk56 { get; set; } = 2;
        public uint EventID { get; set; }
        public uint Flag { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float CollisionRadius { get; set; } = 60.0f;
        public float CollisionHeight { get; set; } = 150.0f;
        public float VisualRadius { get; set; } = 30.0f;
        public float VisualHeight { get; set; } = 150.0f;
        public uint ModelID { get; set; } = 29; // Model 29 = Poké Ball
        public uint ObjectType { get; set; } = 4; // Object Type 4 = Item Poké Ball
        public bool HasVisualModel { get; set; } = true;

        public GroundItemEntry() { }

        public GroundItemEntry(byte[] eiRecord, byte[] eiCollision, byte[] esRecord = null)
        {
            if (eiRecord != null && eiRecord.Length >= EI_RECORD_SIZE)
            {
                X = BitConverter.ToSingle(eiRecord, 4);
                Y = BitConverter.ToSingle(eiRecord, 8);
                Z = BitConverter.ToSingle(eiRecord, 12);
                Flag = BitConverter.ToUInt32(eiRecord, 36);
                EventID = BitConverter.ToUInt32(eiRecord, 44);
                ItemID = (int)BitConverter.ToUInt32(eiRecord, 48); // Item ID at record offset 48
                Quantity = BitConverter.ToUInt16(eiRecord, 52);   // Quantity at record offset 52
                PickupType = BitConverter.ToUInt16(eiRecord, 54); // Pickup/trigger mode at offset 54 (0=ball, 9=hidden)
                Unk56 = BitConverter.ToUInt32(eiRecord, 56);      // Mode flag at offset 56 (2=visible, 0=hidden)
            }

            if (eiCollision != null && eiCollision.Length >= EI_COLLISION_SIZE)
            {
                CollisionRadius = BitConverter.ToSingle(eiCollision, 20);
                CollisionHeight = BitConverter.ToSingle(eiCollision, 24);
            }

            if (esRecord != null && esRecord.Length >= ES_RECORD_SIZE)
            {
                HasVisualModel = true;
                ObjectType = BitConverter.ToUInt32(esRecord, 0);
                ModelID = BitConverter.ToUInt32(esRecord, 48);
            }
            else
            {
                // No matching ES record found; item is a hidden item
                HasVisualModel = false;
            }
        }

        public byte[] BuildEiRecord(uint collisionOffset)
        {
            byte[] record = new byte[EI_RECORD_SIZE];
            BitConverter.GetBytes(10u).CopyTo(record, 0); // 0x0A (Item entity type)
            BitConverter.GetBytes(X).CopyTo(record, 4);
            BitConverter.GetBytes(Y).CopyTo(record, 8);
            BitConverter.GetBytes(Z).CopyTo(record, 12);
            BitConverter.GetBytes(1.0f).CopyTo(record, 28); // Quaternion W = 1.0f
            BitConverter.GetBytes(Flag).CopyTo(record, 36);
            BitConverter.GetBytes(EventID).CopyTo(record, 44);
            BitConverter.GetBytes((uint)ItemID).CopyTo(record, 48);
            BitConverter.GetBytes((ushort)Quantity).CopyTo(record, 52);
            BitConverter.GetBytes(PickupType).CopyTo(record, 54);
            BitConverter.GetBytes(Unk56).CopyTo(record, 56);
            BitConverter.GetBytes(collisionOffset).CopyTo(record, 60);
            return record;
        }

        public byte[] BuildEiCollision()
        {
            byte[] col = new byte[EI_COLLISION_SIZE];
            BitConverter.GetBytes(1u).CopyTo(col, 0); // Shape type
            BitConverter.GetBytes(X).CopyTo(col, 8);
            BitConverter.GetBytes(Y).CopyTo(col, 12);
            BitConverter.GetBytes(Z).CopyTo(col, 16);
            BitConverter.GetBytes(CollisionRadius).CopyTo(col, 20);
            BitConverter.GetBytes(CollisionHeight).CopyTo(col, 24);
            return col;
        }

        public byte[] BuildEsRecord(uint secondaryOffset)
        {
            byte[] record = new byte[ES_RECORD_SIZE];
            BitConverter.GetBytes(ObjectType).CopyTo(record, 0);
            BitConverter.GetBytes(X).CopyTo(record, 4);
            BitConverter.GetBytes(Y).CopyTo(record, 8);
            BitConverter.GetBytes(Z).CopyTo(record, 12);
            BitConverter.GetBytes(1.0f).CopyTo(record, 28); // Quaternion W = 1.0f
            BitConverter.GetBytes(Flag).CopyTo(record, 36);
            BitConverter.GetBytes(EventID).CopyTo(record, 44);
            BitConverter.GetBytes(ModelID).CopyTo(record, 48);
            BitConverter.GetBytes(secondaryOffset).CopyTo(record, 52);
            return record;
        }

        public byte[] BuildEsSecondary()
        {
            byte[] sec = new byte[ES_SECONDARY_SIZE];
            BitConverter.GetBytes(1u).CopyTo(sec, 0); // Shape type
            BitConverter.GetBytes(X).CopyTo(sec, 8);
            BitConverter.GetBytes(Y).CopyTo(sec, 12);
            BitConverter.GetBytes(Z).CopyTo(sec, 16);
            BitConverter.GetBytes(VisualRadius).CopyTo(sec, 20);
            BitConverter.GetBytes(VisualHeight).CopyTo(sec, 24);
            return sec;
        }
    }

    /// <summary>
    /// Dual-file manager for Gen 7 Overworld Ground Items, synchronizing ED.EI (interaction) and ED.ES (visuals).
    /// Handles record insertion, coordinate synchronization, and collision/secondary block pointer recalculation.
    /// </summary>
    public class GroundItemTable
    {
        public List<GroundItemEntry> Items { get; } = new List<GroundItemEntry>();

        public byte[] RawEiData { get; private set; }
        public byte[] RawEsData { get; private set; }

        public GroundItemTable(byte[] eiData, byte[] esData)
        {
            Load(eiData, esData);
        }

        public void Load(byte[] eiData, byte[] esData)
        {
            Items.Clear();
            RawEiData = eiData != null ? (byte[])eiData.Clone() : Array.Empty<byte>();
            RawEsData = esData != null ? (byte[])esData.Clone() : Array.Empty<byte>();

            if (RawEiData.Length < 4)
                return;

            uint eiCount = BitConverter.ToUInt32(RawEiData, 0);
            int colBase = 4 + (int)eiCount * GroundItemEntry.EI_RECORD_SIZE;

            // Pre-index ES records by EventID for fast visual attribute correlation
            var esMap = new Dictionary<uint, byte[]>();
            if (RawEsData.Length >= 4)
            {
                uint esCount = BitConverter.ToUInt32(RawEsData, 0);
                for (int j = 0; j < esCount; j++)
                {
                    int esOff = 4 + j * GroundItemEntry.ES_RECORD_SIZE;
                    if (esOff + GroundItemEntry.ES_RECORD_SIZE <= RawEsData.Length)
                    {
                        uint evId = BitConverter.ToUInt32(RawEsData, esOff + 44);
                        byte[] esRec = new byte[GroundItemEntry.ES_RECORD_SIZE];
                        Array.Copy(RawEsData, esOff, esRec, 0, GroundItemEntry.ES_RECORD_SIZE);
                        esMap[evId] = esRec;
                    }
                }
            }

            for (int i = 0; i < eiCount; i++)
            {
                int recOff = 4 + i * GroundItemEntry.EI_RECORD_SIZE;
                if (recOff + GroundItemEntry.EI_RECORD_SIZE > RawEiData.Length)
                    break;

                byte[] eiRec = new byte[GroundItemEntry.EI_RECORD_SIZE];
                Array.Copy(RawEiData, recOff, eiRec, 0, GroundItemEntry.EI_RECORD_SIZE);

                uint colOffset = BitConverter.ToUInt32(eiRec, 60);
                byte[] colRec = new byte[GroundItemEntry.EI_COLLISION_SIZE];
                if (colOffset + GroundItemEntry.EI_COLLISION_SIZE <= RawEiData.Length)
                {
                    Array.Copy(RawEiData, (int)colOffset, colRec, 0, GroundItemEntry.EI_COLLISION_SIZE);
                }

                uint evId = BitConverter.ToUInt32(eiRec, 44);
                esMap.TryGetValue(evId, out byte[] matchedEs);

                Items.Add(new GroundItemEntry(eiRec, colRec, matchedEs));
            }
        }

        /// <summary>
        /// Appends a new ground item Poké Ball and updates internal structures.
        /// </summary>
        public void AddItem(GroundItemEntry item)
        {
            Items.Add(item);
        }

        /// <summary>
        /// Removes a ground item at the given index.
        /// </summary>
        public void RemoveItem(int index)
        {
            if (index >= 0 && index < Items.Count)
                Items.RemoveAt(index);
        }

        /// <summary>
        /// Assembles and serializes the complete, valid binary byte array for ED.EI (interaction).
        /// Recalculates all collision block offsets.
        /// </summary>
        public byte[] WriteEI()
        {
            uint count = (uint)Items.Count;
            int totalSize = 4 + (int)count * GroundItemEntry.EI_RECORD_SIZE + (int)count * GroundItemEntry.EI_COLLISION_SIZE;
            byte[] outData = new byte[totalSize];

            BitConverter.GetBytes(count).CopyTo(outData, 0);

            for (int i = 0; i < Items.Count; i++)
            {
                uint colOffset = (uint)(4 + count * GroundItemEntry.EI_RECORD_SIZE + i * GroundItemEntry.EI_COLLISION_SIZE);
                byte[] record = Items[i].BuildEiRecord(colOffset);
                Array.Copy(record, 0, outData, 4 + i * GroundItemEntry.EI_RECORD_SIZE, GroundItemEntry.EI_RECORD_SIZE);

                byte[] col = Items[i].BuildEiCollision();
                Array.Copy(col, 0, outData, (int)colOffset, GroundItemEntry.EI_COLLISION_SIZE);
            }

            RawEiData = outData;
            return outData;
        }

        /// <summary>
        /// Synchronizes and serializes the complete, valid binary byte array for ED.ES (visuals).
        /// Preserves non-item visual models, appends secondary blocks, and shifts all pointer offsets.
        /// </summary>
        public byte[] WriteES()
        {
            if (RawEsData.Length < 4)
                return RawEsData;

            uint oldEsCount = BitConverter.ToUInt32(RawEsData, 0);

            // Read all existing ES main records
            var existingRecords = new List<byte[]>();
            for (int i = 0; i < oldEsCount; i++)
            {
                int off = 4 + i * GroundItemEntry.ES_RECORD_SIZE;
                if (off + GroundItemEntry.ES_RECORD_SIZE <= RawEsData.Length)
                {
                    byte[] rec = new byte[GroundItemEntry.ES_RECORD_SIZE];
                    Array.Copy(RawEsData, off, rec, 0, GroundItemEntry.ES_RECORD_SIZE);
                    existingRecords.Add(rec);
                }
            }

            // Extract the secondary block section
            int oldSecStart = 4 + (int)oldEsCount * GroundItemEntry.ES_RECORD_SIZE;
            int oldSecLength = Math.Max(0, RawEsData.Length - oldSecStart);
            byte[] oldSecondaryBlock = new byte[oldSecLength];
            if (oldSecLength > 0 && oldSecStart < RawEsData.Length)
            {
                Array.Copy(RawEsData, oldSecStart, oldSecondaryBlock, 0, oldSecLength);
            }

            // Identify which items already exist in ES (matched by EventID)
            var existingEvIds = new HashSet<uint>();
            foreach (var rec in existingRecords)
            {
                uint evId = BitConverter.ToUInt32(rec, 44);
                existingEvIds.Add(evId);
            }

            // For items already present in ES, update their coordinates and flags in-place
            foreach (var item in Items)
            {
                for (int i = 0; i < existingRecords.Count; i++)
                {
                    var rec = existingRecords[i];
                    uint evId = BitConverter.ToUInt32(rec, 44);
                    if (evId == item.EventID)
                    {
                        BitConverter.GetBytes(item.X).CopyTo(rec, 4);
                        BitConverter.GetBytes(item.Y).CopyTo(rec, 8);
                        BitConverter.GetBytes(item.Z).CopyTo(rec, 12);
                        BitConverter.GetBytes(item.Flag).CopyTo(rec, 36);
                        break;
                    }
                }
            }

            // For brand-new items not yet in ES, create new ES records and secondary blocks
            var newRecords = new List<byte[]>();
            var newSecondaryBlocks = new List<byte[]>();
            foreach (var item in Items)
            {
                if (item.HasVisualModel && !existingEvIds.Contains(item.EventID))
                {
                    newRecords.Add(item.BuildEsRecord(0)); // Offset populated below
                    newSecondaryBlocks.Add(item.BuildEsSecondary());
                }
            }

            if (newRecords.Count == 0)
            {
                // No new visual entities added, write back updated existing records
                byte[] updated = (byte[])RawEsData.Clone();
                for (int i = 0; i < existingRecords.Count; i++)
                {
                    Array.Copy(existingRecords[i], 0, updated, 4 + i * GroundItemEntry.ES_RECORD_SIZE, GroundItemEntry.ES_RECORD_SIZE);
                }
                RawEsData = updated;
                return updated;
            }

            // If new records are added, the secondary block section shifts forward by:
            int mainShift = newRecords.Count * GroundItemEntry.ES_RECORD_SIZE;

            // Shift all existing non-zero secondary pointers by mainShift
            foreach (var rec in existingRecords)
            {
                uint secOff = BitConverter.ToUInt32(rec, 52);
                if (secOff > 0)
                {
                    BitConverter.GetBytes(secOff + (uint)mainShift).CopyTo(rec, 52);
                }
            }

            // Calculate total new file size
            int newSecStart = oldSecStart + mainShift;
            int newTotalSize = newSecStart + oldSecLength + newSecondaryBlocks.Count * GroundItemEntry.ES_SECONDARY_SIZE;
            byte[] outEs = new byte[newTotalSize];

            // Header count
            uint newTotalCount = (uint)(existingRecords.Count + newRecords.Count);
            BitConverter.GetBytes(newTotalCount).CopyTo(outEs, 0);

            // Write existing records
            int curOff = 4;
            foreach (var rec in existingRecords)
            {
                Array.Copy(rec, 0, outEs, curOff, GroundItemEntry.ES_RECORD_SIZE);
                curOff += GroundItemEntry.ES_RECORD_SIZE;
            }

            // Assign offsets and write new records
            uint appendSecOff = (uint)(newSecStart + oldSecLength);
            for (int k = 0; k < newRecords.Count; k++)
            {
                var rec = newRecords[k];
                BitConverter.GetBytes(appendSecOff).CopyTo(rec, 52);
                Array.Copy(rec, 0, outEs, curOff, GroundItemEntry.ES_RECORD_SIZE);
                curOff += GroundItemEntry.ES_RECORD_SIZE;
                appendSecOff += (uint)GroundItemEntry.ES_SECONDARY_SIZE;
            }

            // Copy old secondary blocks (now shifted by mainShift)
            if (oldSecLength > 0)
            {
                Array.Copy(oldSecondaryBlock, 0, outEs, newSecStart, oldSecLength);
            }

            // Append new secondary blocks
            int secWriteOff = newSecStart + oldSecLength;
            foreach (var sec in newSecondaryBlocks)
            {
                Array.Copy(sec, 0, outEs, secWriteOff, GroundItemEntry.ES_SECONDARY_SIZE);
                secWriteOff += GroundItemEntry.ES_SECONDARY_SIZE;
            }

            RawEsData = outEs;
            return outEs;
        }
    }
}
