using Frosty.Sdk.Attributes;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.Sdk;
using static Frosty.Sdk.Sdk.TypeFlags;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO.Ebx;
using Frosty.Sdk.IO.PartitionEbx;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO;
public class EbxWriter
{
    public static EbxWriter CreateWriter(DataStream inStream)
    {
        if (ProfilesLibrary.EbxVersion == 6)
        {
            throw new NotImplementedException("RIFF ebx writing");
        }
        return new EbxWriter(inStream, ProfilesLibrary.EbxVersion == 5);
    }

    private const long c_headerStringsOffsetPos = 0x4;
    private const long c_headerTypeNamesLenPos = 0x1A;
    private const long c_headerDataLenPos = 0x24;
    private const long c_headerBoxedValuesPos = 0x38;

    private static readonly Type s_pointerType = typeof(PointerRef);
    private static readonly Type s_valueType = typeof(ValueType);
    private static readonly Type s_objectType = typeof(object);
    private static readonly Type s_dataContainerType = TypeLibrary.GetType("DataContainer")!;
    private static readonly Type? s_boxedValueRefType = TypeLibrary.GetType("BoxedValueRef");

    private static readonly string s_ebxNamespace = "Frostbite";
    private static readonly string s_collectionName = "ObservableCollection`1";

    private uint m_stringsLength = 0;

    private List<string> m_strings = new();
    private List<EbxBoxedValue> m_boxedValues = new();
    private Block<byte>? m_boxedValueData;
    private DataStream? m_boxedValueWriter;

    private HashSet<int> m_typesToProcessSet = new();
    private List<Type> m_typesToProcess = new();
    private HashSet<object> m_processedObjects = new();
    private Dictionary<uint, int> m_typeToDescriptor = new();

    private List<object> m_objs = new();
    private List<object> m_objsSorted = new();

    private List<EbxTypeDescriptor> m_typeDescriptors = new();
    private List<EbxFieldDescriptor> m_fieldTypes = new();
    private List<string> m_typeNames = new();

    private HashSet<EbxImportReference> m_imports = new();
    private Dictionary<EbxImportReference, int> m_importOrderFw = new();
    private Dictionary<int, EbxImportReference> m_importOrderBw = new();

    private Block<byte>? m_data;
    private List<EbxInstance> m_instances = new();
    private List<EbxArray> m_arrays = new();
    private List<Block<byte>> m_arrayData = new();

    private ushort m_uniqueClassCount = 0;
    private ushort m_numExports = 0;

    private bool m_usesSharedTypes;

    protected readonly DataStream m_stream;

    protected EbxWriter(DataStream inStream, bool inIsShared)
    {
        m_stream = inStream;
        m_usesSharedTypes = inIsShared;
    }

    public void WriteAsset(EbxAsset inAsset)
    {
        foreach (object ebxObj in inAsset.Objects)
        {
            ExtractType(ebxObj.GetType(), ebxObj);
            m_objs.Insert(0, ebxObj);
        }

        WriteEbx(inAsset.PartitionGuid);
    }

    private void WriteHeader(Guid inFileGuid)
    {
        if (m_usesSharedTypes)
        {
            EbxSharedTypeDescriptors.Initialize();
            m_fieldTypes.Clear();
            m_typeNames.Clear();
            for (int i = 0; i < m_typeDescriptors.Count; i++)
            {
                EbxTypeDescriptor type = m_typeDescriptors[i];
                m_typeDescriptors[i] = EbxSharedTypeDescriptors.GetKey(type);
            }
        }
        m_stream.WriteInt32((int)(ProfilesLibrary.EbxVersion >= 4 ? EbxVersion.Version4 : EbxVersion.Version2));
        m_stream.WriteInt32(0x00); // stringsOffset
        m_stream.WriteInt32(0x00); // stringsAndDataLen
        m_stream.WriteInt32(m_imports.Count);
        m_stream.WriteUInt16((ushort)m_instances.Count);
        m_stream.WriteUInt16(m_numExports);
        m_stream.WriteUInt16(m_uniqueClassCount);
        m_stream.WriteUInt16((ushort)m_typeDescriptors.Count);
        m_stream.WriteUInt16((ushort)m_fieldTypes.Count);
        m_stream.WriteUInt16(0x00); // typeNamesLen
        m_stream.WriteInt32(0x00); // stringsLen
        m_stream.WriteInt32(m_arrays.Count);
        m_stream.WriteInt32(0x00); // dataLen
        m_stream.WriteGuid(inFileGuid);
    }

    private void WriteEbx(Guid inAssetFileGuid)
    {
        GenerateImportOrder();

        // m_typesToProcess.Reverse();
        foreach (Type objTypes in m_typesToProcess)
        {
            ProcessClass(objTypes);
        }

        ProcessData();

        WriteHeader(inAssetFileGuid);

        if (ProfilesLibrary.EbxVersion >= 4)
        {
            m_stream.WriteUInt32(0xDEADBEEF);
            m_stream.WriteUInt32(0xDEADBEEF);
        }
        else
        {
            m_stream.Pad(16);
        }

        foreach (EbxImportReference importRef in m_imports)
        {
            m_stream.WriteGuid(importRef.FileGuid);
            m_stream.WriteGuid(importRef.ClassGuid);
        }

        m_stream.Pad(16);

        long offset = m_stream.Position;
        foreach (string name in m_typeNames)
        {
            m_stream.WriteNullTerminatedString(name);
        }

        m_stream.Pad(16);

        ushort typeNamesLen = (ushort)(m_stream.Position - offset);

        foreach (EbxFieldDescriptor fieldType in m_fieldTypes)
        {
            m_stream.WriteUInt32(fieldType.NameHash);
            m_stream.WriteUInt16(fieldType.Flags);
            m_stream.WriteUInt16(fieldType.TypeDescriptorRef);
            m_stream.WriteUInt32(fieldType.DataOffset);
            m_stream.WriteUInt32(fieldType.SecondOffset);
        }

        foreach (EbxTypeDescriptor classType in m_typeDescriptors)
        {
            m_stream.WriteUInt32(classType.NameHash);
            m_stream.WriteInt32(classType.FieldIndex);
            m_stream.WriteByte(classType.FieldCount);
            m_stream.WriteByte(classType.Alignment);
            m_stream.WriteUInt16(classType.Flags);
            m_stream.WriteUInt16(classType.Size);
            m_stream.WriteUInt16(classType.SecondSize);
        }

        foreach (EbxInstance instance in m_instances)
        {
            m_stream.WriteUInt16(instance.TypeDescriptorRef);
            m_stream.WriteUInt16(instance.Count);
        }

        m_stream.Pad(16);

        long arraysOffset = m_stream.Position;
        for (int i = 0; i < m_arrays.Count; i++)
        {
            m_stream.WriteInt32(0);
            m_stream.WriteInt32(0);
            m_stream.WriteInt32(0);
        }

        m_stream.Pad(16);

        long boxedValueRefOffset = m_stream.Position;
        for (int i = 0; i < m_boxedValues.Count; i++)
        {
            m_stream.WriteUInt32(0);
            m_stream.WriteUInt16(0);
            m_stream.WriteUInt16(0);
        }

        m_stream.Pad(16);

        uint stringsOffset = (uint)m_stream.Position;
        foreach (string str in m_strings)
        {
            m_stream.WriteNullTerminatedString(str);
        }

        m_stream.Pad(16);

        m_stringsLength = (uint)(m_stream.Position - stringsOffset);

        offset = m_stream.Position;
        m_stream.Write(m_data!);
        m_data!.Dispose();
        m_stream.WriteByte(0);

        m_stream.Pad(16);

        uint dataLen = (uint)(m_stream.Position - offset);

        if (m_arrays.Count > 0)
        {
            offset = m_stream.Position;
            for (int i = 0; i < m_arrays.Count; i++)
            {
                if (m_arrays[i].Count <= 0)
                {
                    continue;
                }

                EbxArray array = m_arrays[i];

                m_stream.WriteUInt32(array.Count);

                array.Offset = (uint)(m_stream.Position - offset);

                m_stream.Write(m_arrayData[i]);
                m_arrayData[i].Dispose();

                m_arrays[i] = array;
            }

            m_stream.Pad(16);
        }

        uint boxedValueOffset = (uint)(m_stream.Position - m_stringsLength - stringsOffset);
        if (m_boxedValueWriter?.Length > 0)
        {
            m_boxedValueWriter.Dispose();
            m_stream.Write(m_boxedValueData!);
            m_boxedValueData!.Dispose();
        }

        m_stream.Pad(16);

        uint stringsAndDataLen = (uint)(m_stream.Position - stringsOffset);

        m_stream.Position = c_headerStringsOffsetPos;
        m_stream.WriteUInt32(stringsOffset);
        m_stream.WriteUInt32(stringsAndDataLen);

        m_stream.Position = c_headerTypeNamesLenPos;
        m_stream.WriteUInt16(typeNamesLen);
        m_stream.WriteUInt32(m_stringsLength);

        m_stream.Position = c_headerDataLenPos;
        m_stream.WriteUInt32(dataLen);

        m_stream.Position = arraysOffset;
        for (int i = 0; i < m_arrays.Count; i++)
        {
            m_stream.WriteUInt32(m_arrays[i].Offset);
            m_stream.WriteUInt32(m_arrays[i].Count);
            m_stream.WriteInt32(m_arrays[i].TypeDescriptorRef);
        }

        if (ProfilesLibrary.EbxVersion >= 4)
        {
            m_stream.Position = c_headerBoxedValuesPos;
            m_stream.WriteInt32(m_boxedValues.Count);
            m_stream.WriteUInt32(boxedValueOffset);

            m_stream.Position = boxedValueRefOffset;
            for (int i = 0; i < m_boxedValues.Count; i++)
            {
                m_stream.WriteUInt32(m_boxedValues[i].Offset);
                m_stream.WriteUInt16(m_boxedValues[i].TypeDescriptorRef);
                m_stream.WriteUInt16(m_boxedValues[i].Type);
            }
        }

        m_stream.Position = stringsOffset + stringsAndDataLen;
    }

    private void GenerateImportOrder()
    {
        int iter = 0;
        foreach (EbxImportReference import in m_imports)
        {
            m_importOrderFw[import] = iter;
            m_importOrderBw[iter] = import;
            iter++;
        }
    }

    #region Writing

    private void ProcessData()
    {
        HashSet<Type> uniqueTypes = new(m_objs.Count);
        List<object> exportedObjs = new(m_objs.Count);
        List<object> otherObjs = new(m_objs.Count);

        for (int i = 0; i < m_objs.Count; i++)
        {
            dynamic obj = m_objs[i];
            AssetClassGuid guid = obj.GetInstanceGuid();
            if (guid.IsExported)
            {
                exportedObjs.Add(obj);
            }
            else
            {
                otherObjs.Add(obj);
            }
        }

        object root = exportedObjs[0];
        exportedObjs.RemoveAt(0);

        exportedObjs.Sort((dynamic a, dynamic b) =>
        {
            AssetClassGuid guidA = a.GetInstanceGuid();
            AssetClassGuid guidB = b.GetInstanceGuid();

            byte[] bA = guidA.ExportedGuid.ToByteArray();
            byte[] bB = guidB.ExportedGuid.ToByteArray();

            uint idA = (uint)(bA[0] << 24 | bA[1] << 16 | bA[2] << 8 | bA[3]);
            uint idB = (uint)(bB[0] << 24 | bB[1] << 16 | bB[2] << 8 | bB[3]);

            return idA.CompareTo(idB);
        });

        otherObjs.Sort((a, b) => string.Compare(a.GetType().Name, b.GetType().Name, StringComparison.Ordinal));

        m_objsSorted.Add(root);
        m_objsSorted.AddRange(exportedObjs);
        m_objsSorted.AddRange(otherObjs);

        m_data = new Block<byte>(10);
        using (BlockStream writer = new(m_data, true))
        {
            Type type = m_objsSorted[0].GetType();
            int classIdx = FindExistingType(type);

            EbxInstance inst = new()
            {
                TypeDescriptorRef = (ushort)classIdx,
                Count = 0,
                IsExported = true
            };

            ushort count = 0;
            m_numExports++;

            for (int i = 0; i < m_objsSorted.Count; i++)
            {
                AssetClassGuid guid = ((dynamic)m_objsSorted[i]).GetInstanceGuid();

                type = m_objsSorted[i].GetType();
                classIdx = FindExistingType(type);
                EbxTypeDescriptor classType = m_typeDescriptors[classIdx];

                uniqueTypes.Add(type);

                if (classIdx != inst.TypeDescriptorRef || inst.IsExported && !guid.IsExported)
                {
                    inst.Count = count;
                    m_instances.Add(inst);

                    inst = new EbxInstance
                    {
                        TypeDescriptorRef = (ushort)classIdx,
                        IsExported = guid.IsExported
                    };
                    m_numExports += (ushort)((inst.IsExported) ? 1 : 0);

                    count = 0;
                }

                writer.Pad(classType.GetAlignment());

                if (guid.IsExported)
                {
                    writer.WriteGuid(guid.ExportedGuid);
                }

                if (classType.GetAlignment() != 0x04)
                {
                    writer.WriteUInt64(0);
                }

                WriteClass(m_objsSorted[i], type, writer, writer.Position - 8);
                count++;
            }

            // Add final instance
            inst.Count = count;
            m_instances.Add(inst);
        }

        m_uniqueClassCount = (ushort)uniqueTypes.Count;
    }

    private void WriteClass(object obj, Type objType, DataStream writer, long startPos)
    {
        bool inherited = false;
        if (objType.BaseType!.Namespace!.StartsWith(s_ebxNamespace))
        {
            WriteClass(obj, objType.BaseType, writer, startPos);
            inherited = true;
        }

        PropertyInfo[] allClassProperties = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        EbxTypeDescriptor classType = m_typeDescriptors[FindExistingType(objType)];

        int iter = 0;
        foreach (PropertyInfo ebxProperty in allClassProperties)
        {
            // ignore transients if not saving to project
            if (ebxProperty.GetCustomAttribute<IsTransientAttribute>() is not null)
            {
                continue;
            }

            EbxFieldMetaAttribute? fieldMeta = ebxProperty.GetCustomAttribute<EbxFieldMetaAttribute>();

            TypeEnum ebxType = fieldMeta!.Flags.GetTypeEnum();
            WriteField(ebxProperty.GetValue(obj)!,
                ebxType,
                writer,
                startPos,
                m_fieldTypes[classType.FieldIndex + (inherited ? 1 : 0) + iter++]);
        }

        writer.Pad(classType.GetAlignment());
    }

    private void WriteField(object ebxObj,
        TypeEnum ebxType,
        DataStream writer,
        long startPos,
        EbxFieldDescriptor? field = null)
    {
        writer.Position = startPos + (field?.DataOffset ?? 0);

        switch (ebxType)
        {
            case TypeEnum.TypeRef:
            {
                writer.WriteUInt64(AddString((TypeRef)((IPrimitive)ebxObj).ToActualType()));
                break;
            }
            case TypeEnum.FileRef:
            {
                writer.WriteUInt64(AddString((FileRef)((IPrimitive)ebxObj).ToActualType()));
                break;
            }
            case TypeEnum.CString:
            {
                writer.WriteUInt32(AddString((string)((IPrimitive)ebxObj).ToActualType()));
                break;
            }
            case TypeEnum.Class:
            {
                PointerRef pointer = (PointerRef)ebxObj;
                uint pointerIndex = 0;

                if (pointer.Type == PointerRefType.External)
                {
                    int importIdx = m_importOrderFw[pointer.External];
                    pointerIndex = (uint)(importIdx | 0x80000000);
                }
                else if (pointer.Type == PointerRefType.Internal)
                {
                    pointerIndex = (uint)(m_objsSorted.IndexOf(pointer.Internal!) + 1);
                }

                writer.WriteUInt32(pointerIndex);
            }
            break;

            case TypeEnum.Struct:
            {
                Type structType = ebxObj.GetType();

                EbxTypeDescriptor structClassType = m_typeDescriptors[FindExistingType(structType)];
                writer.Pad(structClassType.Alignment);

                WriteClass(ebxObj, structType, writer, writer.Position);
            }
            break;

            case TypeEnum.Array:
            {
                int arrayClassIdx = FindExistingType(ebxObj.GetType());
                int arrayIdx = 0;

                EbxTypeDescriptor arrayClassType = m_typeDescriptors[arrayClassIdx];
                EbxFieldDescriptor arrayFieldType = m_fieldTypes[arrayClassType.FieldIndex];

                ebxType = arrayFieldType.Flags.GetTypeEnum();

                Type arrayType = ebxObj.GetType();
                int count = (int)arrayType.GetMethod("get_Count")!.Invoke(ebxObj, null)!;

                if (m_arrays.Count == 0)
                {
                    m_arrays.Add(
                        new EbxArray()
                        {
                            Count = 0,
                            TypeDescriptorRef = arrayClassIdx
                        });
                    m_arrayData.Add(Block<byte>.Empty());
                }

                if (count > 0)
                {
                    Block<byte> arrayStream = new(0);
                    using (BlockStream arrayWriter = new(arrayStream, true))
                    {
                        for (int i = 0; i < count; i++)
                        {
                            object subValue = arrayType.GetMethod("get_Item")!.Invoke(ebxObj, new object[] { i })!;

                            WriteField(subValue, ebxType, arrayWriter, arrayWriter.Position);
                        }
                        arrayWriter.Pad(16);
                    }

                    arrayIdx = m_arrays.Count;
                    m_arrays.Add(
                        new EbxArray
                        {
                            Count = (uint)count,
                            TypeDescriptorRef = arrayClassIdx
                        });

                    m_arrayData.Add(arrayStream);
                }
                writer.WriteInt32(arrayIdx);
            }
            break;

            case TypeEnum.Enum:
                writer.WriteInt32((int)ebxObj);
                break;
            case TypeEnum.Float32:
                writer.WriteSingle((float)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeEnum.Float64:
                writer.WriteDouble((double)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeEnum.Boolean:
                writer.WriteBoolean((bool)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeEnum.Int8:
                writer.WriteSByte((sbyte)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeEnum.UInt8:
                writer.WriteByte((byte)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeEnum.Int16:
                writer.WriteInt16((short)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeEnum.UInt16:
                writer.WriteUInt16((ushort)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeEnum.Int32:
                writer.WriteInt32((int)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeEnum.UInt32:
                writer.WriteUInt32((uint)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeEnum.Int64:
                writer.WriteInt64((long)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeEnum.UInt64:
                writer.WriteUInt64((ulong)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeEnum.Guid:
                writer.WriteGuid((Guid)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeEnum.Sha1:
                writer.WriteSha1((Sha1)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeEnum.String:
                writer.WriteFixedSizedString((string)((IPrimitive)ebxObj).ToActualType(), 32);
                break;
            case TypeEnum.ResourceRef:
                writer.WriteUInt64((ResourceRef)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeEnum.BoxedValueRef:
            {
                BoxedValueRef value = (BoxedValueRef)((IPrimitive)ebxObj).ToActualType();
                int index = m_boxedValues.Count;

                if (value.Type == TypeEnum.Inherited)
                {
                    index = -1;
                }
                else
                {
                    m_boxedValueWriter ??= new BlockStream(m_boxedValueData = new Block<byte>(1), true);

                    m_boxedValueWriter!.WriteInt32(0);
                    EbxBoxedValue boxedValue = new()
                    {
                        Offset = (uint)m_boxedValueWriter.Position,
                        Type = (ushort)value.Type
                    };

                    // we need to get the correct typedesc ref
                    switch (value.Type)
                    {
                        case TypeEnum.Array:
                        case TypeEnum.Enum:
                        case TypeEnum.Struct:
                            boxedValue.TypeDescriptorRef = (ushort)FindExistingType(value.Value!.GetType());
                            break;
                    }

                    WriteField(value.Value!, value.Type, m_boxedValueWriter, m_boxedValueWriter.Position);
                    m_boxedValues.Add(boxedValue);
                }

                writer.WriteInt32(index);
                writer.WriteUInt64(0);
                writer.WriteUInt32(0);
            }
            break;


            default:
            {
                throw new InvalidDataException("Error");
            }
        }
    }

    #endregion

    private int FindExistingType(Type inType)
    {
        uint hash;
        if (inType.Name.Equals(s_collectionName))
        {
            Type elementType = inType.GenericTypeArguments[0].Name == "PointerRef" ? s_dataContainerType : inType.GenericTypeArguments[0];

            hash = elementType.GetCustomAttribute<ArrayHashAttribute>()!.Hash;
        }
        else
        {
            hash = inType.GetCustomAttribute<NameHashAttribute>()!.Hash;
        }

        if (m_typeToDescriptor.TryGetValue(hash, out int index))
        {
            return index;
        }

        return -1;
    }

    private void ReserveFields(int count)
    {
        for (int i = 0; i < count; i++)
        {
            m_fieldTypes.Add(new EbxFieldDescriptor());
        }
    }

    #region Adding
    private int AddClass(string name,
        uint nameHash,
        int fieldIndex,
        byte fieldCount,
        byte alignment,
        ushort typeFlags,
        ushort size,
        ushort secondSize)
    {
        m_typeDescriptors.Add(new EbxTypeDescriptor()
        {
            Name = name,
            NameHash = nameHash,
            FieldIndex = fieldIndex,
            FieldCount = fieldCount,
            Flags = typeFlags,
            Alignment = alignment,
            Size = size,
            SecondSize = secondSize
        });

        AddTypeName(name);
        m_typeToDescriptor.Add(nameHash, m_typeDescriptors.Count - 1);

        return m_typeDescriptors.Count - 1;
    }

    private void AddField(string name, uint nameHash, TypeFlags typeFlags, ushort classRef, uint dataOffset, uint secondOffset, int index)
    {
        m_fieldTypes[index] = new EbxFieldDescriptor()
        {
            Name = name,
            NameHash = nameHash,
            Flags = typeFlags,
            TypeDescriptorRef = classRef,
            DataOffset = dataOffset,
            SecondOffset = secondOffset
        };
        AddTypeName(name);
    }

    private void AddTypeName(string inName)
    {
        if (m_typeNames.Contains(inName))
        {
            return;
        }
        m_typeNames.Add(inName);
    }

    protected uint AddString(string stringToAdd)
    {
        if (stringToAdd == "")
        {
            return 0xFFFFFFFF;
        }

        uint offset = 0;
        if (m_strings.Contains(stringToAdd))
        {
            for (int i = 0; i < m_strings.Count; i++)
            {
                if (m_strings[i] == stringToAdd)
                {
                    break;
                }
                offset += (uint)(m_strings[i].Length + 1);
            }
        }
        else
        {
            offset = m_stringsLength;
            m_strings.Add(stringToAdd);
            m_stringsLength += (uint)(stringToAdd.Length + 1);
        }

        return offset;
    }

    #endregion

    #region Processing
    private ushort ProcessClass(Type objType)
    {
        int index = FindExistingType(objType);
        if (index != -1)
        {
            return (ushort)index;
        }

        EbxTypeMetaAttribute? typeMeta = objType.GetCustomAttribute<EbxTypeMetaAttribute>();

        if (objType.IsEnum)
        {
            string[] enumNames = objType.GetEnumNames();
            Array enumValues = objType.GetEnumValues();

            string name = objType.GetName();

            index = AddClass(name,
                objType.GetCustomAttribute<NameHashAttribute>()?.Hash ?? 0,
                m_fieldTypes.Count,
                (byte)enumNames.Length,
                4,
                typeMeta!.Flags,
                4,
                0);

            for (int i = 0; i < enumNames.Length; i++)
            {
                ReserveFields(1);
                int enumValue = (int)enumValues.GetValue(i)!;
                AddField(enumNames[i], (uint)Utils.Utils.HashString(enumNames[i]), 0, 0, (uint)enumValue, (uint)enumValue, m_fieldTypes.Count - 1);
            }
        }
        else if (objType.Name.Equals(s_collectionName))
        {
            Type elementType = objType.GenericTypeArguments[0].Name == "PointerRef" ? s_dataContainerType : objType.GenericTypeArguments[0];

            string name = elementType.GetName();

            index = AddClass($"{name}-Array", elementType.GetCustomAttribute<ArrayHashAttribute>()?.Hash ?? 0,
                m_fieldTypes.Count, 1, 4, elementType.GetCustomAttribute<EbxArrayMetaAttribute>()!.Flags, 4, 0);

            ReserveFields(1);

            ushort arrayClassRef = typeof(IPrimitive).IsAssignableFrom(elementType) ? (ushort)0 : ProcessClass(elementType);

            AddField("member", (uint)Utils.Utils.HashString("member"), elementType.GetCustomAttribute<EbxTypeMetaAttribute>()!.Flags, arrayClassRef, 0, 0, m_typeDescriptors[index].FieldIndex);
        }
        else if (objType.IsClass)
        {
            bool inherited = false;
            ushort superClassRef = 0;
            if (objType.BaseType!.Namespace!.StartsWith(s_ebxNamespace))
            {
                superClassRef = ProcessClass(objType.BaseType);
                inherited = true;
            }

            PropertyInfo[] allProps = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            List<PropertyInfo> classProperties = new();

            foreach (PropertyInfo pi in allProps)
            {
                // ignore transients if saving to project
                if (pi.GetCustomAttribute<IsTransientAttribute>() is not null)
                {
                    continue;
                }

                classProperties.Add(pi);
            }

            string name = objType.GetName();

            index = AddClass(name,
                objType.GetCustomAttribute<NameHashAttribute>()?.Hash ?? 0,
                m_fieldTypes.Count,
                (byte)classProperties.Count,
                4,
                typeMeta!.Flags,
                8,
                0);

            EbxTypeDescriptor typeDesc = m_typeDescriptors[index];

            ReserveFields(typeDesc.GetFieldCount());

            int fieldIndex = 0;
            bool hasFirstField = true;

            if (inherited)
            {
                ReserveFields(1);

                uint firstOffset = 8;
                if (m_typeDescriptors[superClassRef].Size > 8)
                {
                    hasFirstField = false;
                    firstOffset = m_fieldTypes[m_typeDescriptors[superClassRef].FieldIndex].DataOffset;
                }

                AddField("$", (uint)Utils.Utils.HashString("$"), 0, superClassRef, firstOffset, 0, typeDesc.FieldIndex);

                typeDesc.SetFieldCount((ushort)(typeDesc.GetFieldCount() + 1));
                typeDesc.Size = m_typeDescriptors[superClassRef].Size;
                typeDesc.SetAlignment(m_typeDescriptors[superClassRef].GetAlignment());
                fieldIndex++;
            }

            foreach (PropertyInfo pi in classProperties)
            {
                ProcessField(pi, ref typeDesc, typeDesc.FieldIndex + fieldIndex++);
            }

            while (typeDesc.Size % typeDesc.GetAlignment() != 0)
            {
                typeDesc.Size++;
            }

            if (inherited && hasFirstField && typeDesc.GetFieldCount() > 1)
            {
                EbxFieldDescriptor inheritedField = m_fieldTypes[typeDesc.FieldIndex];
                inheritedField.DataOffset = m_fieldTypes[typeDesc.FieldIndex + 1].DataOffset;
                m_fieldTypes[typeDesc.FieldIndex] = inheritedField;
            }

            m_typeDescriptors[index] = typeDesc;
        }
        else if (objType.IsValueType)
        {
            PropertyInfo[] allProps = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            List<PropertyInfo> objProperties = new();

            foreach (PropertyInfo propertyInfo in allProps)
            {
                // ignore transients
                if (propertyInfo.GetCustomAttribute<IsTransientAttribute>() is not null)
                {
                    continue;
                }

                objProperties.Add(propertyInfo);
            }

            string name = objType.GetName();

            index = AddClass(name,
                objType.GetCustomAttribute<NameHashAttribute>()?.Hash ?? (uint)Utils.Utils.HashString(name),
                m_fieldTypes.Count,
                (byte)objProperties.Count,
                1,
                typeMeta!.Flags,
                0,
                0);

            EbxTypeDescriptor typeDesc = m_typeDescriptors[index];

            ReserveFields(typeDesc.GetFieldCount());

            int fieldIndex = 0;
            foreach (PropertyInfo fieldProperty in objProperties)
            {
                ProcessField(fieldProperty, ref typeDesc, typeDesc.FieldIndex + fieldIndex++);
            }

            if (typeMeta.Flags.GetFlags().HasFlag(Flags.LayoutImmutable))
            {
                typeDesc.Size = typeMeta.Size;
                typeDesc.SetAlignment(typeMeta.Alignment);
            }

            while (typeDesc.Size % typeDesc.GetAlignment() != 0)
            {
                typeDesc.Size++;
            }

            m_typeDescriptors[index] = typeDesc;
        }

        return (ushort)index;
    }

    private void ProcessField(PropertyInfo objField, ref EbxTypeDescriptor typeDesc, int fieldIndex)
    {
        ushort classRef = 0;

        EbxFieldMetaAttribute? fieldMeta = objField.GetCustomAttribute<EbxFieldMetaAttribute>();
        TypeEnum ebxType = fieldMeta!.Flags.GetTypeEnum();

        Type propType = objField.PropertyType;
        EbxTypeMetaAttribute? typeMeta = propType.GetCustomAttribute<EbxTypeMetaAttribute>();

        byte alignment = 1;
        ushort fieldSize = 0;
        if (typeMeta is not null)
        {
            alignment = typeMeta.Alignment;
            fieldSize = typeMeta.Size;
        }

        switch (ebxType)
        {
            case TypeEnum.Class:
            case TypeEnum.CString:
                fieldSize = 4;
                alignment = 4;
                break;
            case TypeEnum.Array:
                classRef = ProcessClass(propType);
                alignment = m_typeDescriptors[classRef].GetAlignment();
                fieldSize = m_typeDescriptors[classRef].Size;
                break;
            case TypeEnum.Struct:
            case TypeEnum.Enum:
                classRef = ProcessClass(propType);
                alignment = m_typeDescriptors[classRef].GetAlignment();
                fieldSize = m_typeDescriptors[classRef].Size;
                break;
        }

        while (typeDesc.Size % alignment != 0)
        {
            typeDesc.Size++;
        }

        // TODO: classes seem to not use the in memory offset from the typeinfo
        // set to 0 and hope that not that many errors occur
        AddField(objField.Name,
            objField.GetCustomAttribute<NameHashAttribute>()!.Hash,
            fieldMeta.Flags,
            classRef,
            typeDesc.Size,
            objField.DeclaringType!.IsClass ? 0 : fieldMeta.Offset,
            fieldIndex);

        // update size and alignment
        typeDesc.Size += fieldSize;
        typeDesc.SetAlignment(Math.Max(typeDesc.GetAlignment(), alignment));
    }

    private void ExtractType(Type type, object obj, bool add = true)
    {
        if (typeof(IPrimitive).IsAssignableFrom(type))
        {
            // ignore primitive types
            return;
        }

        if (add && !m_processedObjects.Add(obj))
        {
            // dont get caught in a infinite loop
            return;
        }

        if (type.BaseType != s_objectType && type.BaseType != s_valueType)
        {
            ExtractType(type.BaseType!, obj, false);
        }

        if (add)
        {
            int hash = type.GetHashCode();
            if (!m_typesToProcessSet.Contains(hash))
            {
                m_typesToProcess.Add(type);
                m_typesToProcessSet.Add(hash);
            }
        }

        PropertyInfo[] ebxObjFields = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        foreach (PropertyInfo ebxField in ebxObjFields)
        {
            if (ebxField.GetCustomAttribute<IsTransientAttribute>() is not null)
            {
                // transient field, do not write
                continue;
            }
            ExtractField(ebxField.PropertyType, ebxField.GetValue(obj)!);
        }
    }

    private void ExtractField(Type type, object obj)
    {
        // pointerRefs
        if (type == s_pointerType)
        {
            PointerRef value = (PointerRef)obj;
            if (value.Type == PointerRefType.Internal)
            {
                ExtractType(value.Internal!.GetType(), value.Internal);
            }
            else if (value.Type == PointerRefType.External)
            {
                m_imports.Add(value.External);
            }
        }

        // structs
        else if (type.BaseType == s_valueType && type.Namespace!.StartsWith(s_ebxNamespace))
        {
            object structObj = obj;
            ExtractType(structObj.GetType(), structObj);
        }

        // arrays (stored as ObservableCollections in the sdk)
        else if (type.Name.Equals(s_collectionName))
        {
            Type arrayType = type;
            int count = (int)arrayType.GetMethod("get_Count")!.Invoke(obj, null)!;

            for (int arrayIter = 0; arrayIter < count; arrayIter++)
            {
                ExtractField(arrayType.GenericTypeArguments[0], arrayType.GetMethod("get_Item")!.Invoke(obj, new object[] { arrayIter })!);
            }
        }

        // boxed value refs
        else if (type == s_boxedValueRefType)
        {
            BoxedValueRef boxedValueRef = (BoxedValueRef)((IPrimitive)obj).ToActualType();

            if (boxedValueRef.Value is not null)
            {
                ExtractType(boxedValueRef.Value!.GetType(), boxedValueRef.Value);
            }
        }
    }

    #endregion
}
