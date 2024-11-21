using System;
using System.Collections;
using System.Collections.Generic;
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
using TypeInfo = Frosty.Sdk.Sdk.TypeInfo;

namespace Frosty.Sdk.IO.PartitionEbx;

public class EbxWriter : BaseEbxWriter
{
    private const long c_headerStringsOffsetPos = 0x4;
    private const long c_headerTypeNamesLenPos = 0x1A;
    private const long c_headerDataLenPos = 0x24;
    private const long c_headerBoxedValuesPos = 0x3C;

    private readonly List<EbxArray> m_arrays = new();
    private readonly List<EbxBoxedValue> m_boxedValues = new();

    private readonly List<EbxTypeDescriptor> m_typeDescriptors = new();
    private readonly List<EbxFieldDescriptor> m_fieldDescriptors = new();
    private readonly HashSet<string> m_typeNames = new();

    private Block<byte>? m_data;
    private readonly List<EbxInstance> m_instances = new();

    private ushort m_uniqueClassCount;
    private ushort m_numExports;

    private readonly EbxTypeResolver m_typeResolver;

    public EbxWriter(DataStream inStream)
        : base(inStream)
    {
        m_typeResolver = new EbxTypeResolver(m_typeDescriptors, m_fieldDescriptors);
    }

    protected override void InternalWriteEbx(Guid inPartitionGuid, int inExportedInstanceCount)
    {
        ProcessData();

        WriteHeader(inPartitionGuid);

        if (ProfilesLibrary.EbxVersion >= 4)
        {
            m_stream.WriteInt32(m_boxedValues.Count);
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

        long arrayPos = m_stream.Position;
        for (int i = 0; i < m_arrays.Count; i++)
        {
            m_stream.WriteUInt32(m_arrays[i].Offset);
            m_stream.WriteInt32(m_arrays[i].Count);
            m_stream.WriteInt32(m_arrays[i].TypeDescriptorRef);
        }

        m_stream.Pad(16);

        long boxedValuesPos = m_stream.Position;
        for (int i = 0; i < m_boxedValues.Count; i++)
        {
            m_stream.WriteUInt32(m_boxedValues[i].Offset);
            m_stream.WriteUInt16(m_boxedValues[i].TypeDescriptorRef);
            m_stream.WriteUInt16(m_boxedValues[i].Type);
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

        if (m_arrayData.Count > 0)
        {
            offset = m_stream.Position;
            for (int i = 0, j = 0; i < m_arrays.Count; i++)
            {
                EbxArray array = m_arrays[i];
                if (array.Count > 0)
                {
                    // make sure the array data is padded correctly for the first item TODO: proper alignment
                    m_stream.Position += sizeof(int);
                    m_stream.Pad(16);
                    m_stream.Position -= sizeof(int);
                    m_stream.WriteInt32(array.Count);

                    array.Offset = (uint)(m_stream.Position - offset);
                    m_stream.StepIn(arrayPos + i * 12);
                    m_stream.WriteUInt32(array.Offset);
                    m_stream.StepOut();

                    m_stream.Write(m_arrayData[j]);
                    m_arrayData[j++].Dispose();
                }
            }
            m_stream.Pad(16);
        }

        uint boxedValueOffset = (uint)(m_stream.Position - m_stringsLength - stringsOffset);
        if (m_boxedValues.Count > 0)
        {
            for (int i = 0; i < m_boxedValues.Count; i++)
            {
                EbxBoxedValue boxedValue = m_boxedValues[i];

                // TODO: proper alignment
                m_stream.Pad(16);

                boxedValue.Offset = (uint)(m_stream.Position - boxedValueOffset - m_stringsLength - stringsOffset);
                m_stream.StepIn(boxedValuesPos + i * 8);
                m_stream.WriteUInt32(boxedValue.Offset);
                m_stream.StepOut();

                m_stream.Write(m_boxedValueData[i]);
                m_boxedValueData[i].Dispose();

                m_boxedValues[i] = boxedValue;
            }
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

        if (ProfilesLibrary.EbxVersion >= 4)
        {
            m_stream.Position = c_headerBoxedValuesPos;
            m_stream.WriteUInt32(boxedValueOffset);
        }

        m_stream.Position = stringsOffset + stringsAndDataLen;
    }

    protected override int CompareObjects(object inA, object inB)
    {
        return string.Compare(inA.GetType().GetName(), inB.GetType().GetName(), StringComparison.Ordinal);
    }

    protected override int AddType(Type inType)
    {
        if (m_useSharedTypeDescriptors)
        {
            m_typeDescriptors.Add(EbxSharedTypeDescriptors.GetKey(inType.GetNameHash()));
            m_typeToDescriptor.Add(inType.GetNameHash(), m_typeDescriptors.Count - 1);

            return m_typeDescriptors.Count - 1;
        }

        int index;
        EbxTypeMetaAttribute? typeMeta = inType.GetCustomAttribute<EbxTypeMetaAttribute>();

        if (inType.IsEnum)
        {
            string[] enumNames = inType.GetEnumNames();
            Array enumValues = inType.GetEnumValues();

            string name = inType.GetName();

            uint nameHash = (uint)Utils.Utils.HashString(name);

            index = AddType(name,
                nameHash,
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
                AddField(enumNames[i], (uint)Utils.Utils.HashString(enumNames[i]), new TypeFlags(TypeEnum.Int32, unk: 0), 0, (uint)enumValue,
                    (uint)enumValue, m_fieldDescriptors.Count - 1);
            }
        }
        else if (inType.Name.Equals(s_collectionName))
        {
            Type elementType = inType.GenericTypeArguments[0].Name == "PointerRef"
                ? s_dataContainerType
                : inType.GenericTypeArguments[0];

            string name = inType.GetName();

            uint nameHash = (uint)Utils.Utils.HashString(name);

            index = AddType(name, nameHash, m_fieldDescriptors.Count, 1, 4,
                elementType.GetCustomAttribute<EbxArrayMetaAttribute>()!.Flags, 4, 0);

            ReserveFields(1);

            ushort arrayClassRef = (ushort)(typeof(IPrimitive).IsAssignableFrom(elementType) ? 0 : FindOrAddType(elementType));

            AddField("member", (uint)Utils.Utils.HashString("member"),
                elementType.GetCustomAttribute<EbxTypeMetaAttribute>()!.Flags,
                arrayClassRef, 0, 0, m_typeDescriptors[index].FieldIndex);
        }
        else if (inType.IsClass)
        {
            bool inherited = false;
            ushort superClassRef = 0;
            if (inType.BaseType!.Namespace!.StartsWith(s_ebxNamespace))
            {
                superClassRef = FindOrAddType(inType.BaseType);
                inherited = true;
            }

            PropertyInfo[] allProps = inType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
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
            string name = inType.GetName();

            uint nameHash = (uint)Utils.Utils.HashString(name);
            index = AddType(name, nameHash, m_fieldDescriptors.Count, (byte)classProperties.Count, 4, typeMeta!.Flags,
                8, 0);

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
        else if (inType.IsValueType)
        {
            PropertyInfo[] allProps = inType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
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

            string name = inType.GetName();

            uint nameHash = (uint)Utils.Utils.HashString(name);

            index = AddType(name, nameHash, m_fieldDescriptors.Count, (byte)objProperties.Count, 1, typeMeta!.Flags, 0,
                0);

            EbxTypeDescriptor typeDesc = m_typeDescriptors[index];

            ReserveFields(typeDesc.GetFieldCount());

            int fieldIndex = 0;
            foreach (PropertyInfo pi in objProperties)
            {
                ProcessField(pi, ref typeDesc, typeDesc.FieldIndex + fieldIndex++);
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
        else
        {
            throw new Exception();
        }

        return index;
    }

    private ushort FindOrAddType(Type inType)
    {
        int index = FindExistingType(inType);
        if (index == -1)
        {
            index = AddType(inType);
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
            case TypeEnum.TypeRef:
            case TypeEnum.FileRef:
                fieldSize = 4;
                alignment = 4;
                break;
            case TypeEnum.Array:
                classRef = FindOrAddType(propType);
                alignment = m_typeDescriptors[classRef].GetAlignment();
                fieldSize = m_typeDescriptors[classRef].Size;
                break;
            case TypeEnum.Struct:
            case TypeEnum.Enum:
                classRef = FindOrAddType(propType);
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
            (uint)Utils.Utils.HashString(objField.Name),
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
        HashSet<Type> uniqueTypes = new(m_objsSorted.Count);

        m_data = new Block<byte>(0);
        using (BlockStream writer = new(m_data, true))
        {
            Type type = m_objsSorted[0].GetType();
            int typeDescriptorRef = FindExistingType(type);

            EbxInstance inst = new()
            {
                TypeDescriptorRef = (ushort)typeDescriptorRef,
                Count = 0,
                IsExported = true
            };

            ushort count = 0;
            m_numExports++;

            for (int i = 0; i < m_objsSorted.Count; i++)
            {
                AssetClassGuid guid = ((dynamic)m_objsSorted[i]).GetInstanceGuid();

                type = m_objsSorted[i].GetType();
                typeDescriptorRef = FindExistingType(type);
                EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(typeDescriptorRef);

                uniqueTypes.Add(type);

                if (typeDescriptorRef != inst.TypeDescriptorRef || inst.IsExported && !guid.IsExported)
                {
                    inst.Count = count;
                    m_instances.Add(inst);

                    inst = new EbxInstance
                    {
                        TypeDescriptorRef = (ushort)typeDescriptorRef,
                        IsExported = guid.IsExported
                    };
                    m_numExports += (ushort)(inst.IsExported ? 1 : 0);

                    count = 0;
                }

                writer.Pad(typeDescriptor.GetAlignment());

                if (guid.IsExported)
                {
                    writer.WriteGuid(guid.ExportedGuid);
                }

                if (typeDescriptor.GetAlignment() != 0x04)
                {
                    writer.WriteUInt64(0);
                }

                WriteType(m_objsSorted[i], typeDescriptor, writer, writer.Position - 8);
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

    private int AddType(string name, uint nameHash, int fieldIndex, byte fieldCount, byte alignment, ushort typeFlags,
        ushort size, ushort secondSize)
    {
        EbxTypeDescriptor typeDesc = new()
        {
            Name = name,
            NameHash = nameHash,
            FieldIndex = fieldIndex,
            Flags = typeFlags,
            Size = size,
            SecondSize = secondSize,
            Index = -1,
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

    private void WriteType(object obj, EbxTypeDescriptor inTypeDescriptor, DataStream writer, long startPos)
    {
        Type objType = obj.GetType();
        PropertyInfo[] properties = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        for (int i = 0; i < inTypeDescriptor.GetFieldCount(); i++)
        {
            EbxFieldDescriptor field = m_typeResolver.ResolveField(inTypeDescriptor.FieldIndex + i);
            PropertyInfo? ebxProperty = properties.FirstOrDefault(prop =>
            {
                if (string.IsNullOrEmpty(field.Name))
                {
                    return prop.GetCustomAttribute<NameHashAttribute>()?.Hash == field.NameHash;
                }

                return prop.GetName() == field.Name;
            });

            writer.Position = startPos + field.DataOffset;

            TypeEnum type = field.Flags.GetTypeEnum();
            switch (type)
            {
                case TypeEnum.Void:
                    // read superclass first
                    WriteType(obj, m_typeResolver.ResolveType(inTypeDescriptor, field.TypeDescriptorRef), writer, startPos);
                    break;
                default:
                    if (ebxProperty is null)
                    {
                        continue;
                    }
                    WriteField(ebxProperty.GetValue(obj)!, inTypeDescriptor, type, field.TypeDescriptorRef, writer);
                    break;
            }
        }

        writer.Position = startPos + inTypeDescriptor.Size;
    }

    private void WriteField(object ebxObj, EbxTypeDescriptor? inParentTypeDescriptor, TypeEnum ebxType, ushort inTypeDescriptorRef, DataStream writer)
    {
        switch (ebxType)
        {
            case TypeEnum.TypeRef:
            {
                writer.WriteUInt32(AddString(TypeInfo.Version > 4 ? ((TypeRef)((IPrimitive)ebxObj).ToActualType()).Guid.ToString().ToUpper() : (TypeRef)((IPrimitive)ebxObj).ToActualType()));
                break;
            }
            case TypeEnum.FileRef:
            {
                writer.WriteUInt32(AddString((FileRef)((IPrimitive)ebxObj).ToActualType()));
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
                EbxTypeDescriptor structType = inParentTypeDescriptor.HasValue ? m_typeResolver.ResolveType(inParentTypeDescriptor.Value, inTypeDescriptorRef) : m_typeResolver.ResolveType(inTypeDescriptorRef);
                writer.Pad(structType.GetAlignment());

                WriteType(ebxObj, structType, writer, writer.Position);
            }
            break;

            case TypeEnum.Array:
            {
                int typeDescriptorRef = FindExistingType(ebxObj.GetType());
                int arrayIdx = 0;

                EbxTypeDescriptor arrayTypeDescriptor = m_typeResolver.ResolveType(typeDescriptorRef);
                EbxFieldDescriptor elementFieldDescriptor = m_typeResolver.ResolveField(arrayTypeDescriptor.FieldIndex);

                ebxType = elementFieldDescriptor.Flags.GetTypeEnum();

                IList arrayObj = (IList)ebxObj;
                int count = arrayObj.Count;

                if (m_arrays.Count == 0)
                {
                    m_arrays.Add(
                        new EbxArray
                        {
                            Count = 0,
                            TypeDescriptorRef = typeDescriptorRef
                        });
                }

                if (count > 0)
                {
                    arrayIdx = m_arrays.Count;
                    m_arrays.Add(
                        new EbxArray
                        {
                            Count = count,
                            TypeDescriptorRef = typeDescriptorRef
                        });
                    Block<byte> data = new(0);
                    m_arrayData.Add(data);
                    using (BlockStream arrayWriter = new(data, true))
                    {
                        for (int i = 0; i < count; i++)
                        {
                            object subValue = arrayObj[i]!;

                            WriteField(subValue, arrayTypeDescriptor, ebxType, elementFieldDescriptor.TypeDescriptorRef, arrayWriter);
                        }
                    }
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

                if (value.Type == TypeEnum.Void)
                {
                    index = -1;
                }
                else
                {
                    EbxBoxedValue boxedValue = new()
                    {
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

                    Block<byte> data = new(0);
                    m_boxedValueData.Add(data);
                    using (BlockStream stream = new(data, true))
                    {
                        WriteField(value.Value!, null, value.Type, boxedValue.TypeDescriptorRef, stream);
                    }

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

    private new uint AddString(string inValue)
    {
        if (string.IsNullOrEmpty(inValue))
        {
            return 0xFFFFFFFF;
        }

        return base.AddString(inValue);
    }
}