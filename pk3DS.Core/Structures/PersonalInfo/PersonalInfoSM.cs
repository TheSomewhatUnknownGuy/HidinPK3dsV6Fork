using System;
using System.Linq;

namespace pk3DS.Core.Structures.PersonalInfo;

public class PersonalInfoSM : PersonalInfoXY
{
    public new const int SIZE = 0x54;

    public PersonalInfoSM()
    {
        Data = new byte[SIZE];
        TMHM = new bool[128];
        TutorFlags = new bool[160];
    }

    private readonly bool _tmMasked = pk3DS.Core.Modding.PassiveGuard.Enabled; // TM bits 104+ were not read, so Write must not zero their bytes even if the setting is off by then

    public PersonalInfoSM(byte[] data)
    {
        Data = new byte[SIZE];
        TMHM = new bool[128];
        TutorFlags = new bool[160];
        if (data == null)
            return;
        
        Array.Copy(data, 0, Data, 0, Math.Min(data.Length, SIZE));

        // TM/HM: 0x28 to 0x37 (16 bytes / 128 bits)
        TMHM = GetBits(Data.Skip(0x28).Take(0x10).ToArray());
        if (pk3DS.Core.Modding.PassiveGuard.Enabled) // bits 104-127 are passive ability bytes then, not TMs
            Array.Clear(TMHM, pk3DS.Core.Modding.PassiveGuard.TmCap, TMHM.Length - pk3DS.Core.Modding.PassiveGuard.TmCap);
        
        // Tutor Flags: 0x38 to 0x4B (20 bytes / 160 bits)
        // This covers Word 28, 29, 2A, 2B, 2C.
        byte[] tutorData = new byte[20];
        Array.Copy(Data, 0x38, tutorData, 0, Math.Min(20, Data.Length - 0x38));
        TutorFlags = GetBits(tutorData);
    }

    public bool[] TutorFlags;

    // Ability ids are 9-bit: low byte at 0x18/0x19/0x1A, bit 8 in Data[0x53] bits 0/1/2 —
    // this is the layout the Expansion Pack code.bin reads. ExpandedAbilities widens to 10-bit
    // by sourcing bit 9 from Data[0x52] bits 4/5/6, which are free. Ids below 512 encode
    // identically either way, so toggling never invalidates existing personal data.
    // 0x1E/0x1F cannot host ability high bytes: that is FormeSprite (PersonalInfoXY.cs:101).
    public override int[] Abilities
    {
        get
        {
            int hi = Data[0x53];
            int hi2 = Data[0x1E];
            int hi3 = Data[0x1F];
            int[] abilities = [
                Data[0x18] | (hi << 8),
                Data[0x19] | (hi2 << 8),
                Data[0x1A] | (hi3 << 8)
            ];

            if (GameConfig.ExpandedAbilities && !pk3DS.Core.Modding.PassiveGuard.Enabled)  // passives own 0x52 bits 4-6 then
            {
                if(Data[0x52] & 0xE == 0)
                    {
                        Data[0x18] | (hi << 8);
                        Data[0x19] | (hi2 << 8);
                        Data[0x1A] | (hi3 << 8);
                    }
    
                    //use new version
                else
                    {
                        Data[0x18] | ((Data[0x52] >> 1 & 1) << 8);
                        Data[0x19] | ((Data[0x52] >> 2 & 1) << 8);
                        Data[0x1A] | ((Data[0x52] >> 3 & 1) << 8);
                    }
                abilities[0] |= (hi >> 4 & 1) << 9;
                abilities[1] |= (hi2 >> 4 & 1) << 9;
                abilities[2] |= (hi3 >> 4 & 1) << 9;
            }
            return abilities;
        }
        set
        {
            if (value?.Length != 3) return;
            
            //set low bytes
            Data[0x18] = (byte)(value[0] & 0xFF);
            Data[0x19] = (byte)(value[1] & 0xFF);
            Data[0x1A] = (byte)(value[2] & 0xFF);
    
            //clear the 3 bits from old version
            Data[0x52] = Data[0x52] & 0x1; 

            // Vacate the pre-fix location so entries written by the 0x52-packing builds stop
            // reporting ids 0x100 too low. Bit 0 is LocalVariant and must survive.
            //Data[0x52] &= unchecked((byte)~0x0E);

            if (GameConfig.ExpandedAbilities && !pk3DS.Core.Modding.PassiveGuard.Enabled)  // passives own 0x52 bits 4-6 then
            {
            //sets high bytes?
                Data[0x53] = (byte)(value[0] >> 8);
                Data[0x1E] = (byte)(value[1] >> 8);
                Data[0x1F] = (byte)(value[2] >> 8);
            }
        }
    }

    private static readonly int[] PassiveLow = [0x35, 0x36, 0x37];

    public int[] Passives
    {
        get => pk3DS.Core.Modding.PassiveGuard.Unpack(Data, PassiveLow, 0x52, 4);
        set => pk3DS.Core.Modding.PassiveGuard.Pack(Data, PassiveLow, 0x52, 4, value);
    }

    public override byte[] Write()
    {
        byte[] passiveBytes = Data[0x35..0x38];
        SetBits(TMHM).CopyTo(Data, 0x28);
        if (_tmMasked || pk3DS.Core.Modding.PassiveGuard.Enabled) passiveBytes.CopyTo(Data, 0x35); // a TM tick must never change a passive
        SetBits(TutorFlags).CopyTo(Data, 0x38);
        return Data;
    }

    public int SpecialZ_Item { get => BitConverter.ToUInt16(Data, 0x4C); set => BitConverter.GetBytes((ushort)value).CopyTo(Data, 0x4C); }
    public int SpecialZ_BaseMove { get => BitConverter.ToUInt16(Data, 0x4E); set => BitConverter.GetBytes((ushort)value).CopyTo(Data, 0x4E); }
    public int SpecialZ_ZMove { get => BitConverter.ToUInt16(Data, 0x50); set => BitConverter.GetBytes((ushort)value).CopyTo(Data, 0x50); }
    public bool LocalVariant { get => (Data[0x52] & 1) != 0; set => Data[0x52] = (byte)((Data[0x52] & ~1) | (value ? 1 : 0)); }
}
