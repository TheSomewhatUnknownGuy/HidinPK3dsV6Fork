using System;
using System.ComponentModel;

namespace pk3DS.Core.Structures;

public class ZoneData7
{
    public const int SIZE = 0x54;
    private readonly byte[] Data;
    private const string Weather = "Weather";
    private const string Zone = "Zone";
    private const string Camera = "Camera";

    public int WorldIndex;
    public int AreaIndex;
    public string Name { get; private set; }
    public string LocationName { get; private set; }
    public int Index { get; private set; }

    public ZoneData7(byte[] data)
    {
        Data = (byte[])(data ?? new byte[SIZE]).Clone();
    }

    public ZoneData7(byte[] data, int index)
    {
        Data = new byte[SIZE];
        Array.Copy(data, index * SIZE, Data, 0, SIZE);
    }

    public byte[] Write() => (byte[])Data.Clone();

    #region Structure

    [Category(Zone), Description("The ID of the daytime BGM that plays in this location.")]
    public uint BgmDay { get => BitConverter.ToUInt32(Data, 0x00); set => BitConverter.GetBytes(value).CopyTo(Data, 0x00); }

    [Category(Zone), Description("The ID of the nighttime BGM that plays in this location.")]
    public uint BgmNight { get => BitConverter.ToUInt32(Data, 0x04); set => BitConverter.GetBytes(value).CopyTo(Data, 0x04); }

    [Category(Zone), Description("Event data archive ID used by this zone.")]
    public ushort EventDataId { get => BitConverter.ToUInt16(Data, 0x08); set => BitConverter.GetBytes(value).CopyTo(Data, 0x08); }

    [Category(Zone), Description("THIS ONE IS THE NUMBER IN PAWNGARC! Town map grouping value for this zone.")]
    public ushort ZoneGroup { get => BitConverter.ToUInt16(Data, 0x0A); set => BitConverter.GetBytes(value).CopyTo(Data, 0x0A); }

    [Category(Zone), Description("Message archive ID used for zone text.")]
    public ushort MessageArcId { get => BitConverter.ToUInt16(Data, 0x0C); set => BitConverter.GetBytes(value).CopyTo(Data, 0x0C); }

    [Category(Zone), Description("Gimmick/field behavior ID for this zone.")]
    public ushort GimmickId { get => BitConverter.ToUInt16(Data, 0x0E); set => BitConverter.GetBytes(value).CopyTo(Data, 0x0E); }

    [Category(Zone), Description("Default map start X position.")]
    public int StartPosX { get => BitConverter.ToInt32(Data, 0x10); set => BitConverter.GetBytes(value).CopyTo(Data, 0x10); }

    [Category(Zone), Description("Default map start Y position.")]
    public int StartPosY { get => BitConverter.ToInt32(Data, 0x14); set => BitConverter.GetBytes(value).CopyTo(Data, 0x14); }

    [Category(Zone), Description("Default map start Z position.")]
    public int StartPosZ { get => BitConverter.ToInt32(Data, 0x18); set => BitConverter.GetBytes(value).CopyTo(Data, 0x18); }

    [Category(Zone), Description("Place name message ID shown for this zone (index into the place-name text table).")]
    public ushort PlaceNameId { get => BitConverter.ToUInt16(Data, 0x1C); set => BitConverter.GetBytes(value).CopyTo(Data, 0x1C); }

    [Category(Zone), Description("Place name window/type setting.")]
    public byte PlaceNameType { get => Data[0x1E]; set => Data[0x1E] = value; }

    [Category(Zone), Description("Map resource/type selector.")]
    public byte MapResourceId { get => Data[0x1F]; set => Data[0x1F] = value; }

    [Category(Zone), Description("Zone scenery/memory ID.")]
    public byte ZoneSceneryId { get => Data[0x21]; set => Data[0x21] = value; }

    [Category(Weather), Description("Weather environment profile index.")]
    public byte WeatherEnvIndex { get => Data[0x22]; set => Data[0x22] = value; }

    [Category(Zone), Description("Map transition behavior type.")]
    public byte MapChangeType { get => Data[0x23]; set => Data[0x23] = value; }

    [Category(Weather), Description("Morning weather. 1 Cloudy, 2 Drizzle, 3 Thunderstorm, 4 Light Snow (no battle), 5 Blizzard, 6 Harsh Sunlight, 7 Sandstorm, 8 Fog, 9 Normal Rain, 10 Diamond Dust.")]
    public uint WeatherMorning { get => BitConverter.ToUInt32(Data, 0x24); set => BitConverter.GetBytes(value).CopyTo(Data, 0x24); }

    [Category(Weather), Description("Weather value used in the noon time zone.")]
    public uint WeatherNoon { get => BitConverter.ToUInt32(Data, 0x28); set => BitConverter.GetBytes(value).CopyTo(Data, 0x28); }

    [Category(Weather), Description("Weather value used in the evening time zone.")]
    public uint WeatherEvening { get => BitConverter.ToUInt32(Data, 0x2C); set => BitConverter.GetBytes(value).CopyTo(Data, 0x2C); }

    [Category(Weather), Description("Weather value used in the night time zone.")]
    public uint WeatherNight { get => BitConverter.ToUInt32(Data, 0x30); set => BitConverter.GetBytes(value).CopyTo(Data, 0x30); }

    [Category(Weather), Description("Weather value used in the midnight time zone.")]
    public uint WeatherMidnight { get => BitConverter.ToUInt32(Data, 0x34); set => BitConverter.GetBytes(value).CopyTo(Data, 0x34); }

    [Category(Zone), Description("Various one-bit flags for this zone; see ZoneFlags7 for the known bit meanings.")]
    public ZoneFlags7 Flags { get => (ZoneFlags7)BitConverter.ToUInt32(Data, 0x38); set => BitConverter.GetBytes((uint)value).CopyTo(Data, 0x38); }

    [Category(Camera), Description("Environment data pack index for this zone.")]
    public byte EnvironmentIndex { get => Data[0x3C]; set => Data[0x3C] = value; }

    [Category(Camera), Description("Default camera data index.")]
    public byte CameraDefaultIndex { get => Data[0x3D]; set => Data[0x3D] = value; }

    [Category(Camera), Description("Default camera support data index.")]
    public byte CameraSupportDefaultIndex { get => Data[0x3E]; set => Data[0x3E] = value; }

    [Category(Zone), Description("Island index classification value.")]
    public byte IslandIndex { get => Data[0x3F]; set => Data[0x3F] = value; }

    [Category(Zone), Description("Near background attribute pattern ID.")]
    public ushort AttrBgNearPattern { get => BitConverter.ToUInt16(Data, 0x40); set => BitConverter.GetBytes(value).CopyTo(Data, 0x40); }

    [Category(Zone), Description("Far battle background type ID.")]
    public ushort BattleBgFarType { get => BitConverter.ToUInt16(Data, 0x42); set => BitConverter.GetBytes(value).CopyTo(Data, 0x42); }

    [Category(Zone), Description("Subscreen base X coordinate.")]
    public int SubscreenBasisPosX { get => BitConverter.ToInt32(Data, 0x44); set => BitConverter.GetBytes(value).CopyTo(Data, 0x44); }

    [Category(Zone), Description("Subscreen base Z coordinate.")]
    public int SubscreenBasisPosZ { get => BitConverter.ToInt32(Data, 0x48); set => BitConverter.GetBytes(value).CopyTo(Data, 0x48); }

    [Category(Zone), Description("Zone ID used as town map basis reference.")]
    public ushort TownmapBasisPosZone { get => BitConverter.ToUInt16(Data, 0x4C); set => BitConverter.GetBytes(value).CopyTo(Data, 0x4C); }

    [Category(Zone), Description("Subscreen map type value.")]
    public byte SubscreenMapType { get => Data[0x4E]; set => Data[0x4E] = value; }

    [Category(Zone), Description("Rotom UI effect type value.")]
    public byte RotomEffectType { get => Data[0x4F]; set => Data[0x4F] = value; }

    #endregion

    // Info Tracking
    public void SetZoneName(string[] locationList, int index)
    {
        LocationName = (locationList != null && PlaceNameId < locationList.Length) ? locationList[PlaceNameId] : $"Unknown ({PlaceNameId})";
        Name = $"{index:000} - {LocationName}";
        Index = index;
    }

    public static ZoneData7[] GetArray(byte[] zoneData)
    {
        ZoneData7[] zd = new ZoneData7[zoneData.Length / SIZE];
        for (int i = 0; i < zd.Length; i++)
            zd[i] = new ZoneData7(zoneData, i);
        return zd;
    }

    public static ZoneData7[] GetZoneData7Array(byte[] zoneData, byte[] worldData, string[] locationList, byte[][] worlds)
    {
        var zones = GetArray(zoneData);
        for (int i = 0; i < zones.Length; i++)
        {
            zones[i].WorldIndex = BitConverter.ToUInt16(worldData, i * 0x2);
            zones[i].SetZoneName(locationList, i);

            var world = worlds[zones[i].WorldIndex];
            var mappingOffset = BitConverter.ToInt32(world, 0x8);
            for (var ofs = mappingOffset; ofs < world.Length; ofs += 4)
            {
                if (BitConverter.ToUInt16(world, ofs) != i)
                    continue;
                zones[i].AreaIndex = BitConverter.ToUInt16(world, ofs + 2);
                break;
            }
        }
        return zones;
    }
}

/// <summary>
/// One-bit flags packed into <see cref="ZoneData7.Flags"/>. Bit order/meanings come from the
/// community-authored ZoneDataPattern.hexpat (its own author notes this "is NOT foolproof at
/// identifying each structure properly 100% of the time") - treat exact bit positions as the best
/// available evidence, not certainty, until cross-checked against real in-game behavior.
/// </summary>
[Flags]
public enum ZoneFlags7 : uint
{
    None = 0,
    PokerideRun = 1 << 0,
    PokerideFly = 1 << 1,
    PokerideDowsing = 1 << 2,
    BgmChange = 1 << 3,
    Escape = 1 << 4,
    Skybox = 1 << 5,
    Dash = 1 << 6,
    Flash = 1 << 7,
    Reverb = 1 << 8,
    Breath = 1 << 9,
    Communication = 1 << 10,
    Bloom = 1 << 11,
    StereoDraw = 1 << 12,
    EscapeSave = 1 << 13,
    SubscreenMapMode = 1 << 14,
    SubscreenSleep = 1 << 15,
    TownmapMode = 1 << 16,
    MoveModelDrawNumControl = 1 << 17,
    DispLowPriorityForTraffic = 1 << 18,
    ChrGogglesOn = 1 << 19,
}
