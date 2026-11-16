using Colossal.Serialization.Entities;
using JunctionRules.Core;
using Unity.Entities;

namespace JunctionRules.Boxes
{
    // 0.4.0-probe (JR-T-007): a saved yellow box = one of these components on the game's own entity (junction: the
    // Game.Net.Node; entrance: the Game.Buildings.Building). Saved with the city through the game's component serializer
    // (mod types join it after TypeManager.InitializeAdditionalTypes + SerializerSystem.SetDirty, Game.Modding/
    // ModManager.cs 147-151). Byte layout and versioning: Core/BoxRecord.cs.
    //
    // Loading a save WITHOUT the mod (decompile of Colossal.Core, Colossal.Serialization.Entities):
    // - ComponentSerializer.DeserializeType (ComponentSerializer.cs 43-73): Type.GetType of the saved assembly-qualified
    //   name fails (no JunctionRules assembly), the type table has no entry, so it logs "Not serializable type" and
    //   returns false.
    // - EntityDeserializer.cs 337-374: such a type gets the ObsoleteComponentSerializer and a null ComponentType; 499-513:
    //   null types are left out of the archetype the entities are created with (the node / building loads without it).
    // - ObsoleteComponentSerializer.cs 21-31, 56-66: its data block is skipped by its size prefix (Begin reads the size,
    //   End jumps to start + size; BinaryReader.cs 36-50), so every later component reads from the right place.
    // - The next save made without the mod does not contain the component (it no longer exists on any entity).
    // Loading WITH a later build of the mod whose assembly version differs: the stripped name in [FormerlySerializedAs]
    // matches the type table (ComponentSerializer.cs 55-63 strips trailing ", ..." parts; ComponentSerializerLibrary.cs
    // 114-121 fills the table from this attribute), so the type still resolves.
    // Not IEmptySerializable (a plain tag would carry no version byte); never writes zero bytes (ComponentDataSerializer.cs
    // 72-75). No Entity fields: nothing to remap, nothing dangling.

    /// <summary>A yellow box on this junction node.</summary>
    [FormerlySerializedAs("JunctionRules.Boxes.YellowBoxJunction, JunctionRules")]
    public struct YellowBoxJunction : IComponentData, IQueryTypeParameter, ISerializable
    {
        public byte m_Flags;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var sink = new WriterSink<TWriter> { m_Writer = writer };
            new BoxRecord((BoxRecordFlags)m_Flags).Encode(ref sink);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            var source = new ReaderSource<TReader> { m_Reader = reader };
            BoxRecord.Decode(ref source, out BoxRecord r, out _);
            m_Flags = (byte)r.Flags;
        }
    }

    /// <summary>A yellow box on the carriageway in front of this building's road access.</summary>
    [FormerlySerializedAs("JunctionRules.Boxes.YellowBoxEntrance, JunctionRules")]
    public struct YellowBoxEntrance : IComponentData, IQueryTypeParameter, ISerializable
    {
        public byte m_Flags;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var sink = new WriterSink<TWriter> { m_Writer = writer };
            new BoxRecord((BoxRecordFlags)m_Flags).Encode(ref sink);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            var source = new ReaderSource<TReader> { m_Reader = reader };
            BoxRecord.Decode(ref source, out BoxRecord r, out _);
            m_Flags = (byte)r.Flags;
        }
    }

    /// <summary>IWriter as a byte sink. The game's writers keep their buffer in native containers, so a copy writes on.</summary>
    internal struct WriterSink<TWriter> : IByteSink where TWriter : IWriter
    {
        public TWriter m_Writer;
        public void Put(byte b) => m_Writer.Write(b);
    }

    /// <summary>IReader as a byte source. BinaryReader keeps its position in a NativeReference (BinaryReader.cs 16), so a copy reads on.</summary>
    internal struct ReaderSource<TReader> : IByteSource where TReader : IReader
    {
        public TReader m_Reader;
        public byte Next() { m_Reader.Read(out byte b); return b; }
    }
}
