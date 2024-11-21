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
using Microsoft.Extensions.Logging;

namespace Frosty.Sdk.IO.RiffEbx;

public class EbxReader : BaseEbxReader
{
    private long m_payloadOffset;
    private EbxFixup m_fixup;
    private readonly EbxTypeResolver m_typeResolver;

    private readonly Dictionary<uint, EbxExtra> m_boxedValues = new();

    private static readonly string s_collectionName = "ObservableCollection`1";

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
        return TypeLibrary.GetType(m_fixup.TypeGuids[m_stream.ReadUInt16()])?.Name ?? throw new Exception("Unknown type");
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

            m_objects.Add(TypeLibrary.CreateObject(m_typeResolver.ResolveType(typeRef).NameHash));
            m_refCounts.Add(0);
        }

        m_stream.Position = m_payloadOffset;

        for (int i = 0; i < m_fixup.InstanceOffsets.Count; i++)
        {
            EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(typeRefs[i]);

            m_stream.Pad(typeDescriptor.Alignment);

            Guid instanceGuid = i < m_fixup.ExportedInstanceCount ? m_stream.ReadGuid(): Guid.Empty;

            Debug.Assert(m_stream.Position - m_payloadOffset == m_fixup.InstanceOffsets[i]);

            object? obj = m_objects[i];
            ((dynamic?)obj)?.SetInstanceGuid(new AssetClassGuid(instanceGuid, i));
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
        PropertyInfo[] properties = objType.GetProperties();

        for (int i = 0; i < inTypeDescriptor.FieldCount; i++)
        {
            EbxFieldDescriptor fieldDescriptor = m_typeResolver.ResolveField(inTypeDescriptor.FieldIndex + i);
            PropertyInfo? propertyInfo = properties.FirstOrDefault(prop =>
                prop.GetCustomAttribute<NameHashAttribute>()?.Hash == fieldDescriptor.NameHash);

            m_stream.Position = inStartOffset + fieldDescriptor.DataOffset;

            TypeFlags.TypeEnum type = fieldDescriptor.Flags.GetTypeEnum();

            if (fieldDescriptor.Flags.GetCategoryEnum() == TypeFlags.CategoryEnum.Array)
            {
                if (propertyInfo is null)
                {
                    FrostyLogger.Logger?.LogDebug("Skipping field \"{}.{}\", because it does not exist in the type info", objType.GetName(), fieldDescriptor.NameHash);
                    continue;
                }
                ReadArray(fieldDescriptor, value =>
                {
                    if (typeof(IDelegate).IsAssignableFrom(propertyInfo.PropertyType.GenericTypeArguments[0]))
                    {
                        IDelegate @delegate = (IDelegate)Activator.CreateInstance(propertyInfo.PropertyType.GenericTypeArguments[0])!;
                        @delegate.FunctionType = (IType?)value;
                        value = @delegate;
                    }
                    else if (value is null)
                    {
                        return;
                    }

                    if (typeof(IPrimitive).IsAssignableFrom(propertyInfo.PropertyType.GenericTypeArguments[0]))
                    {
                        IPrimitive primitive = (IPrimitive)Activator.CreateInstance(propertyInfo.PropertyType.GenericTypeArguments[0])!;
                        primitive.FromActualType(value);
                        value = primitive;
                    }

                    IList? list = (IList?)propertyInfo.GetValue(obj);
                    list?.Add(value);
                });
            }
            else
            {
                switch (type)
                {
                    case TypeFlags.TypeEnum.Void:
                        ReadType(m_typeResolver.ResolveTypeFromField(fieldDescriptor.TypeDescriptorRef), obj, inStartOffset);
                        break;
                    default:
                        if (propertyInfo is null)
                        {
                            FrostyLogger.Logger?.LogDebug("Skipping field \"{}.{}\", because it does not exist in the type info", objType.GetName(), fieldDescriptor.NameHash);
                            continue;
                        }
                        ReadField(fieldDescriptor, value =>
                        {
                            if (typeof(IDelegate).IsAssignableFrom(propertyInfo.PropertyType))
                            {
                                IDelegate @delegate = (IDelegate)Activator.CreateInstance(propertyInfo.PropertyType)!;
                                @delegate.FunctionType = (IType?)value;
                                value = @delegate;
                            }
                            else if (value is null)
                            {
                                return;
                            }

                            if (typeof(IPrimitive).IsAssignableFrom(propertyInfo.PropertyType))
                            {
                                IPrimitive primitive = (IPrimitive)Activator.CreateInstance(propertyInfo.PropertyType)!;
                                primitive.FromActualType(value);
                                value = primitive;
                            }
                            propertyInfo.SetValue(obj, value);
                        });
                        break;
                }
            }
        }

        // ordering of fields is weird
        m_stream.Position = inStartOffset + inTypeDescriptor.Size;
    }

    private void SharedReadField(TypeFlags.TypeEnum inType, Action<object?> inAddFunc)
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
                FrostyLogger.Logger?.LogError("Not implemented type {} in ebx", inType);
                break;
        }
    }

    private void ReadField(EbxFieldDescriptor inFieldDescriptor, Action<object?> inAddFunc)
    {
        switch (inFieldDescriptor.Flags.GetTypeEnum())
        {
            case TypeFlags.TypeEnum.Struct:
                EbxTypeDescriptor structType = m_typeResolver.ResolveTypeFromField(inFieldDescriptor.TypeDescriptorRef);
                m_stream.Pad(structType.Alignment);
                object? obj = TypeLibrary.CreateObject(structType.NameHash);
                ReadType(structType, obj, m_stream.Position);
                inAddFunc(obj);
                break;
            default:
                SharedReadField(inFieldDescriptor.Flags.GetTypeEnum(), inAddFunc);
                break;
        }
    }

    private void ReadBoxedField(TypeFlags.TypeEnum inType,
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
                SharedReadField(inType, inAddFunc);
                break;
        }
    }

    private void ReadArray(EbxFieldDescriptor inFieldDescriptor,
        Action<object?> inAddFunc)
    {
        int offset = m_stream.ReadInt32();

        long arrayPos = m_stream.Position;

        m_stream.Position += offset - 8;

        int count = m_stream.ReadInt32();

        for (int i = 0; i < count; i++)
        {
            ReadField(inFieldDescriptor, inAddFunc);
        }

        m_stream.Position = arrayPos;
    }

    private void ReadBoxedArray(TypeFlags.TypeEnum inType, int inTypeDescriptorRef,
        Action<object?> inAddFunc)
    {
        int offset = m_stream.ReadInt32();

        long arrayPos = m_stream.Position;

        m_stream.Position += offset - 8;

        int count = m_stream.ReadInt32();

        for (int i = 0; i < count; i++)
        {
            ReadBoxedField(inType, inTypeDescriptorRef, inAddFunc);
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

        object? obj = m_objects[objIndex];
        if (obj is not null)
        {
            m_refCounts[objIndex]++;
            return new PointerRef(obj);
        }

        FrostyLogger.Logger?.LogDebug("Ref to null instance");
        return new PointerRef();
    }

    private TypeRef ReadTypeRef()
    {
        uint packed = m_stream.ReadUInt32();
        m_stream.Position += 4; // 0 (index in packed) or -1 (primitive)

        if (packed == 0)
        {
            return new TypeRef();
        }

        if ((packed & 0x80000000) != 0)
        {
            // primitive type
            return new TypeRef(GetPrimitiveTypeFromEbxField((TypeFlags)(packed & ~0x80000000)));
        }

        int typeRef = (int)(packed >> 2);
        return new TypeRef(m_fixup.TypeGuids[typeRef]);
    }

    private IType? ReadDelegate()
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
            return GetPrimitiveTypeFromEbxField((TypeFlags)(packed & ~0x80000000));
        }

        int typeRef = (int)(packed >> 2);
        return TypeLibrary.GetType(m_fixup.TypeGuids[typeRef]);
    }

    private BoxedValueRef ReadBoxedValueRef()
    {
        TypeRef typeRef = ReadTypeRef();
        long offset = m_stream.ReadInt64();

        if (typeRef.IsNull())
        {
            return new BoxedValueRef();
        }

        m_stream.StepIn(m_stream.Position + offset - 8);

        object? value = null;
        EbxExtra b = m_boxedValues[(uint)(m_stream.Position - m_payloadOffset)];

        if (typeRef.Type!.Name == s_collectionName)
        {
            value = Activator.CreateInstance(typeRef.Type)!;
            ReadBoxedArray(b.Flags.GetTypeEnum(), b.TypeDescriptorRef, obj =>
            {
                if (typeof(IDelegate).IsAssignableFrom(typeRef.Type.GenericTypeArguments[0]))
                {
                    IDelegate @delegate = (IDelegate)Activator.CreateInstance(typeRef.Type.GenericTypeArguments[0])!;
                    @delegate.FunctionType = (IType?)obj;
                    obj = @delegate;
                }
                else if (obj is null)
                {
                    return;
                }

                if (typeof(IPrimitive).IsAssignableFrom(typeRef.Type.GenericTypeArguments[0]))
                {
                    IPrimitive primitive = (IPrimitive)Activator.CreateInstance(typeRef.Type.GenericTypeArguments[0])!;
                    primitive.FromActualType(obj);
                    obj = primitive;
                }

                ((IList?)value)?.Add(obj);
            });
        }
        else
        {
            switch (b.Flags.GetTypeEnum())
            {
                case TypeFlags.TypeEnum.Enum:
                    ReadBoxedField(b.Flags.GetTypeEnum(), b.TypeDescriptorRef, obj =>
                    {
                        value = Enum.Parse(typeRef.Type, obj!.ToString()!);
                    });
                    break;
                default:
                    ReadBoxedField(b.Flags.GetTypeEnum(), b.TypeDescriptorRef, obj =>
                    {
                        if (typeof(IDelegate).IsAssignableFrom(typeRef.Type))
                        {
                            IDelegate @delegate = (IDelegate)Activator.CreateInstance(typeRef.Type)!;
                            @delegate.FunctionType = (IType?)obj;
                            obj = @delegate;
                        }
                        else if (obj is null)
                        {
                            return;
                        }

                        if (typeof(IPrimitive).IsAssignableFrom(typeRef.Type))
                        {
                            IPrimitive primitive = (IPrimitive)Activator.CreateInstance(typeRef.Type)!;
                            primitive.FromActualType(obj);
                            obj = primitive;
                        }

                        value = obj;
                    });
                    break;
            }
        }

        m_stream.StepOut();

        return new BoxedValueRef(value, b.Flags);
    }

    private IType GetPrimitiveTypeFromEbxField(TypeFlags inFlags)
    {
        Type type;
        switch (inFlags.GetTypeEnum())
        {
            case TypeFlags.TypeEnum.Void: type = s_voidType;
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

            default:
                throw new NotImplementedException();
        }

        if (inFlags.GetCategoryEnum() == TypeFlags.CategoryEnum.Array)
        {
            return new SdkType(typeof(ObservableCollection<>).MakeGenericType(type));
        }

        return new SdkType(type);
    }
}