using System;
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

namespace Frosty.Sdk.IO;


public class EbxReader
{
    public static EbxReader CreateReader(DataStream inStream)
    {
        return ProfilesLibrary.EbxVersion == 6 ? new EbxReaderRiff(inStream) : new EbxReader(inStream);
    }

    public Guid FileGuid => m_fileGuid;
    public virtual string RootType => GetType(m_typeResolver.ResolveType(m_instances[0].TypeDescriptorRef)).Name;
    public HashSet<Guid> Dependencies => m_dependencies;
    public EbxImportReference[] Imports => m_imports;
    public bool IsValid => m_isValid;

    private static readonly Type s_stringType = TypeLibrary.GetType("String")!;
    private static readonly Type s_sbyteType = TypeLibrary.GetType("Int8")!;
    private static readonly Type s_byteType = TypeLibrary.GetType("UInt8") ?? TypeLibrary.GetType("Uint8")!;
    private static readonly Type s_boolType = TypeLibrary.GetType("Boolean")!;
    private static readonly Type s_ushortType = TypeLibrary.GetType("UInt16") ?? TypeLibrary.GetType("Uint16")!;
    private static readonly Type s_shortType = TypeLibrary.GetType("Int16")!;
    private static readonly Type s_uintType = TypeLibrary.GetType("UInt32") ?? TypeLibrary.GetType("Uint32")!;
    private static readonly Type s_intType = TypeLibrary.GetType("Int32")!;
    private static readonly Type s_ulongType = TypeLibrary.GetType("UInt64") ?? TypeLibrary.GetType("Uint64")!;
    private static readonly Type s_longType = TypeLibrary.GetType("Int64")!;
    private static readonly Type s_floatType = TypeLibrary.GetType("Float32")!;
    private static readonly Type s_doubleType = TypeLibrary.GetType("Float64")!;
    private static readonly Type s_pointerType = typeof(PointerRef);
    private static readonly Type s_guidType = TypeLibrary.GetType("Guid")!;
    private static readonly Type s_sha1Type = TypeLibrary.GetType("SHA1")!;
    private static readonly Type s_cStringType = TypeLibrary.GetType("CString")!;
    private static readonly Type s_resourceRefType = TypeLibrary.GetType("ResourceRef")!;
    private static readonly Type s_fileRefType = TypeLibrary.GetType("FileRef")!;
    private static readonly Type? s_typeRefType = TypeLibrary.GetType("TypeRef")!;
    private static readonly Type? s_boxedValueRefType = TypeLibrary.GetType("BoxedValueRef")!;

    protected EbxFieldDescriptor[] m_fieldDescriptors;
    protected EbxTypeDescriptor[] m_typeDescriptors;
    protected EbxInstance[] m_instances;
    protected EbxArray[] m_arrays;
    protected EbxBoxedValue[] m_boxedValues;
    protected EbxImportReference[] m_imports;
    protected HashSet<Guid> m_dependencies = new();
    protected List<object> m_objects = new();
    protected List<int> m_refCounts = new();

    protected Guid m_fileGuid;
    protected long m_arraysOffset;
    internal long m_stringsOffset;
    protected long m_boxedValuesOffset;

    internal EbxVersion m_magic;
    protected bool m_isValid;

    private EbxTypeResolver m_typeResolver;

    protected readonly DataStream m_stream;

    protected EbxReader(DataStream inStream)
    {
        m_stream = inStream;
    }

    public virtual void ReadHeader()
    {
        m_magic = (EbxVersion)m_stream.ReadUInt32();
        if (m_magic != EbxVersion.Version2 && m_magic != EbxVersion.Version4)
        {
            throw new InvalidDataException("magic");
        }

        m_stringsOffset = m_stream.ReadUInt32();
        uint stringsAndDataLen = m_stream.ReadUInt32();
        uint importCount = m_stream.ReadUInt32();
        ushort instanceCount = m_stream.ReadUInt16();
        ushort exportedCount = m_stream.ReadUInt16();
        ushort uniqueTypeCount = m_stream.ReadUInt16();
        ushort typeDescriptorCount = m_stream.ReadUInt16();
        ushort fieldDescriptorCount = m_stream.ReadUInt16();
        ushort typeNamesLen = m_stream.ReadUInt16();

        uint stringsLen = m_stream.ReadUInt32();
        uint arrayCount = m_stream.ReadUInt32();
        uint dataLen = m_stream.ReadUInt32();

        m_arraysOffset = m_stringsOffset + stringsLen + dataLen;

        m_fileGuid = m_stream.ReadGuid();

        uint boxedValuesCount = 0;
        if (m_magic == EbxVersion.Version4)
        {
            boxedValuesCount = m_stream.ReadUInt32();
            m_boxedValuesOffset = m_stream.ReadUInt32();
            m_boxedValuesOffset += m_stringsOffset + stringsLen;
        }
        else
        {
            m_stream.Pad(16);
        }

        m_imports = new EbxImportReference[importCount];
        for (int i = 0; i < importCount; i++)
        {
            EbxImportReference import = new()
            {
                FileGuid = m_stream.ReadGuid(),
                ClassGuid = m_stream.ReadGuid()
            };

            m_imports[i] = (import);
            m_dependencies.Add(import.FileGuid);
        }

        Dictionary<int, string> typeNames = new();

        long typeNamesOffset = m_stream.Position;
        while (m_stream.Position - typeNamesOffset < typeNamesLen)
        {
            string typeName = m_stream.ReadNullTerminatedString();
            int hash = Utils.Utils.HashString(typeName);

            typeNames.TryAdd(hash, typeName);
        }

        m_fieldDescriptors = new EbxFieldDescriptor[fieldDescriptorCount];
        for (int i = 0; i < fieldDescriptorCount; i++)
        {
            EbxFieldDescriptor fieldDescriptor = new()
            {
                NameHash = m_stream.ReadUInt32(),
                Flags = m_stream.ReadUInt16(),
                TypeDescriptorRef = m_stream.ReadUInt16(),
                DataOffset = m_stream.ReadUInt32(),
                SecondOffset = m_stream.ReadUInt32(),
            };

            fieldDescriptor.Name = typeNames.TryGetValue((int)fieldDescriptor.NameHash, out string? value)
                ? value
                : string.Empty;

            m_fieldDescriptors[i] = fieldDescriptor;
        }

        m_typeDescriptors = new EbxTypeDescriptor[typeDescriptorCount];
        for (int i = 0; i < typeDescriptorCount; i++)
        {
            EbxTypeDescriptor typeDescriptor = new()
            {
                NameHash = m_stream.ReadUInt32(),
                FieldIndex = m_stream.ReadInt32(),
                FieldCount = m_stream.ReadByte(),
                Alignment = m_stream.ReadByte(),
                Flags = m_stream.ReadUInt16(),
                Size = m_stream.ReadUInt16(),
                SecondSize = m_stream.ReadUInt16()
            };

            typeDescriptor.Name = typeNames.TryGetValue((int)typeDescriptor.NameHash, out string? value)
                ? value
                : string.Empty;

            m_typeDescriptors[i] = typeDescriptor;
        }

        m_typeResolver = new EbxTypeResolver(m_typeDescriptors, m_fieldDescriptors);

        m_instances = new EbxInstance[instanceCount];
        for (int i = 0; i < instanceCount; i++)
        {
            EbxInstance inst = new()
            {
                TypeDescriptorRef = m_stream.ReadUInt16(),
                Count = m_stream.ReadUInt16()
            };

            if (i < exportedCount)
            {
                inst.IsExported = true;
            }

            m_instances[i] = inst;
        }

        m_stream.Pad(16);

        m_arrays = new EbxArray[arrayCount];
        for (int i = 0; i < arrayCount; i++)
        {
            m_arrays[i] = new EbxArray
            {
                Offset = m_stream.ReadUInt32(),
                Count = m_stream.ReadUInt32(),
                TypeDescriptorRef = m_stream.ReadInt32()
            };
        }

        m_stream.Pad(16);

        m_boxedValues = new EbxBoxedValue[boxedValuesCount];
        for (int i = 0; i < boxedValuesCount; i++)
        {
            m_boxedValues[i] = new EbxBoxedValue
            {
                Offset = m_stream.ReadUInt32(),
                TypeDescriptorRef = m_stream.ReadUInt16(),
                Type = m_stream.ReadUInt16()
            };
        }

        m_stream.Position = m_stringsOffset + stringsLen;
    }

    public T ReadAsset<T>() where T : EbxAsset, new()
    {
        ReadHeader();

        T asset = new();
        InternalReadObjects();

        asset.fileGuid = m_fileGuid;
        asset.objects = m_objects;
        asset.refCounts = m_refCounts;
        asset.dependencies = m_dependencies;
        asset.OnLoadComplete();

        return asset;
    }

    public dynamic ReadObject()
    {
        InternalReadObjects();
        return m_objects[0];
    }

    public List<object> ReadObjects()
    {
        InternalReadObjects();
        return m_objects;
    }

    protected virtual void InternalReadObjects()
    {
        foreach (EbxInstance inst in m_instances)
        {
            EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(inst.TypeDescriptorRef);
            for (int i = 0; i < inst.Count; i++)
            {
                m_objects.Add(CreateObject(typeDescriptor));
                m_refCounts.Add(0);
            }
        }

        int typeId = 0;
        int index = 0;

        foreach (EbxInstance inst in m_instances)
        {
            EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(inst.TypeDescriptorRef);
            for (int i = 0; i < inst.Count; i++)
            {
                m_stream.Pad(typeDescriptor.GetAlignment());

                Guid instanceGuid = Guid.Empty;
                if (inst.IsExported)
                {
                    instanceGuid = m_stream.ReadGuid();
                }

                if (typeDescriptor.GetAlignment() != 0x04)
                {
                    m_stream.Position += 8;
                }

                dynamic obj = m_objects[typeId++];
                obj.SetInstanceGuid(new AssetClassGuid(instanceGuid, index++));

                ReadClass(typeDescriptor, obj, m_stream.Position - 8);
            }
        }
    }

    protected virtual void ReadClass(EbxTypeDescriptor classType, object? obj, long startOffset)
    {
        if (obj == null)
        {
            m_stream.Position += classType.Size;
            m_stream.Pad(classType.GetAlignment());
            return;
        }
        Type objType = obj.GetType();

        for (int j = 0; j < classType.GetFieldCount(); j++)
        {
            EbxFieldDescriptor fieldType = m_typeResolver.ResolveField(classType.FieldIndex + j);
            PropertyInfo? fieldProp = GetProperty(objType, fieldType);

            m_stream.Position = startOffset + fieldType.DataOffset;

            if (fieldType.Flags.GetTypeEnum() == TypeFlags.TypeEnum.Inherited)
            {
                // read super class first
                ReadClass(m_typeResolver.ResolveType(classType, fieldType.TypeDescriptorRef), obj, startOffset);
            }
            else
            {
                if (fieldType.Flags.GetTypeEnum() == TypeFlags.TypeEnum.Array)
                {
                    EbxTypeDescriptor arrayType = m_typeResolver.ResolveType(classType, fieldType.TypeDescriptorRef);

                    int index = m_stream.ReadInt32();
                    EbxArray array = m_arrays[index];

                    long arrayPos = m_stream.Position;
                    m_stream.Position = m_arraysOffset + array.Offset;

                    for (int i = 0; i < array.Count; i++)
                    {
                        EbxFieldDescriptor arrayField = m_typeResolver.ResolveField(arrayType.FieldIndex);
                        object value = ReadField(arrayType, arrayField.Flags.GetTypeEnum(), arrayField.TypeDescriptorRef);

                        try
                        {
                            if (typeof(IPrimitive).IsAssignableFrom(fieldProp?.PropertyType.GenericTypeArguments[0]))
                            {
                                IPrimitive primitive = (IPrimitive)Activator.CreateInstance(fieldProp.PropertyType.GenericTypeArguments[0])!;
                                primitive.FromActualType(value);
                                value = primitive;
                            }
                            fieldProp?.GetValue(obj)?.GetType().GetMethod("Add")?.Invoke(fieldProp.GetValue(obj), new[] { value });
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                    }
                    m_stream.Position = arrayPos;
                }
                else
                {
                    object value = ReadField(classType, fieldType.Flags.GetTypeEnum(), fieldType.TypeDescriptorRef);

                    try
                    {
                        if (typeof(IPrimitive).IsAssignableFrom(fieldProp?.PropertyType))
                        {
                            IPrimitive primitive = (IPrimitive)Activator.CreateInstance(fieldProp.PropertyType)!;
                            primitive.FromActualType(value);
                            value = primitive;
                        }
                        fieldProp?.SetValue(obj, value);
                    }
                    catch (Exception e)
                    {
                        // ignored
                    }
                }
            }
        }
        m_stream.
        Pad(classType.GetAlignment());
    }

    protected object ReadField(EbxTypeDescriptor? parentClass, TypeFlags.TypeEnum fieldType, ushort fieldClassRef)
    {
        switch (fieldType)
        {
            case TypeFlags.TypeEnum.Boolean:
                return m_stream.ReadBoolean();
            case TypeFlags.TypeEnum.Int8:
                return (sbyte)m_stream.ReadByte();
            case TypeFlags.TypeEnum.UInt8:
                return m_stream.ReadByte();
            case TypeFlags.TypeEnum.Int16:
                return m_stream.ReadInt16();
            case TypeFlags.TypeEnum.UInt16:
                return m_stream.ReadUInt16();
            case TypeFlags.TypeEnum.Int32:
                return m_stream.ReadInt32();
            case TypeFlags.TypeEnum.UInt32:
                return m_stream.ReadUInt32();
            case TypeFlags.TypeEnum.Int64:
                return m_stream.ReadInt64();
            case TypeFlags.TypeEnum.UInt64:
                return m_stream.ReadUInt64();
            case TypeFlags.TypeEnum.Float32:
                return m_stream.ReadSingle();
            case TypeFlags.TypeEnum.Float64:
                return m_stream.ReadDouble();
            case TypeFlags.TypeEnum.Guid:
                return m_stream.ReadGuid();
            case TypeFlags.TypeEnum.ResourceRef:
                return ReadResourceRef();
            case TypeFlags.TypeEnum.Sha1:
                return m_stream.ReadSha1();
            case TypeFlags.TypeEnum.String:
                return m_stream.ReadFixedSizedString(32);
            case TypeFlags.TypeEnum.CString:
                return ReadCString(m_stream.ReadUInt32());
            case TypeFlags.TypeEnum.FileRef:
                return ReadFileRef();
            case TypeFlags.TypeEnum.Delegate:
            case TypeFlags.TypeEnum.TypeRef:
                return ReadTypeRef();
            case TypeFlags.TypeEnum.BoxedValueRef:
                return ReadBoxedValueRef();
            case TypeFlags.TypeEnum.Struct:
                EbxTypeDescriptor structType = parentClass.HasValue ? m_typeResolver.ResolveType(parentClass.Value, fieldClassRef) : m_typeResolver.ResolveType(fieldClassRef);
                m_stream.Pad(structType.GetAlignment());
                object structObj = CreateObject(structType);
                ReadClass(structType, structObj, m_stream.Position);
                return structObj;
            case TypeFlags.TypeEnum.Enum:
                return m_stream.ReadInt32();
            case TypeFlags.TypeEnum.Class:
                return ReadPointerRef();
            case TypeFlags.TypeEnum.DbObject:
                throw new InvalidDataException("DbObject");
            default:
                throw new InvalidDataException("Unknown");
        }
    }

    protected virtual PropertyInfo? GetProperty(Type objType, EbxFieldDescriptor field)
    {
        return objType.GetProperties().FirstOrDefault((pi) => pi.GetCustomAttribute<NameHashAttribute>()?.Hash == field.NameHash);
    }

    protected virtual object CreateObject(EbxTypeDescriptor typeDescriptor) => TypeLibrary.CreateObject(typeDescriptor.NameHash)!;

    protected virtual Type GetType(EbxTypeDescriptor classType) => TypeLibrary.GetType(classType.NameHash)!;

    protected Type GetTypeFromEbxField(TypeFlags.TypeEnum inFlags, ushort inTypeDescriptorRef)
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
            case TypeFlags.TypeEnum.TypeRef: return s_typeRefType;
            case TypeFlags.TypeEnum.BoxedValueRef: return s_boxedValueRefType;
            case TypeFlags.TypeEnum.Array:
                EbxTypeDescriptor arrayType = m_typeDescriptors[inTypeDescriptorRef];
                EbxFieldDescriptor element = m_fieldDescriptors[arrayType.FieldIndex];
                return typeof(List<>).MakeGenericType(GetTypeFromEbxField(element.Flags.GetTypeEnum(), element.TypeDescriptorRef));
            case TypeFlags.TypeEnum.Enum:
                return GetType(m_typeResolver.ResolveType(inTypeDescriptorRef));

            default:
                throw new NotImplementedException();
        }
    }

    protected virtual string ReadString(uint offset)
    {
        if (offset == 0xFFFFFFFF)
        {
            return string.Empty;
        }

        long pos = m_stream.Position;
        m_stream.Position = m_stringsOffset + offset;

        string retStr = m_stream.ReadNullTerminatedString();
        m_stream.Position = pos;

        return retStr;
    }

    protected CString ReadCString(uint offset) => new(ReadString(offset));

    protected ResourceRef ReadResourceRef() => new(m_stream.ReadUInt64());

    protected FileRef ReadFileRef()
    {
        uint index = m_stream.ReadUInt32();
        m_stream.Position += 4;

        return new FileRef(ReadString(index));
    }

    protected virtual PointerRef ReadPointerRef()
    {
        uint index = m_stream.ReadUInt32();

        if ((index >> 0x1F) == 1)
        {
            EbxImportReference import = m_imports[(int)(index & 0x7FFFFFFF)];

            return new PointerRef(import);
        }

        if (index == 0)
        {
            return new PointerRef();
        }

        m_refCounts[(int)(index - 1)]++;
        return new PointerRef(m_objects[(int)(index - 1)]);
    }

    protected virtual TypeRef ReadTypeRef()
    {
        string str = ReadString(m_stream.ReadUInt32());
        m_stream.Position += 4;

        if (string.IsNullOrEmpty(str))
        {
            return new TypeRef();
        }

        if (Guid.TryParse(str, out Guid guid))
        {
            if (guid != Guid.Empty)
            {
                return new TypeRef(guid);
            }
        }

        return new TypeRef(str);
    }

    protected virtual BoxedValueRef ReadBoxedValueRef()
    {
        int index = m_stream.ReadInt32();
        m_stream.Position += 12;

        if (index == -1)
        {
            return new BoxedValueRef();
        }

        EbxBoxedValue boxedValue = m_boxedValues[index];

        long pos = m_stream.Position;
        m_stream.Position = m_boxedValuesOffset + boxedValue.Offset;

        object value;
        if ((TypeFlags.TypeEnum)boxedValue.Type == TypeFlags.TypeEnum.Array)
        {
            EbxTypeDescriptor arrayType = m_typeResolver.ResolveType(boxedValue.TypeDescriptorRef);
            EbxFieldDescriptor arrayField = m_typeResolver.ResolveField(arrayType.FieldIndex);

            Type elementType = GetTypeFromEbxField(arrayField.Flags.GetTypeEnum(), arrayField.TypeDescriptorRef);
            value = Activator.CreateInstance(typeof(ObservableCollection<>).MakeGenericType(elementType))!;
            index = m_stream.ReadInt32();
            EbxArray array = m_arrays[index];

            long arrayPos = m_stream.Position;
            m_stream.Position = m_arraysOffset + array.Offset;

            for (int i = 0; i < array.Count; i++)
            {
                object subValue = ReadField(arrayType, arrayField.Flags.GetTypeEnum(), arrayField.TypeDescriptorRef);
                if (typeof(IPrimitive).IsAssignableFrom(elementType))
                {
                    IPrimitive primitive = (IPrimitive)Activator.CreateInstance(elementType)!;
                    primitive.FromActualType(value);
                    value = primitive;
                }
                value.GetType().GetMethod("Add")?.Invoke(value, new[] { subValue });
            }

            m_stream.Position = arrayPos;
        }
        else
        {
            value = ReadField(null, (TypeFlags.TypeEnum)boxedValue.Type, boxedValue.TypeDescriptorRef);
            Type fieldType = GetTypeFromEbxField((TypeFlags.TypeEnum)boxedValue.Type, boxedValue.TypeDescriptorRef);
            if (typeof(IPrimitive).IsAssignableFrom(fieldType))
            {
                IPrimitive primitive = (IPrimitive)Activator.CreateInstance(fieldType)!;
                primitive.FromActualType(value);
                value = primitive;
            }
            if ((TypeFlags.TypeEnum)boxedValue.Type == TypeFlags.TypeEnum.Enum)
            {
                object tmpValue = value;
                EbxTypeDescriptor enumClass = m_typeResolver.ResolveType(boxedValue.TypeDescriptorRef);
                value = Enum.Parse(GetType(enumClass), tmpValue.ToString()!);
            }
        }
        m_stream.Position = pos;

        return new BoxedValueRef(value, (TypeFlags.TypeEnum)boxedValue.Type);
    }
}