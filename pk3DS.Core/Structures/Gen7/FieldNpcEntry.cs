using System;

namespace pk3DS.Core.Structures
{
    /// <summary>
    /// One placement record from the per-zone "EM" (character/NPC) table inside `ED`'s Mini-packed
    /// sub-parts (see <see cref="FieldNpcTable"/>) - confirmed live this session, cross-referenced
    /// against a real PawnGarcEditor "pgeif" placement export (`kind: "01_EM"`) for Iki Town: this is
    /// the actual per-NPC dialogue/script hookup, separate from `OWTrainerFile`'s trainer-battle-only
    /// table and from the `AC`/`CP` model-placement container (which only holds the visual model,
    /// no script reference at all).
    /// <para>
    /// 120 bytes per entry. Position/rotation fields confirmed by direct comparison against the pgeif
    /// export's own `x`/`y`/`z` values (byte-exact match). <see cref="CharacterId"/>/
    /// <see cref="EventId"/>/<see cref="ScriptId"/> confirmed the same way, disambiguated using a
    /// real entry whose three values were all distinct (character_id=177, event_id=10, script_id=3)
    /// to pin down field order unambiguously - a coincidence-proof identification, not a guess.
    /// </para>
    /// <para>
    /// <see cref="ScriptId"/> does NOT reference a separate file (unlike <see cref="OWTrainerEntry.BattleScriptID"/>'s
    /// real-trainer-id-plus-1000 convention) - it's a dispatch parameter into this SAME zone-group's
    /// own single, large "ZS" (Zone Script) file, which contains one big Pawn `switch` statement with
    /// one `case` per distinct NPC behavior - confirmed live by decoding Iki Town's real ZS[0] via
    /// <see cref="Script"/>/<see cref="ScriptEditing"/> and finding a switch whose case labels are
    /// exactly 1 through 26, matching every non-zero `ScriptId` seen in that zone's real placement
    /// data - not a coincidence, an exact range match. `ScriptId=0` falls through to that switch's
    /// default case, and empirically covers every "generic filler townsperson" NPC with no unique
    /// dialogue (confirmed: several real entries with plain small `CharacterId`s all share
    /// `ScriptId=0`).
    /// </para>
    /// <para>
    /// <see cref="DetailPoolStart"/>/<see cref="DetailPoolEnd"/> point into a shared pool of 28-byte
    /// detail blocks (further position + two float "radius"-shaped values, purpose not fully pinned
    /// down - plausibly vision/interaction range) appended after all the index records - confirmed
    /// live that these offsets are relative to the record array's own start (byte 0x14 of the parent
    /// blob), NOT the blob's absolute start (verified: a real entry's own recorded start/end,
    /// interpreted with a +0x14 base, landed EXACTLY on a real, matching-position detail block -
    /// interpreting them as blob-absolute instead pointed at plain zero padding, so the +0x14-relative
    /// reading is the confirmed one). Multiple entries CAN reference the identical pool span
    /// (confirmed: two real entries in one zone shared the exact same detail bytes) - the established
    /// "clone a real, already-valid reference rather than synthesize new pool data" approach applies
    /// here exactly as it did for <see cref="OWTrainerEntry"/>'s own collision/path offsets.
    /// </para>
    /// </summary>
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

        /// <summary>A small, per-entry sequential id (846, 847, 848... in the one real sample checked) - purpose not confirmed, plausibly an internal record id unrelated to script dispatch.</summary>
        public uint SequentialId
        {
            get => BitConverter.ToUInt32(RawData, 0x20);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x20);
        }

        /// <summary>Confirmed byte-exact against a real pgeif export's own "event_id" field - a small, zone-local id (1..N, N = this zone's own real entry count).</summary>
        public uint EventId
        {
            get => BitConverter.ToUInt32(RawData, 0x28);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x28);
        }

        /// <summary>Seen as a constant `5` on every real NPC-type entry checked - plausibly a "record kind" marker distinguishing this table's own entry types, not confirmed beyond that.</summary>
        public uint Kind
        {
            get => BitConverter.ToUInt32(RawData, 0x2C);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x2C);
        }

        /// <summary>Confirmed byte-exact against a real pgeif export's own "character_id" field - which NPC appearance/costume this placement uses (separate from the AC/CP model container's own model reference).</summary>
        public uint CharacterId
        {
            get => BitConverter.ToUInt32(RawData, 0x30);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x30);
        }

        /// <summary>Confirmed byte-exact against a real pgeif export's own "script_id" field - see the class remarks for what this actually dispatches to (a switch case in this zone-group's own ZS script, NOT a separate file reference).</summary>
        public uint ScriptId
        {
            get => BitConverter.ToUInt32(RawData, 0x34);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x34);
        }

        /// <summary>Start of this entry's own detail-pool span, relative to the record array's own start (add the record array's own base offset within the parent blob - always 0x14 - to get a blob-absolute address).</summary>
        public uint DetailPoolStart
        {
            get => BitConverter.ToUInt32(RawData, 0x6C);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x6C);
        }

        /// <summary>End (exclusive) of this entry's own detail-pool span, same relative-to-0x14 base as <see cref="DetailPoolStart"/>.</summary>
        public uint DetailPoolEnd
        {
            get => BitConverter.ToUInt32(RawData, 0x70);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x70);
        }

        /// <summary>Seen as a constant `1` on every real entry checked - plausibly a detail-block-count or format-version field, not confirmed beyond that.</summary>
        public uint DetailBlockCount
        {
            get => BitConverter.ToUInt32(RawData, 0x74);
            set => BitConverter.GetBytes(value).CopyTo(RawData, 0x74);
        }
    }
}
