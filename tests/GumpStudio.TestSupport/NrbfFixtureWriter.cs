using System.Text;

namespace GumpStudio.TestSupport;

/// <summary>A value in a synthesized NRBF payload.</summary>
public abstract record NrbfValue;

/// <summary>A 32-bit integer member.</summary>
public sealed record NrbfInt(int Value) : NrbfValue;

/// <summary>A boolean member.</summary>
public sealed record NrbfBool(bool Value) : NrbfValue;

/// <summary>A string member.</summary>
public sealed record NrbfString(string Value) : NrbfValue;

/// <summary>A null reference typed as a class, which NRBF must still declare.</summary>
public sealed record NrbfNull(string TypeName, string? Library = null) : NrbfValue;

/// <summary>An object with named members.</summary>
/// <param name="TypeName">Fully qualified type name as the original wrote it.</param>
/// <param name="Library">Assembly name, or null for a framework type.</param>
/// <param name="Members">Members in declaration order.</param>
public sealed record NrbfObject(
    string TypeName,
    string? Library,
    params (string Name, NrbfValue Value)[] Members) : NrbfValue;

/// <summary>An <c>object[]</c>.</summary>
public sealed record NrbfObjectArray(params NrbfValue[] Items) : NrbfValue;

/// <summary>
/// Writes MS-NRBF payloads: the wire format <c>BinaryFormatter</c> produced.
/// </summary>
/// <remarks>
/// <para>
/// Exists so the legacy importer can be tested without shipping a real 1.8
/// <c>.gump</c> file, and without resurrecting <c>BinaryFormatter</c> to produce
/// one — it is removed from modern .NET and banned across this repository.
/// </para>
/// <para>
/// This implements only the subset the 1.8 format uses: classes with named
/// members, strings, int32, boolean, object arrays and nulls. It is a test
/// fixture, not a general serializer.
/// </para>
/// </remarks>
public sealed class NrbfFixtureWriter
{
    // MS-NRBF record type identifiers.
    private const byte SerializedStreamHeader = 0;
    private const byte ClassWithMembersAndTypes = 5;
    private const byte SystemClassWithMembersAndTypes = 4;
    private const byte BinaryObjectString = 6;
    private const byte ObjectNull = 10;
    private const byte MessageEnd = 11;
    private const byte BinaryLibrary = 12;
    private const byte ArraySingleObject = 16;

    // MS-NRBF BinaryTypeEnum values.
    private const byte TypePrimitive = 0;
    private const byte TypeString = 1;
    private const byte TypeSystemClass = 3;
    private const byte TypeClass = 4;
    private const byte TypeObjectArray = 5;

    // MS-NRBF PrimitiveTypeEnum values.
    private const byte PrimitiveBoolean = 1;
    private const byte PrimitiveInt32 = 8;

    private readonly Dictionary<string, int> _libraries = new(StringComparer.Ordinal);
    private int _nextObjectId = 1;

    /// <summary>Serialises one object graph into a self-contained NRBF payload.</summary>
    public byte[] Write(NrbfValue root)
    {
        ArgumentNullException.ThrowIfNull(root);

        _libraries.Clear();
        _nextObjectId = 1;

        using MemoryStream body = new();
        using BinaryWriter writer = new(body, Encoding.UTF8, leaveOpen: true);

        // The root's id must appear in the header, which precedes it, so reserve
        // it before writing anything.
        int rootId = _nextObjectId++;

        using MemoryStream graph = new();
        using BinaryWriter graphWriter = new(graph, Encoding.UTF8, leaveOpen: true);

        CollectLibraries(root);
        WriteRecord(graphWriter, root, rootId);

        writer.Write(SerializedStreamHeader);
        writer.Write(rootId);
        writer.Write(-1);
        writer.Write(1);
        writer.Write(0);

        foreach ((string name, int id) in _libraries)
        {
            writer.Write(BinaryLibrary);
            writer.Write(id);
            WriteString(writer, name);
        }

        graphWriter.Flush();
        writer.Write(graph.ToArray());
        writer.Write(MessageEnd);
        writer.Flush();

        return body.ToArray();
    }

    /// <summary>Writes several payloads back to back, as a <c>.gump</c> file does.</summary>
    public byte[] WriteAll(params NrbfValue[] roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        using MemoryStream combined = new();

        foreach (NrbfValue root in roots)
        {
            byte[] payload = Write(root);

            combined.Write(payload);
        }

        return combined.ToArray();
    }

    private void CollectLibraries(NrbfValue value)
    {
        switch (value)
        {
            case NrbfObject obj:
                if (obj.Library is { } library && !_libraries.ContainsKey(library))
                {
                    _libraries[library] = _nextObjectId++;
                }

                foreach ((_, NrbfValue member) in obj.Members)
                {
                    CollectLibraries(member);
                }

                break;

            case NrbfObjectArray array:
                foreach (NrbfValue item in array.Items)
                {
                    CollectLibraries(item);
                }

                break;

            case NrbfNull nullValue:
                if (nullValue.Library is { } nullLibrary && !_libraries.ContainsKey(nullLibrary))
                {
                    _libraries[nullLibrary] = _nextObjectId++;
                }

                break;

            default:
                break;
        }
    }

    private void WriteRecord(BinaryWriter writer, NrbfValue value, int objectId)
    {
        switch (value)
        {
            case NrbfObject obj:
                WriteClass(writer, obj, objectId);
                break;

            case NrbfObjectArray array:
                writer.Write(ArraySingleObject);
                writer.Write(objectId);
                writer.Write(array.Items.Length);

                foreach (NrbfValue item in array.Items)
                {
                    WriteMemberValue(writer, item);
                }

                break;

            case NrbfString text:
                writer.Write(BinaryObjectString);
                writer.Write(objectId);
                WriteString(writer, text.Value);
                break;

            default:
                throw new NotSupportedException($"{value.GetType().Name} cannot be a record root.");
        }
    }

    private void WriteClass(BinaryWriter writer, NrbfObject obj, int objectId)
    {
        bool isSystemClass = obj.Library is null;

        writer.Write(isSystemClass ? SystemClassWithMembersAndTypes : ClassWithMembersAndTypes);

        // ClassInfo
        writer.Write(objectId);
        WriteString(writer, obj.TypeName);
        writer.Write(obj.Members.Length);

        foreach ((string name, _) in obj.Members)
        {
            WriteString(writer, name);
        }

        // MemberTypeInfo: every member's BinaryTypeEnum, then their extra info.
        foreach ((_, NrbfValue member) in obj.Members)
        {
            writer.Write(GetBinaryType(member));
        }

        foreach ((_, NrbfValue member) in obj.Members)
        {
            WriteAdditionalTypeInfo(writer, member);
        }

        if (!isSystemClass)
        {
            writer.Write(_libraries[obj.Library!]);
        }

        // Member values, in declaration order.
        foreach ((_, NrbfValue member) in obj.Members)
        {
            WriteMemberValue(writer, member);
        }
    }

    private static byte GetBinaryType(NrbfValue value) => value switch
    {
        NrbfInt or NrbfBool => TypePrimitive,
        NrbfString => TypeString,
        NrbfObjectArray => TypeObjectArray,
        NrbfObject obj => obj.Library is null ? TypeSystemClass : TypeClass,
        NrbfNull nullValue => nullValue.Library is null ? TypeSystemClass : TypeClass,
        _ => throw new NotSupportedException($"No binary type for {value.GetType().Name}."),
    };

    private void WriteAdditionalTypeInfo(BinaryWriter writer, NrbfValue value)
    {
        switch (value)
        {
            case NrbfInt:
                writer.Write(PrimitiveInt32);
                break;

            case NrbfBool:
                writer.Write(PrimitiveBoolean);
                break;

            case NrbfObject obj when obj.Library is null:
                WriteString(writer, obj.TypeName);
                break;

            case NrbfObject obj:
                WriteString(writer, obj.TypeName);
                writer.Write(_libraries[obj.Library!]);
                break;

            case NrbfNull nullValue when nullValue.Library is null:
                WriteString(writer, nullValue.TypeName);
                break;

            case NrbfNull nullValue:
                WriteString(writer, nullValue.TypeName);
                writer.Write(_libraries[nullValue.Library!]);
                break;

            default:
                // String and ObjectArray carry no additional type info.
                break;
        }
    }

    private void WriteMemberValue(BinaryWriter writer, NrbfValue value)
    {
        switch (value)
        {
            case NrbfInt number:
                // Primitives are written inline, with no record header.
                writer.Write(number.Value);
                break;

            case NrbfBool flag:
                writer.Write(flag.Value);
                break;

            case NrbfNull:
                writer.Write(ObjectNull);
                break;

            default:
                WriteRecord(writer, value, _nextObjectId++);
                break;
        }
    }

    /// <summary>Writes a length-prefixed UTF-8 string using 7-bit encoded length.</summary>
    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        uint length = (uint)bytes.Length;

        while (length >= 0x80)
        {
            writer.Write((byte)(length | 0x80));
            length >>= 7;
        }

        writer.Write((byte)length);
        writer.Write(bytes);
    }
}
