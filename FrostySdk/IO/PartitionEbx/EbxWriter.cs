using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO.Ebx;
using Frosty.Sdk.Sdk;
using Frosty.Sdk.Utils;
using static Frosty.Sdk.Sdk.TypeFlags;

namespace Frosty.Sdk.IO.PartitionEbx;

public class EbxWriter : BaseEbxWriter
{
    private const long c_headerStringsOffsetPos = 0x4;
    private const long c_headerTypeNamesLenPos = 0x1A;
    private const long c_headerDataLenPos = 0x24;
    private const long c_headerBoxedValuesPos = 0x38;

    private readonly List<EbxArray> m_arrays = new();
    private readonly List<EbxBoxedValue> m_boxedValues = new();

    private readonly List<EbxTypeDescriptor> m_typeDescriptors = new();
    private readonly List<EbxFieldDescriptor> m_fieldDescriptors = new();
    private readonly HashSet<string> m_typeNames = new();

    private Block<byte>? m_data;
    private readonly List<EbxInstance> m_instances = new();

    private ushort m_uniqueClassCount;
    private ushort m_numExports;

    private readonly bool m_usesSharedTypes = EbxSharedTypeDescriptors.Exists();

    public EbxWriter(DataStream inStream)
        : base(inStream)
    {
    }

    protected override void InternalWriteEbx(Guid inPartitionGuid)
    {
        foreach (Type objTypes in m_typesToProcess)
        {
            ProcessClass(objTypes);
        }

        // we need to replace the typeDescriptors, since we use them to write
        if (m_usesSharedTypes)
        {
            EbxSharedTypeDescriptors.Initialize();
            for (int i = 0; i < m_typeDescriptors.Count; i++)
            {
                EbxTypeDescriptor type = m_typeDescriptors[i];
                EbxTypeDescriptor sharedType = EbxSharedTypeDescriptors.GetSharedTypeDescriptor(type.NameHash);

                type.Flags = sharedType.Flags;
                type.SetAlignment(sharedType.GetAlignment());
                type.Size = sharedType.Size;
                type.SecondSize = sharedType.SecondSize;

                Dictionary<uint, EbxFieldDescriptor> sharedFields = new();
                for (int j = 0; j < sharedType.GetFieldCount(); j++)
                {
                    EbxFieldDescriptor sharedField = EbxSharedTypeDescriptors.GetFieldDescriptor(sharedType.FieldIndex + j);
                    sharedFields.Add(sharedField.NameHash, sharedField);
                }

                for (int j = 0; j < type.GetFieldCount(); j++)
                {
                    EbxFieldDescriptor field = m_fieldDescriptors[type.FieldIndex + j];

                    if (!sharedFields.TryGetValue(field.NameHash, out EbxFieldDescriptor sharedField))
                    {
                        // should only be $
                        sharedField = sharedFields[TypeSdkGenerator.HashTypeName(field.Name)];
                    }

                    EbxTypeDescriptor refType = m_typeDescriptors[field.TypeDescriptorRef];
                    EbxTypeDescriptor sharedRefType =
                        EbxSharedTypeDescriptors.GetTypeDescriptor((short)(sharedField.TypeDescriptorRef + sharedType.Index));
                    Debug.Assert(refType.NameHash == sharedRefType.NameHash);

                    field.DataOffset = sharedField.DataOffset;
                    field.Flags = sharedField.Flags;
                    field.SecondOffset = sharedField.SecondOffset;
                    m_fieldDescriptors[type.FieldIndex + j] = field;
                }

                m_typeDescriptors[i] = type;
            }
        }

        ProcessData();

        WriteHeader(inPartitionGuid);

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
            m_stream.WriteGuid(importRef.PartitionGuid);
            m_stream.WriteGuid(importRef.InstanceGuid);
        }

        m_stream.Pad(16);

        long offset = m_stream.Position;
        foreach (string name in m_typeNames)
        {
            m_stream.WriteNullTerminatedString(name);
        }

        m_stream.Pad(16);

        ushort typeNamesLen = (ushort)(m_stream.Position - offset);

        foreach (EbxFieldDescriptor fieldType in m_fieldDescriptors)
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

        if (m_arrayData is not null)
        {
            m_arrayWriter?.Dispose();
            m_stream.Write(m_arrayData);
            m_arrayData.Dispose();
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
                m_fieldDescriptors.Count,
                (byte)enumNames.Length,
                4,
                typeMeta!.Flags,
                4,
                0);

            for (int i = 0; i < enumNames.Length; i++)
            {
                ReserveFields(1);
                int enumValue = (int)enumValues.GetValue(i)!;
                AddField(enumNames[i], (uint)Utils.Utils.HashString(enumNames[i]), 0, 0, (uint)enumValue,
                    (uint)enumValue, m_fieldDescriptors.Count - 1);
            }
        }
        else if (objType.Name.Equals(s_collectionName))
        {
            Type elementType = objType.GenericTypeArguments[0].Name == "PointerRef" ? s_dataContainerType : objType.GenericTypeArguments[0];

            string name = elementType.GetName();

            index = AddClass($"{name}-Array", elementType.GetCustomAttribute<ArrayHashAttribute>()?.Hash ?? 0,
                m_fieldDescriptors.Count, 1, 4, elementType.GetCustomAttribute<EbxArrayMetaAttribute>()!.Flags, 4, 0);

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
                m_fieldDescriptors.Count,
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
                    firstOffset = m_fieldDescriptors[m_typeDescriptors[superClassRef].FieldIndex].DataOffset;
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
                EbxFieldDescriptor inheritedField = m_fieldDescriptors[typeDesc.FieldIndex];
                inheritedField.DataOffset = m_fieldDescriptors[typeDesc.FieldIndex + 1].DataOffset;
                m_fieldDescriptors[typeDesc.FieldIndex] = inheritedField;
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
                m_fieldDescriptors.Count,
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

                WriteType(m_objsSorted[i], type, writer, writer.Position - 8);
                count++;
            }

            // Add final instance
            inst.Count = count;
            m_instances.Add(inst);
        }

        m_uniqueClassCount = (ushort)uniqueTypes.Count;
    }

    private void ReserveFields(int count)
    {
        for (int i = 0; i < count; i++)
        {
            m_fieldDescriptors.Add(new EbxFieldDescriptor());
        }
    }

    private int AddClass(string name, uint nameHash, int fieldIndex, byte fieldCount, byte alignment, ushort typeFlags,
        ushort size, ushort secondSize)
    {
        EbxTypeDescriptor typeDesc = new()
        {
            Name = name,
            NameHash = nameHash,
            FieldIndex = fieldIndex,
            Flags = typeFlags,
            Size = size,
            SecondSize = secondSize
        };

        typeDesc.SetFieldCount(fieldCount);
        typeDesc.SetAlignment(alignment);

        m_typeDescriptors.Add(typeDesc);

        m_typeNames.Add(name);
        m_typeToDescriptor.Add(nameHash, m_typeDescriptors.Count - 1);

        return m_typeDescriptors.Count - 1;
    }

    private void AddField(string name, uint nameHash, TypeFlags typeFlags, ushort classRef, uint dataOffset,
        uint secondOffset, int index)
    {
        m_fieldDescriptors[index] = new EbxFieldDescriptor
        {
            Name = name,
            NameHash = nameHash,
            Flags = typeFlags,
            TypeDescriptorRef = classRef,
            DataOffset = dataOffset,
            SecondOffset = secondOffset
        };
        m_typeNames.Add(name);
    }

    private void WriteHeader(Guid inPartitionGuid)
    {
        if (m_usesSharedTypes)
        {
            m_fieldDescriptors.Clear();
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
        m_stream.WriteUInt16((ushort)m_fieldDescriptors.Count);
        m_stream.WriteUInt16(0x00); // typeNamesLen
        m_stream.WriteInt32(0x00); // stringsLen
        m_stream.WriteInt32(m_arrays.Count);
        m_stream.WriteInt32(0x00); // dataLen
        m_stream.WriteGuid(inPartitionGuid);
    }

    private void WriteType(object obj, Type objType, DataStream writer, long startPos)
    {
        bool inherited = false;
        if (objType.BaseType!.Namespace!.StartsWith(s_ebxNamespace))
        {
            WriteType(obj, objType.BaseType, writer, startPos);
            inherited = true;
        }

        PropertyInfo[] properties = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        EbxTypeDescriptor type = m_typeDescriptors[FindExistingType(objType)];

        for (int i = inherited ? 1 : 0; i < type.GetFieldCount(); i++)
        {
            EbxFieldDescriptor field = m_fieldDescriptors[type.FieldIndex + i];
            PropertyInfo? ebxProperty = properties.FirstOrDefault(prop =>
            {
                if (string.IsNullOrEmpty(field.Name))
                {
                    return prop.GetCustomAttribute<NameHashAttribute>()?.Hash == field.NameHash;
                }

                return prop.GetName() == field.Name;
            });

            if (ebxProperty is null)
            {
                continue;
            }

            writer.Position = startPos + field.DataOffset;

            WriteField(ebxProperty.GetValue(obj)!, field.Flags.GetTypeEnum(), writer);
        }

        writer.Position = startPos + type.Size;
    }

    private void WriteField(object ebxObj,
        TypeEnum ebxType,
        DataStream writer)
    {
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
                writer.Pad(structClassType.GetAlignment());

                WriteType(ebxObj, structType, writer, writer.Position);
            }
            break;

            case TypeEnum.Array:
            {
                int arrayClassIdx = FindExistingType(ebxObj.GetType());
                int arrayIdx = 0;

                EbxTypeDescriptor arrayClassType = m_typeDescriptors[arrayClassIdx];
                EbxFieldDescriptor arrayFieldType = m_fieldDescriptors[arrayClassType.FieldIndex];

                ebxType = arrayFieldType.Flags.GetTypeEnum();

                IList arrayObj = (IList)ebxObj;
                int count = arrayObj.Count;

                if (m_arrays.Count == 0)
                {
                    m_arrays.Add(
                        new EbxArray()
                        {
                            Count = 0,
                            TypeDescriptorRef = arrayClassIdx
                        });
                }

                if (count > 0)
                {
                    m_arrayWriter ??= new BlockStream(m_arrayData = new Block<byte>(1), true);

                    // make sure the array data is padded correctly for the first item
                    byte alignment = m_typeDescriptors[arrayFieldType.TypeDescriptorRef].GetAlignment();
                    if ((m_arrayWriter.Position + 4) % alignment != 0)
                    {
                        m_arrayWriter.Position += alignment - (m_arrayWriter.Position + 4) % alignment;
                    }

                    m_arrayWriter.WriteInt32(count);

                    arrayIdx = m_arrays.Count;
                    m_arrays.Add(
                        new EbxArray
                        {
                            Count = (uint)count,
                            TypeDescriptorRef = arrayClassIdx,
                            Offset = (uint)m_arrayWriter.Position
                        });

                    for (int i = 0; i < count; i++)
                    {
                        object subValue = arrayObj[i]!;

                        WriteField(subValue, ebxType, m_arrayWriter);
                    }
                    m_arrayWriter.Pad(16);
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

                    WriteField(value.Value!, value.Type, m_boxedValueWriter);
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
}