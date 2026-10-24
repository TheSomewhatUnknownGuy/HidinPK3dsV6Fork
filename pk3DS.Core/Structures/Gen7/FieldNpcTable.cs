using System;
using System.Collections.Generic;

namespace pk3DS.Core.Structures
{
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

        public byte[] Write()
        {
            byte[] newData = (byte[])RawData.Clone();
            foreach (var e in Entries)
                Array.Copy(e.RawData, 0, newData, e.Offset, FieldNpcEntry.SIZE);
            return newData;
        }

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
