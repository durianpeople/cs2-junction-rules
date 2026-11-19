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
        /// <summary>
        /// JR-T-021 (building record only): the building's entrance boxes are explicit ExitKind.Entrance keys; the one-time
        /// migration from 1.0.1's derived entrances (Core AccessRules.MigrateEntrances) has run or is not needed (record made
        /// by this build). Older builds keep the byte as read and ignore the bit.
        /// </summary>
        EntrancesExplicit = 2,
    }

    /// <summary>Byte source for <see cref="BoxRecord.Decode"/>: the component wraps the game's IReader (its position is shared state, so a struct copy reads on).</summary>
    public interface IByteSource { byte Next(); }

    /// <summary>Byte sink for <see cref="BoxRecord.Encode"/>: the component wraps the game's IWriter.</summary>
    public interface IByteSink { void Put(byte b); }

    public struct BoxRecord : IEquatable<BoxRecord>
    {
        public const byte CurrentVersion = 1;
        public const byte V1PayloadLength = 1;

        public BoxRecordFlags Flags;

        public BoxRecord(BoxRecordFlags flags) { Flags = flags; }

        /// <summary>Writes one entity's record (version, length, payload). No allocation (runs inside the game's serializer job).</summary>
        public void Encode<TSink>(ref TSink sink) where TSink : struct, IByteSink
        {
            sink.Put(CurrentVersion);
            sink.Put(V1PayloadLength);
            sink.Put((byte)Flags);
        }

        /// <summary>
        /// Reads one entity's record. Consumes exactly 2 + length bytes whatever the version. Unknown newer versions keep
        /// the fields this build knows (version 1 fields stay at the same offsets in every later version). Returns false
        /// (record = default) only for version 0, which no build writes. No allocation.
        /// </summary>
        public static bool Decode<TSource>(ref TSource source, out BoxRecord record, out byte version) where TSource : struct, IByteSource
        {
            record = default;
            version = source.Next();
            int length = source.Next();
            for (int i = 0; i < length; i++)
            {
                byte b = source.Next();
                if (i == 0) record.Flags = (BoxRecordFlags)b;
            }
            if (version == 0) { record = default; return false; }
            return true;
        }

        public bool Equals(BoxRecord other) => Flags == other.Flags;
        public override bool Equals(object obj) => obj is BoxRecord r && Equals(r);
        public override int GetHashCode() => (int)Flags;
    }

    /// <summary>Array-backed source / sink (tests and the block model).</summary>
    public struct ArrayBytes : IByteSource, IByteSink
    {
        public byte[] Data;
        public int Pos;
        public List<byte> Out;
        public byte Next() => Data[Pos++];
        public void Put(byte b) => Out.Add(b);

        public static byte[] Encode(BoxRecord r)
        {
            var s = new ArrayBytes { Out = new List<byte>() };
            r.Encode(ref s);
            return s.Out.ToArray();
        }
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
            foreach (var r in records) body.AddRange(ArrayBytes.Encode(r));
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
            var src = new ArrayBytes { Data = stream, Pos = pos + 4 };
            for (int i = 0; i < count; i++)
            {
                BoxRecord.Decode(ref src, out BoxRecord r, out _);
                into.Add(r);
            }
            return src.Pos - (pos + 4) == size ? src.Pos : -1;
        }
    }
}
