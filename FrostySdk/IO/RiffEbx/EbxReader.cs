using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.Sdk;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO.RiffEbx;

public class EbxReader : BaseEbxReader
{
    private long m_payloadOffset;
    private EbxFixup m_fixup;
    private readonly EbxTypeResolver m_typeResolver;

    private readonly Dictionary<uint, EbxExtra> m_boxedValues = new();

    public static HashSet<string> Types = new();

    public EbxReader(DataStream inStream)
        : base(inStream)
    {
        RiffStream riffStream = new(m_stream);
        riffStream.ReadHeader(out FourCC fourCc, out uint size);

        if (fourCc != "EBX\x0" && fourCc != "EBXS")
        {
            throw new InvalidDataException("Not a valid ebx chunk");
        }

        // read EBXD chunk
        riffStream.ReadNextChunk(ref size, ProcessChunk);
        // read EFIX chunk
        riffStream.ReadNextChunk(ref size, ProcessChunk);

        m_typeResolver = new EbxTypeResolver(m_fixup.TypeGuids, m_fixup.TypeSignatures);

        // read EBXX chunk
        riffStream.ReadNextChunk(ref size, ProcessChunk);

        Debug.Assert(riffStream.Eof);
    }

    public override Guid GetPartitionGuid() => m_fixup.PartitionGuid;

    public override string GetRootType()
    {
        m_stream.Position = m_payloadOffset + m_fixup.InstanceOffsets[0];
        return TypeLibrary.GetType(m_fixup.TypeGuids[m_stream.ReadUInt16()])?.GetName() ?? string.Empty;
    }

    public override HashSet<Guid> GetDependencies() => m_fixup.Dependencies;

    private void ProcessChunk(DataStream inStream, FourCC inFourCc, uint inSize)
    {
        switch ((string)inFourCc)
        {
            case "EBXD":
                ReadDataChunk(inStream);
                break;
            case "EFIX":
                ReadFixupChunk(inStream);
                break;
            case "EBXX":
                ReadXChunk(inStream);
                break;
        }
    }

    protected override void InternalReadObjects()
    {
        int[] typeRefs = new int[m_fixup.InstanceOffsets.Count];
        int j = 0;
        foreach (uint offset in m_fixup.InstanceOffsets)
        {
            m_stream.Position = m_payloadOffset + offset;
            ushort typeRef = m_stream.ReadUInt16();
            typeRefs[j++] = typeRef;

            m_objects.Add(TypeLibrary.CreateObject(m_fixup.TypeGuids[typeRef]) ?? throw new Exception());
            m_refCounts.Add(0);
        }

        m_stream.Position = m_payloadOffset;

        for (int i = 0; i < m_fixup.InstanceOffsets.Count; i++)
        {
            EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(typeRefs[i]);

            m_stream.Pad(typeDescriptor.Alignment);

            Guid instanceGuid = i < m_fixup.ExportedInstanceCount ? m_stream.ReadGuid(): Guid.Empty;

            Debug.Assert(m_stream.Position - m_payloadOffset == m_fixup.InstanceOffsets[i]);

            object obj = m_objects[i];
            ((dynamic)obj).SetInstanceGuid(new AssetClassGuid(instanceGuid, i));
            ReadType(typeDescriptor, obj, m_stream.Position);
        }
    }

    private void ReadFixupChunk(DataStream inStream)
    {
        m_fixup = EbxFixup.ReadFixup(inStream);
    }

    private void ReadDataChunk(DataStream inStream)
    {
        inStream.Pad(16);
        m_payloadOffset = inStream.Position;
    }

    private void ReadXChunk(DataStream inStream)
    {
        int arrayCount = inStream.ReadInt32();
        int boxedValueCount = inStream.ReadInt32();
        m_boxedValues.EnsureCapacity(boxedValueCount);

        inStream.Position += arrayCount * (sizeof(uint) + sizeof(int) + sizeof(uint) + sizeof(ushort) + sizeof(ushort));

        for (int i = 0; i < boxedValueCount; i++)
        {
            EbxExtra b = new()
            {
                Offset = inStream.ReadUInt32(),
                Count = inStream.ReadInt32(),
                Hash = inStream.ReadUInt32(),
                Flags = inStream.ReadUInt16(),
                TypeDescriptorRef = inStream.ReadUInt16()
            };

            m_boxedValues.Add(b.Offset, b);
        }
    }

    private void ReadType(EbxTypeDescriptor inTypeDescriptor, object? obj, long inStartOffset)
    {
        if (obj is null)
        {
            m_stream.Position = inStartOffset + inTypeDescriptor.Size;
            m_stream.Pad(inTypeDescriptor.Alignment);
            return;
        }

        Type objType = obj.GetType();

        for (int i = 0; i < inTypeDescriptor.FieldCount; i++)
        {
            EbxFieldDescriptor fieldDescriptor = m_typeResolver.ResolveField(inTypeDescriptor.FieldIndex + i);
            PropertyInfo? propertyInfo = objType.GetProperties().FirstOrDefault(prop =>
                prop.GetCustomAttribute<NameHashAttribute>()?.Hash == fieldDescriptor.NameHash);

            m_stream.Position = inStartOffset + fieldDescriptor.DataOffset;

            TypeFlags.TypeEnum type = fieldDescriptor.Flags.GetTypeEnum();

            if (fieldDescriptor.Flags.GetCategoryEnum() == TypeFlags.CategoryEnum.Array)
            {
                ReadArray(type, fieldDescriptor.TypeDescriptorRef, value =>
                {
                    if (value is null)
                    {
                        Debug.Assert(propertyInfo is null, "Struct does not exist in TypeInfo");
                        return;
                    }

                    if (typeof(IPrimitive).IsAssignableFrom(propertyInfo?.PropertyType.GenericTypeArguments[0]))
                    {
                        IPrimitive primitive = (IPrimitive)Activator.CreateInstance(propertyInfo.PropertyType.GenericTypeArguments[0])!;
                        primitive.FromActualType(value);
                        value = primitive;
                    }

                    if (typeof(IDelegate).IsAssignableFrom(propertyInfo?.PropertyType))
                    {
                        IDelegate @delegate = (IDelegate)Activator.CreateInstance(propertyInfo.PropertyType)!;
                        @delegate.FunctionType = (Type)value;
                        value = @delegate;
                    }

                    IList? list = (IList?)propertyInfo?.GetValue(obj);
                    list?.Add(value);
                });
            }
            else
            {
                switch (type)
                {
                    case TypeFlags.TypeEnum.Inherited:
                        ReadType(m_typeResolver.ResolveTypeFromField(fieldDescriptor.TypeDescriptorRef), obj, inStartOffset);
                        break;
                    default:
                        ReadField(type, fieldDescriptor.TypeDescriptorRef, value =>
                        {
                            if (value is null)
                            {
                                Debug.Assert(propertyInfo is null, "Struct does not exist in TypeInfo");
                                return;
                            }

                            if (typeof(IPrimitive).IsAssignableFrom(propertyInfo?.PropertyType))
                            {
                                IPrimitive primitive = (IPrimitive)Activator.CreateInstance(propertyInfo.PropertyType)!;
                                primitive.FromActualType(value);
                                value = primitive;
                            }

                            if (typeof(IDelegate).IsAssignableFrom(propertyInfo?.PropertyType))
                            {
                                IDelegate @delegate = (IDelegate)Activator.CreateInstance(propertyInfo.PropertyType)!;
                                @delegate.FunctionType = (Type)value;
                                value = @delegate;
                            }
                            propertyInfo?.SetValue(obj, value);
                        });
                        break;
                }
            }
        }

        // ordering of fields is weird
        m_stream.Position = inStartOffset + inTypeDescriptor.Size;
    }

    private void ReadField(TypeFlags.TypeEnum inType, Action<object?> inAddFunc)
    {
        switch (inType)
        {
            case TypeFlags.TypeEnum.Boolean:
                inAddFunc(m_stream.ReadBoolean());
                break;
            case TypeFlags.TypeEnum.Int8:
                inAddFunc((sbyte)m_stream.ReadByte());
                break;
            case TypeFlags.TypeEnum.UInt8:
                inAddFunc(m_stream.ReadByte());
                break;
            case TypeFlags.TypeEnum.Int16:
                inAddFunc(m_stream.ReadInt16());
                break;
            case TypeFlags.TypeEnum.UInt16:
                inAddFunc(m_stream.ReadUInt16());
                break;
            case TypeFlags.TypeEnum.Int32:
                inAddFunc(m_stream.ReadInt32());
                break;
            case TypeFlags.TypeEnum.UInt32:
                inAddFunc(m_stream.ReadUInt32());
                break;
            case TypeFlags.TypeEnum.Int64:
                inAddFunc(m_stream.ReadInt64());
                break;
            case TypeFlags.TypeEnum.UInt64:
                inAddFunc(m_stream.ReadUInt64());
                break;
            case TypeFlags.TypeEnum.Float32:
                inAddFunc(m_stream.ReadSingle());
                break;
            case TypeFlags.TypeEnum.Float64:
                inAddFunc(m_stream.ReadDouble());
                break;
            case TypeFlags.TypeEnum.Guid:
                inAddFunc(m_stream.ReadGuid());
                break;
            case TypeFlags.TypeEnum.ResourceRef:
                inAddFunc(ReadResourceRef());
                break;
            case TypeFlags.TypeEnum.Sha1:
                inAddFunc(m_stream.ReadSha1());
                break;
            case TypeFlags.TypeEnum.String:
                inAddFunc(m_stream.ReadFixedSizedString(32));
                break;
            case TypeFlags.TypeEnum.CString:
                inAddFunc(ReadString(m_stream.ReadInt64()));
                break;
            case TypeFlags.TypeEnum.FileRef:
                inAddFunc(ReadFileRef());
                break;
            case TypeFlags.TypeEnum.Delegate:
                inAddFunc(ReadDelegate());
                break;
            case TypeFlags.TypeEnum.TypeRef:
                inAddFunc(ReadTypeRef());
                break;
            case TypeFlags.TypeEnum.BoxedValueRef:
                inAddFunc(ReadBoxedValueRef());
                break;
            case TypeFlags.TypeEnum.Enum:
                inAddFunc(m_stream.ReadInt32());
                break;
            case TypeFlags.TypeEnum.Class:
                inAddFunc(ReadPointerRef());
                break;
            case TypeFlags.TypeEnum.Array:
                throw new InvalidDataException("Array");
            case TypeFlags.TypeEnum.DbObject:
                throw new InvalidDataException("DbObject");
            default:
                throw new InvalidDataException("Unknown");
        }
    }

    private void ReadField(TypeFlags.TypeEnum inType, ushort inTypeDescriptorRef, Action<object?> inAddFunc)
    {
        switch (inType)
        {
            case TypeFlags.TypeEnum.Struct:
                EbxTypeDescriptor structType = m_typeResolver.ResolveTypeFromField(inTypeDescriptorRef);
                m_stream.Pad(structType.Alignment);
                object? obj = TypeLibrary.CreateObject(structType.NameHash);
                ReadType(structType, obj, m_stream.Position);
                inAddFunc(obj);
                break;
            default:
                ReadField(inType, inAddFunc);
                break;
        }
    }

    private void ReadField(TypeFlags.TypeEnum inType,
        int inTypeDescriptorRef, Action<object?> inAddFunc)
    {
        switch (inType)
        {
            case TypeFlags.TypeEnum.Struct:
                EbxTypeDescriptor structType = m_typeResolver.ResolveType(inTypeDescriptorRef);
                m_stream.Pad(structType.Alignment);
                object? obj = TypeLibrary.CreateObject(structType.NameHash);
                ReadType(structType, obj, m_stream.Position);
                inAddFunc(obj);
                break;
            default:
                ReadField(inType, inAddFunc);
                break;
        }
    }

    private void ReadArray(TypeFlags.TypeEnum inType, ushort inTypeDescriptorRef,
        Action<object?> inAddFunc)
    {
        int offset = m_stream.ReadInt32();

        long arrayPos = m_stream.Position;

        m_stream.Position += offset - 8;

        int count = m_stream.ReadInt32();

        for (int i = 0; i < count; i++)
        {
            ReadField(inType, inTypeDescriptorRef, inAddFunc);
        }

        m_stream.Position = arrayPos;
    }

    private void ReadArray(TypeFlags.TypeEnum inType, int inTypeDescriptorRef,
        Action<object?> inAddFunc)
    {
        int offset = m_stream.ReadInt32();

        long arrayPos = m_stream.Position;

        m_stream.Position += offset - 8;

        int count = m_stream.ReadInt32();

        for (int i = 0; i < count; i++)
        {
            ReadField(inType, inTypeDescriptorRef, inAddFunc);
        }

        m_stream.Position = arrayPos;
    }

    private string ReadString(long offset)
    {
        if (offset == -1)
        {
            return string.Empty;
        }

        long pos = m_stream.Position;
        m_stream.Position += offset - 8;

        string retStr = m_stream.ReadNullTerminatedString();
        m_stream.Position = pos;

        return retStr;
    }

    private ResourceRef ReadResourceRef() => new(m_stream.ReadUInt64());

    private FileRef ReadFileRef()
    {
        return new FileRef(ReadString(m_stream.ReadInt64()));
    }

    private PointerRef ReadPointerRef()
    {
        int index = (int)m_stream.ReadInt64();

        if (index == 0)
        {
            return new PointerRef();
        }

        if ((index & 1) == 1)
        {
            return new PointerRef(m_fixup.Imports[index >> 1]);
        }

        long instanceOffset = m_stream.Position - 8 + index - m_payloadOffset;
        int objIndex = m_fixup.InstanceMapping[(uint)instanceOffset];

        m_refCounts[objIndex]++;
        return new PointerRef(m_objects[objIndex]);
    }

    private TypeRef ReadTypeRef()
    {
        uint packed = m_stream.ReadUInt32();
        m_stream.Position += 4; // index, 0 (index in packed) or -1 (primitive)

        if (packed == 0)
        {
            return new TypeRef();
        }

        if ((packed & 0x80000000) != 0)
        {
            // primitive type
            return new TypeRef(GetTypeFromEbxField((TypeFlags)(packed & ~0x80000000), -1));
        }

        int typeRef = (int)(packed >> 2);
        return new TypeRef(m_fixup.TypeGuids[typeRef]);
    }

    private Type? ReadDelegate()
    {
        uint packed = m_stream.ReadUInt32();
        m_stream.Position += 4;

        if (packed == 0)
        {
            return null;
        }

        if ((packed & 0x80000000) != 0)
        {
            // primitive type
            return GetTypeFromEbxField((TypeFlags)(packed & ~0x80000000), -1);
        }

        int typeRef = (int)(packed >> 2);
        return TypeLibrary.GetType(m_fixup.TypeGuids[typeRef]);
    }

    private BoxedValueRef ReadBoxedValueRef()
    {
        uint packed = m_stream.ReadUInt32();
        m_stream.Position += 4;
        long offset = m_stream.ReadInt64();

        if (packed == 0)
        {
            return new BoxedValueRef();
        }

        long pos = m_stream.Position;
        m_stream.Position += offset - 8;


        object? value = null;
        int typeRef = -1;
        TypeFlags.TypeEnum type;
        TypeFlags.CategoryEnum category;
        EbxExtra b = m_boxedValues[(uint)(m_stream.Position - m_payloadOffset)];
        if ((packed & 0x80000000) != 0)
        {
            TypeFlags flags = (TypeFlags)(packed & ~0x80000000);
            type = flags.GetTypeEnum();
            category = flags.GetCategoryEnum();
        }
        else
        {
            typeRef = (int)(packed >> 2);
            type = b.Flags.GetTypeEnum();
            category = b.Flags.GetCategoryEnum();
        }
        Type fieldType = GetTypeFromEbxField(new TypeFlags(type, category), typeRef)!;

        if (category == TypeFlags.CategoryEnum.Array)
        {
            value = Activator.CreateInstance(fieldType)!;
            ReadArray(type, (int)b.TypeDescriptorRef, obj =>
            {
                if (obj is null)
                {
                    return;
                }

                if (typeof(IPrimitive).IsAssignableFrom(fieldType.GenericTypeArguments[0]))
                {
                    IPrimitive primitive = (IPrimitive)Activator.CreateInstance(fieldType.GenericTypeArguments[0])!;
                    primitive.FromActualType(obj);
                    obj = primitive;
                }

                ((IList?)value)?.Add(obj);
            });
        }
        else
        {
            switch (type)
            {
                case TypeFlags.TypeEnum.Enum:
                    ReadField(type, typeRef, obj =>
                    {
                        value = Enum.Parse(fieldType, obj!.ToString()!);
                    });
                    break;
                default:
                    ReadField(type, typeRef, obj =>
                    {
                        if (obj is null)
                        {
                            return;
                        }

                        if (typeof(IPrimitive).IsAssignableFrom(fieldType))
                        {
                            IPrimitive primitive = (IPrimitive)Activator.CreateInstance(fieldType)!;
                            primitive.FromActualType(obj);
                            obj = primitive;
                        }

                        value = obj;
                    });
                    break;
            }
        }

        m_stream.Position = pos;

        return new BoxedValueRef(value, type);
    }

    private Type? GetTypeFromEbxField(TypeFlags inFlags, int inTypeDescriptorRef)
    {
        Type type;
        switch (inFlags.GetTypeEnum())
        {
            case TypeFlags.TypeEnum.Inherited: type = s_voidType;
                break;
            case TypeFlags.TypeEnum.Struct: type = TypeLibrary.GetType(m_fixup.TypeGuids[inTypeDescriptorRef])!;
                break;
            case TypeFlags.TypeEnum.String: type = s_stringType;
                break;
            case TypeFlags.TypeEnum.Int8: type = s_sbyteType;
                break;
            case TypeFlags.TypeEnum.UInt8: type = s_byteType;
                break;
            case TypeFlags.TypeEnum.Boolean: type = s_boolType;
                break;
            case TypeFlags.TypeEnum.UInt16: type = s_ushortType;
                break;
            case TypeFlags.TypeEnum.Int16: type = s_shortType;
                break;
            case TypeFlags.TypeEnum.UInt32: type = s_uintType;
                break;
            case TypeFlags.TypeEnum.Int32: type = s_intType;
                break;
            case TypeFlags.TypeEnum.UInt64: type = s_ulongType;
                break;
            case TypeFlags.TypeEnum.Int64: type = s_longType;
                break;
            case TypeFlags.TypeEnum.Float32: type = s_floatType;
                break;
            case TypeFlags.TypeEnum.Float64: type = s_doubleType;
                break;
            case TypeFlags.TypeEnum.Class: type = s_pointerType;
                break;
            case TypeFlags.TypeEnum.Guid: type = s_guidType;
                break;
            case TypeFlags.TypeEnum.Sha1: type = s_sha1Type;
                break;
            case TypeFlags.TypeEnum.CString: type = s_cStringType;
                break;
            case TypeFlags.TypeEnum.ResourceRef: type = s_resourceRefType;
                break;
            case TypeFlags.TypeEnum.FileRef: type = s_fileRefType;
                break;
            case TypeFlags.TypeEnum.TypeRef: type = s_typeRefType!;
                break;
            case TypeFlags.TypeEnum.BoxedValueRef: type = s_boxedValueRefType!;
                break;
            case TypeFlags.TypeEnum.Enum:
                type = TypeLibrary.GetType(m_fixup.TypeGuids[inTypeDescriptorRef])!;
                break;

            default:
                throw new NotImplementedException();
        }

        if (inFlags.GetCategoryEnum() == TypeFlags.CategoryEnum.Array && inTypeDescriptorRef == ushort.MaxValue)
        {
            return typeof(ObservableCollection<>).MakeGenericType(type);
        }

        return type;
    }
}