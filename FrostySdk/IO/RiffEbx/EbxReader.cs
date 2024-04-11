using System;
using System.Collections.Generic;
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

    private readonly Dictionary<uint, EbxBoxedValue> m_boxedValues = new();


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
        int[] typeRefs = new int[m_fixup.InstanceOffsets.Length];
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

        for (int i = 0; i < m_fixup.InstanceOffsets.Length; i++)
        {
            EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(typeRefs[i]);

            m_stream.Pad(typeDescriptor.Alignment);

            Guid instanceGuid = i < m_fixup.ExportedInstanceCount ? m_stream.ReadGuid(): Guid.Empty;

            if (typeDescriptor.Alignment != 0x04)
            {
                m_stream.Position += 8;
            }

            object obj = m_objects[i];
            ((dynamic)obj).SetInstanceGuid(new AssetClassGuid(instanceGuid, i));
            ReadType(typeDescriptor, obj, m_stream.Position - 8);
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
            var b = new EbxBoxedValue
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
            m_stream.Position += inTypeDescriptor.Size;
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
                    propertyInfo?.GetValue(obj)?.GetType().GetMethod("Add")?.Invoke(propertyInfo.GetValue(obj), new[] { value });
                });
            }
            else
            {
                switch (type)
                {
                    case TypeFlags.TypeEnum.Inherited:
                        ReadType(m_typeResolver.ResolveType(fieldDescriptor.TypeDescriptorRef), obj, inStartOffset);
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
                            propertyInfo?.SetValue(obj, value);
                        });
                        break;
                }
            }
        }

        // ordering of fields is weird
        m_stream.Position = inStartOffset + inTypeDescriptor.Size;
    }

    private void ReadField(TypeFlags.TypeEnum inType,
        ushort inTypeDescriptorRef, Action<object?> inAddFunc)
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
                inAddFunc(ReadString(m_stream.ReadUInt32()));
                break;
            case TypeFlags.TypeEnum.FileRef:
                inAddFunc(ReadFileRef());
                break;
            case TypeFlags.TypeEnum.Delegate:
            case TypeFlags.TypeEnum.TypeRef:
                inAddFunc(ReadTypeRef());
                break;
            case TypeFlags.TypeEnum.BoxedValueRef:
                inAddFunc(ReadBoxedValueRef());
                break;
            case TypeFlags.TypeEnum.Struct:
                EbxTypeDescriptor structType = m_typeResolver.ResolveType(inTypeDescriptorRef);
                m_stream.Pad(structType.Alignment);
                object? obj = TypeLibrary.CreateObject(structType.NameHash);
                ReadType(structType, obj, m_stream.Position);
                inAddFunc(obj);
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

    private void ReadField(TypeFlags.TypeEnum inType,
        int inTypeDescriptorRef, Action<object?> inAddFunc)
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
                inAddFunc(ReadString(m_stream.ReadUInt32()));
                break;
            case TypeFlags.TypeEnum.FileRef:
                inAddFunc(ReadFileRef());
                break;
            case TypeFlags.TypeEnum.Delegate:
            case TypeFlags.TypeEnum.TypeRef:
                inAddFunc(ReadTypeRef());
                break;
            case TypeFlags.TypeEnum.BoxedValueRef:
                inAddFunc(ReadBoxedValueRef());
                break;
            case TypeFlags.TypeEnum.Struct:
                EbxTypeDescriptor structType = m_typeResolver.ResolveType(inTypeDescriptorRef);
                m_stream.Pad(structType.Alignment);
                object? obj = TypeLibrary.CreateObject(structType.NameHash);
                ReadType(structType, obj, m_stream.Position);
                inAddFunc(obj);
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
        m_stream.Position += offset - 4;

        string retStr = m_stream.ReadNullTerminatedString();
        m_stream.Position = pos;

        return retStr;
    }

    private ResourceRef ReadResourceRef() => new(m_stream.ReadUInt64());

    private FileRef ReadFileRef()
    {
        uint index = (uint)m_stream.ReadUInt64();

        return new FileRef(ReadString(index));
    }

    private PointerRef ReadPointerRef()
    {
        int index = m_stream.ReadInt32();

        if (index == 0)
        {
            return new PointerRef();
        }

        if ((index & 1) == 1)
        {
            return new PointerRef(m_fixup.Imports[index >> 1]);
        }

        long instanceOffset = m_stream.Position - 4 + index - m_payloadOffset;
        int objIndex = m_fixup.InstanceMapping[(uint)instanceOffset];

        m_refCounts[objIndex]++;
        return new PointerRef(m_objects[objIndex]);
    }

    private TypeRef ReadTypeRef()
    {
        uint packed = m_stream.ReadUInt32();
        m_stream.Position += 4;

        if (packed == 0)
        {
            return new TypeRef();
        }

        if ((packed & 0x80000000) != 0)
        {
            // primitive type
            return new TypeRef(GetTypeFromEbxField(((TypeFlags)(packed & ~0x80000000)).GetTypeEnum(), -1).GetName());
        }

        int typeRef = (int)(packed >> 2);
        return new TypeRef(m_fixup.TypeGuids[typeRef]);
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
        if ((packed & 0x80000000) != 0)
        {
            TypeFlags flags = (TypeFlags)(packed & ~0x80000000);
            type = flags.GetTypeEnum();
            category = flags.GetCategoryEnum();
        }
        else
        {
            EbxBoxedValue b = m_boxedValues[(uint)(m_stream.Position - m_payloadOffset)];
            typeRef = (int)(packed >> 2);
            type = b.Flags.GetTypeEnum();
            category = b.Flags.GetCategoryEnum();
        }
        Type fieldType = GetTypeFromEbxField(type, typeRef);

        if (category == TypeFlags.CategoryEnum.Array)
        {
            value = Activator.CreateInstance(fieldType)!;
            ReadArray(type, typeRef, obj =>
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
                fieldType.GetMethod("Add")?.Invoke(value, new[] { obj });
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

    private Type GetTypeFromEbxField(TypeFlags.TypeEnum inFlags, int inTypeDescriptorRef)
    {
        switch (inFlags)
        {
            case TypeFlags.TypeEnum.Struct: return TypeLibrary.GetType(m_fixup.TypeGuids[inTypeDescriptorRef])!;
            case TypeFlags.TypeEnum.String: return s_stringType;
            case TypeFlags.TypeEnum.Int8: return s_sbyteType;
            case TypeFlags.TypeEnum.UInt8: return s_byteType;
            case TypeFlags.TypeEnum.Boolean: return s_boolType;
            case TypeFlags.TypeEnum.UInt16: return s_ushortType;
            case TypeFlags.TypeEnum.Int16: return s_shortType;
            case TypeFlags.TypeEnum.UInt32: return s_uintType;
            case TypeFlags.TypeEnum.Int32: return s_intType;
            case TypeFlags.TypeEnum.UInt64: return s_ulongType;
            case TypeFlags.TypeEnum.Int64: return s_longType;
            case TypeFlags.TypeEnum.Float32: return s_floatType;
            case TypeFlags.TypeEnum.Float64: return s_doubleType;
            case TypeFlags.TypeEnum.Class: return s_pointerType;
            case TypeFlags.TypeEnum.Guid: return s_guidType;
            case TypeFlags.TypeEnum.Sha1: return s_sha1Type;
            case TypeFlags.TypeEnum.CString: return s_cStringType;
            case TypeFlags.TypeEnum.ResourceRef: return s_resourceRefType;
            case TypeFlags.TypeEnum.FileRef: return s_fileRefType;
            case TypeFlags.TypeEnum.TypeRef: return s_typeRefType!;
            case TypeFlags.TypeEnum.BoxedValueRef: return s_boxedValueRefType!;
            case TypeFlags.TypeEnum.Array:
                EbxTypeDescriptor arrayTypeDescriptor = m_typeResolver.ResolveType(inTypeDescriptorRef);
                EbxFieldDescriptor elementFieldDescriptor = m_typeResolver.ResolveField(arrayTypeDescriptor.FieldIndex);
                return typeof(List<>).MakeGenericType(GetTypeFromEbxField(elementFieldDescriptor.Flags.GetTypeEnum(), elementFieldDescriptor.TypeDescriptorRef));
            case TypeFlags.TypeEnum.Enum:
                return TypeLibrary.GetType(m_fixup.TypeGuids[inTypeDescriptorRef])!;

            default:
                throw new NotImplementedException();
        }
    }
}