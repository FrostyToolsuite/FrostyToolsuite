using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO.Ebx;
using Frosty.Sdk.Sdk;
using Microsoft.Extensions.Logging;

namespace Frosty.Sdk.IO.PartitionEbx;

public class EbxReader : BaseEbxReader
{
    private readonly EbxHeader m_header;
    private readonly EbxTypeResolver m_typeResolver;

    internal EbxReader(DataStream inStream)
        : base(inStream)
    {
        m_header = EbxHeader.ReadHeader(m_stream);
        m_typeResolver = new EbxTypeResolver(m_header.TypeDescriptors, m_header.FieldDescriptors);
    }

    public override Guid GetPartitionGuid() => m_header.PartitionGuid;

    public override string GetRootType()
    {
        EbxTypeDescriptor type = m_typeResolver.ResolveType(m_header.Instances[0].TypeDescriptorRef);
        if (m_header.TypeNameTableLength > 0)
        {
            // we can just use the name of the type if it's not stripped
            return type.Name;
        }

        return GetType(type).GetName();
    }

    public override HashSet<Guid> GetDependencies() => m_header.Dependencies;

    protected override void InternalReadObjects()
    {
        m_stream.Position = m_header.StringsOffset + m_header.StringTableLength;

        foreach (EbxInstance instance in m_header.Instances)
        {
            EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(instance.TypeDescriptorRef);
            for (int i = 0; i < instance.Count; i++)
            {
                m_objects.Add(CreateObject(typeDescriptor));
                m_refCounts.Add(0);
            }
        }

        int objectIndex = 0;
        foreach (EbxInstance instance in m_header.Instances)
        {
            EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(instance.TypeDescriptorRef);
            for (int j = 0; j < instance.Count; j++)
            {
                m_stream.Pad(typeDescriptor.GetAlignment());

                Guid instanceGuid = instance.IsExported ? m_stream.ReadGuid() : Guid.Empty;

                if (typeDescriptor.GetAlignment() != 0x04)
                {
                    m_stream.Position += 8;
                }

                object? obj = m_objects[objectIndex];
                ((dynamic?)obj)?.SetInstanceGuid(new AssetClassGuid(instanceGuid, objectIndex++));
                ReadType(typeDescriptor, obj, m_stream.Position - 8);
            }
        }
    }

    private void ReadType(EbxTypeDescriptor inTypeDescriptor, object? obj, long inStartOffset)
    {
        if (obj is null)
        {
            m_stream.Position += inTypeDescriptor.Size;
            m_stream.Pad(inTypeDescriptor.GetAlignment());
            return;
        }

        Type objType = obj.GetType();
        PropertyInfo[] properties = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        for (int i = 0; i < inTypeDescriptor.GetFieldCount(); i++)
        {
            EbxFieldDescriptor fieldDescriptor = m_typeResolver.ResolveField(inTypeDescriptor.FieldIndex + i);
            PropertyInfo? propertyInfo = properties.FirstOrDefault(prop =>
            {
                if (string.IsNullOrEmpty(fieldDescriptor.Name))
                {
                    return prop.GetCustomAttribute<NameHashAttribute>()?.Hash == fieldDescriptor.NameHash;
                }

                return prop.GetName() == fieldDescriptor.Name;
            });

            m_stream.Position = inStartOffset + fieldDescriptor.DataOffset;

            TypeFlags.TypeEnum type = fieldDescriptor.Flags.GetTypeEnum();
            switch (type)
            {
                case TypeFlags.TypeEnum.Void:
                    // read superclass first
                    ReadType(m_typeResolver.ResolveType(inTypeDescriptor, fieldDescriptor.TypeDescriptorRef), obj, inStartOffset);
                    break;
                case TypeFlags.TypeEnum.Array:
                    if (propertyInfo is null)
                    {
                        FrostyLogger.Logger?.LogDebug("Skipping field \"{}.{}\", because it does not exist in the type info", inTypeDescriptor.Name, fieldDescriptor.Name);
                        continue;
                    }
                    ReadField(inTypeDescriptor, type, fieldDescriptor.TypeDescriptorRef, value =>
                    {
                        if (value is null)
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
                    break;
                default:
                    if (propertyInfo is null)
                    {
                        FrostyLogger.Logger?.LogDebug("Skipping field \"{}.{}\", because it does not exist in the type info", inTypeDescriptor.Name, fieldDescriptor.Name);
                        continue;
                    }
                    ReadField(inTypeDescriptor, type, fieldDescriptor.TypeDescriptorRef, value =>
                    {
                        if (value is null)
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

        m_stream.Position = inStartOffset + inTypeDescriptor.Size;
    }

    private void ReadField(EbxTypeDescriptor? inParentTypeDescriptor, TypeFlags.TypeEnum inType, ushort inTypeDescriptorRef, Action<object?> inAddFunc)
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
                EbxTypeDescriptor structType = inParentTypeDescriptor.HasValue ? m_typeResolver.ResolveType(inParentTypeDescriptor.Value, inTypeDescriptorRef) : m_typeResolver.ResolveType(inTypeDescriptorRef);
                m_stream.Pad(structType.GetAlignment());
                object? obj = CreateObject(structType);
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
                ReadArray(inParentTypeDescriptor, inTypeDescriptorRef, inAddFunc);
                break;
            case TypeFlags.TypeEnum.DbObject:
                throw new InvalidDataException("DbObject");
            default:
                FrostyLogger.Logger?.LogError("Not implemented type {} in ebx", inType);
                break;
        }
    }

    private void ReadArray(EbxTypeDescriptor? inParentTypeDescriptor, ushort inTypeDescriptorRef, Action<object?> inAddFunc)
    {
        EbxTypeDescriptor arrayTypeDescriptor = inParentTypeDescriptor.HasValue ? m_typeResolver.ResolveType(inParentTypeDescriptor.Value, inTypeDescriptorRef) : m_typeResolver.ResolveType(inTypeDescriptorRef);

        int index = m_stream.ReadInt32();
        EbxArray array = m_header.Arrays[index];

        m_stream.StepIn(m_header.ArrayOffset + array.Offset);

        EbxFieldDescriptor elementFieldDescriptor = m_typeResolver.ResolveField(arrayTypeDescriptor.FieldIndex);
        for (int i = 0; i < array.Count; i++)
        {
            ReadField(arrayTypeDescriptor, elementFieldDescriptor.Flags.GetTypeEnum(), elementFieldDescriptor.TypeDescriptorRef, inAddFunc);
        }
        m_stream.StepOut();
    }

    private string ReadString(uint offset)
    {
        if (offset == 0xFFFFFFFF)
        {
            return string.Empty;
        }

        long pos = m_stream.Position;
        m_stream.Position = m_header.StringsOffset + offset;

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
        uint index = m_stream.ReadUInt32();

        if ((index >> 0x1F) == 1)
        {
            EbxImportReference import = m_header.Imports[(int)(index & 0x7FFFFFFF)];

            return new PointerRef(import);
        }

        if (index == 0)
        {
            return new PointerRef();
        }

        if (index - 1 == 1)
        {

        }

        object? obj = m_objects[(int)(index - 1)];
        if (obj is not null)
        {
            m_refCounts[(int)(index - 1)]++;
            return new PointerRef(obj);
        }

        FrostyLogger.Logger?.LogDebug("Ref to null instance");
        return new PointerRef();
    }

    private TypeRef ReadTypeRef()
    {
        string str = ReadString(m_stream.ReadUInt32());

        if (string.IsNullOrEmpty(str))
        {
            return new TypeRef();
        }

        if (Guid.TryParse(str, out Guid guid))
        {
            if (guid == Guid.Empty)
            {
                return new TypeRef();
            }

            return new TypeRef(guid);
        }

        return new TypeRef(str);
    }

    private BoxedValueRef ReadBoxedValueRef()
    {
        int index = m_stream.ReadInt32();
        m_stream.Position += 12;

        if (index == -1)
        {
            return new BoxedValueRef();
        }

        EbxBoxedValue boxedValue = m_header.BoxedValues[index];

        long pos = m_stream.Position;
        m_stream.Position = m_header.BoxedValueOffset + boxedValue.Offset;

        object? value = null;
        TypeFlags.TypeEnum type = (TypeFlags.TypeEnum)boxedValue.Type;
        Type fieldType = GetTypeFromEbxField(type, boxedValue.TypeDescriptorRef);
        switch (type)
        {
            case TypeFlags.TypeEnum.Array:
                value = Activator.CreateInstance(fieldType)!;
                ReadField(null, type, boxedValue.TypeDescriptorRef, obj =>
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
                break;
            case TypeFlags.TypeEnum.Enum:
                ReadField(null, type, boxedValue.TypeDescriptorRef, obj =>
                {
                    value = Enum.Parse(fieldType, obj!.ToString()!);
                });
                break;
            default:
                ReadField(null, type, boxedValue.TypeDescriptorRef, obj =>
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

        m_stream.Position = pos;

        return new BoxedValueRef(value, new TypeFlags((TypeFlags.TypeEnum)boxedValue.Type));
    }

    private Type GetTypeFromEbxField(TypeFlags.TypeEnum inFlags, ushort inTypeDescriptorRef)
    {
        switch (inFlags)
        {
            case TypeFlags.TypeEnum.Struct: return GetType(m_typeResolver.ResolveType(inTypeDescriptorRef));
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
                return typeof(ObservableCollection<>).MakeGenericType(GetTypeFromEbxField(elementFieldDescriptor.Flags.GetTypeEnum(), elementFieldDescriptor.TypeDescriptorRef));
            case TypeFlags.TypeEnum.Enum:
                return GetType(m_typeResolver.ResolveType(inTypeDescriptorRef));

            default:
                throw new NotImplementedException();
        }
    }

    private Type GetType(EbxTypeDescriptor inTypeDescriptor)
    {
        if (string.IsNullOrEmpty(inTypeDescriptor.Name))
        {
            return TypeLibrary.GetType(inTypeDescriptor.NameHash)?.Type ?? throw new Exception();
        }

        return TypeLibrary.GetType(inTypeDescriptor.Name)?.Type ?? throw new Exception();
    }

    private object? CreateObject(EbxTypeDescriptor inTypeDescriptor)
    {
        if (string.IsNullOrEmpty(inTypeDescriptor.Name))
        {
            return TypeLibrary.CreateObject(inTypeDescriptor.NameHash);
        }

        return TypeLibrary.CreateObject(inTypeDescriptor.Name);
    }
}