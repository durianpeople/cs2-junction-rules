using System;
using System.Collections.Generic;

namespace JunctionRules.Core
{
    // 0.4.0-probe (JR-T-007): the bytes a saved yellow box writes into the save, game-independent so the round trip and
    // the "mod removed" skip are unit-tested (tests BoxRecordTests).
    //
    // Layout, per entity, inside the game's size-prefixed block for the component type:
    //   byte version     (1 = this layout)
    //   byte length      (number of payload bytes that follow, 0..255)
    //   byte[length]     payload; version 1: [0] = flags (BoxRecordFlags), nothing else
    // A newer mod may append payload bytes and raise 'length': this reader reads what it knows and skips the rest, so
    // the game's exact-size check (ComponentDataSerializer.cs 186-189, "Data size mismatch") still holds. Never zero
    // bytes: the game rejects a non-empty-serializable component that writes nothing (ComponentDataSerializer.cs 72-75).
    // No entity references: nothing to remap on load, nothing dangling when the mod is gone.

    [Flags]
    public enum BoxRecordFlags : byte
    {
        None = 0,
        /// <summary>Reserved: the owner turned the box off for driving but kept the paint (not used yet).</summary>
        PaintOnly = 1,
    }

    public struct BoxRecord : IEquatable<BoxRecord>
    {
        public const byte CurrentVersion = 1;
        public const int V1PayloadLength = 1;

        public BoxRecordFlags Flags;

        public BoxRecord(BoxRecordFlags flags) { Flags = flags; }

        /// <summary>The bytes for one entity (version, length, payload).</summary>
        public byte[] Encode()
        {
            return new byte[] { CurrentVersion, V1PayloadLength, (byte)Flags };
        }

        /// <summary>
        /// Reads one entity's record through 'readByte' (the game's IReader.Read(out byte)). Consumes exactly
        /// 2 + length bytes whatever the version. Unknown newer versions keep the fields this build knows (version 1
        /// fields stay at the same offsets in every later version). Returns false (record = default) only for version 0,
        /// which no build writes.
        /// </summary>
        public static bool Decode(Func<byte> readByte, out BoxRecord record, out byte version)
        {
            record = default;
            version = readByte();
            int length = readByte();
            var payload = new byte[length];
            for (int i = 0; i < length; i++) payload[i] = readByte();
            if (version == 0) return false;
            if (length >= 1) record.Flags = (BoxRecordFlags)payload[0];
            return true;
        }

        public bool Equals(BoxRecord other) => Flags == other.Flags;
        public override bool Equals(object obj) => obj is BoxRecord r && Equals(r);
        public override int GetHashCode() => (int)Flags;
    }

    /// <summary>
    /// Offline model of the game's component block (Colossal.Serialization.Entities BinaryWriter.Begin/End and
    /// BinaryReader.Begin/End: an int size written before the data, patched in at End; the reader's End jumps to
    /// start + size; ObsoleteComponentSerializer.cs 21-31 skips an unknown type's block with Begin + End). Used by the
    /// tests to show that a save carrying boxes reads cleanly by a reader that does not know the type.
    /// </summary>
    public static class BlockModel
    {
        /// <summary>Writes one component block: [int32 size][records...].</summary>
        public static void WriteBlock(List<byte> stream, IList<BoxRecord> records)
        {
            var body = new List<byte>();
            foreach (var r in records) body.AddRange(r.Encode());
            stream.AddRange(BitConverter.GetBytes(body.Count));
            stream.AddRange(body);
        }

        /// <summary>Skips one block the way the obsolete serializer does; returns the new position.</summary>
        public static int SkipBlock(byte[] stream, int pos)
        {
            int size = BitConverter.ToInt32(stream, pos);
            return pos + 4 + size;
        }

        /// <summary>Reads 'count' records from the block at 'pos' with exact-size checking; returns the new position or -1 on a size mismatch.</summary>
        public static int ReadBlock(byte[] stream, int pos, int count, List<BoxRecord> into)
        {
            int size = BitConverter.ToInt32(stream, pos);
            int p = pos + 4, start = p;
            for (int i = 0; i < count; i++)
            {
                BoxRecord.Decode(() => stream[p++], out BoxRecord r, out _);
                into.Add(r);
            }
            return p - start == size ? p : -1;
        }
    }
}
