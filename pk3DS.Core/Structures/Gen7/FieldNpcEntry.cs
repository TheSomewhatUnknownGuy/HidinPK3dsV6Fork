using System;

namespace pk3DS.Core.Structures
{
    public class FieldNpcEntry
    {
        public const int SIZE = 120;
        public byte[] RawData;
        public int Offset;

        public FieldNpcEntry(byte[] data, int offset)
        {
            RawData = new byte[SIZE];
            Array.Copy(data, offset, RawData, 0, SIZE);
            Offset = offset;
        }

        public float X
        {
            get => BitConverter.ToSingle(RawData, 0x00);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x00);
        }

        public float Y
        {
            get => BitConverter.ToSingle(RawData, 0x04);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x04);
        }

        public float Z
        {
            get => BitConverter.ToSingle(RawData, 0x08);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x08);
        }

        public float Rotation1
        {
            get => BitConverter.ToSingle(RawData, 0x10);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x10);
        }

        public float Rotation2
        {
            get => BitConverter.ToSingle(RawData, 0x18);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x18);
        }

        public uint SequentialId
        {
            get => BitConverter.ToUInt32(RawData, 0x20);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x20);
        }

        public uint EventId
        {
            get => BitConverter.ToUInt32(RawData, 0x28);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x28);
        }

        public uint Kind
        {
            get => BitConverter.ToUInt32(RawData, 0x2C);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x2C);
        }

        public uint CharacterId
        {
            get => BitConverter.ToUInt32(RawData, 0x30);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x30);
        }

        public uint ScriptId
        {
            get => BitConverter.ToUInt32(RawData, 0x34);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x34);
        }

        public uint DetailPoolStart
        {
            get => BitConverter.ToUInt32(RawData, 0x6C);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x6C);
        }

        public uint DetailPoolEnd
        {
            get => BitConverter.ToUInt32(RawData, 0x70);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x70);
        }

        public uint DetailBlockCount
        {
            get => BitConverter.ToUInt32(RawData, 0x74);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x74);
        }
    }
}
