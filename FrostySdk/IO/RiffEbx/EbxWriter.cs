using Frosty.Sdk.Attributes;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.Sdk;
using Frosty.Sdk.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Frosty.Sdk.IO.RiffEbx;

public class EbxWriter : BaseEbxWriter
{
    private readonly List<EbxTypeDescriptor> m_typeDescriptors = new();
    private readonly List<EbxFieldDescriptor> m_fieldTypes = new();

    [Flags]
    private enum Flags
    {
        Exported = 1 << 8,
        HasGuid = 1 << 12,
        Aggregated = 1 << 13,
        ReadOnly = 1 << 15
    }

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

        ProcessData();
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

            index = AddClass(objType.GetCustomAttribute<NameHashAttribute>()?.Hash ?? 0,
                m_fieldTypes.Count,
                (byte)enumNames.Length,
                4,
                typeMeta!.Flags,
                4);

            for (int i = 0; i < enumNames.Length; i++)
            {
                ReserveFields(1);
                int enumValue = (int)enumValues.GetValue(i)!;
                AddField((uint)Utils.Utils.HashString(enumNames[i]), 0, 0, (uint)enumValue, m_fieldTypes.Count - 1);
            }
        }
        else if (objType.Name.Equals(s_collectionName))
        {
            Type elementType = objType.GenericTypeArguments[0].Name == "PointerRef" ? s_dataContainerType : objType.GenericTypeArguments[0];

            index = AddClass(elementType.GetCustomAttribute<ArrayHashAttribute>()?.Hash ?? 0,
                m_fieldTypes.Count, 1, 4, elementType.GetCustomAttribute<EbxArrayMetaAttribute>()!.Flags, 4);

            ReserveFields(1);

            ushort arrayClassRef = typeof(IPrimitive).IsAssignableFrom(elementType) ? (ushort)0 : ProcessClass(elementType);

            AddField((uint)Utils.Utils.HashString("member"), elementType.GetCustomAttribute<EbxTypeMetaAttribute>()!.Flags, arrayClassRef, 0, m_typeDescriptors[index].FieldIndex);
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

            index = AddClass(objType.GetCustomAttribute<NameHashAttribute>()?.Hash ?? 0,
                m_fieldTypes.Count,
                (byte)classProperties.Count,
                4,
                typeMeta!.Flags,
                8);

            EbxTypeDescriptor typeDesc = m_typeDescriptors[index];

            ReserveFields(typeDesc.FieldCount);

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

                AddField((uint)Utils.Utils.HashString("$"), 0, superClassRef, firstOffset, typeDesc.FieldIndex);

                typeDesc.FieldCount = (ushort)(typeDesc.FieldCount + 1);
                typeDesc.Size = m_typeDescriptors[superClassRef].Size;
                typeDesc.Alignment = m_typeDescriptors[superClassRef].Alignment;
                fieldIndex++;
            }

            foreach (PropertyInfo pi in classProperties)
            {
                ProcessField(pi, ref typeDesc, typeDesc.FieldIndex + fieldIndex++);
            }

            while (typeDesc.Size % typeDesc.Alignment != 0)
            {
                typeDesc.Size++;
            }

            if (inherited && hasFirstField && typeDesc.FieldCount > 1)
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

            index = AddClass(
                objType.GetCustomAttribute<NameHashAttribute>()?.Hash ?? (uint)Utils.Utils.HashString(name),
                m_fieldTypes.Count,
                (byte)objProperties.Count,
                1,
                typeMeta!.Flags,
                0);

            EbxTypeDescriptor typeDesc = m_typeDescriptors[index];

            ReserveFields(typeDesc.FieldCount);

            int fieldIndex = 0;
            foreach (PropertyInfo fieldProperty in objProperties)
            {
                ProcessField(fieldProperty, ref typeDesc, typeDesc.FieldIndex + fieldIndex++);
            }

            if (typeMeta.Flags.GetFlags().HasFlag(TypeFlags.Flags.LayoutImmutable))
            {
                typeDesc.Size = typeMeta.Size;
                typeDesc.Alignment = typeMeta.Alignment;
            }

            while (typeDesc.Size % typeDesc.Alignment != 0)
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
        TypeFlags.TypeEnum ebxType = fieldMeta!.Flags.GetTypeEnum();

        Type propType = objField.PropertyType;
        EbxTypeMetaAttribute? typeMeta = propType.GetCustomAttribute<EbxTypeMetaAttribute>();

        ushort alignment = 1;
        ushort fieldSize = 0;
        if (typeMeta is not null)
        {
            alignment = typeMeta.Alignment;
            fieldSize = typeMeta.Size;
        }

        switch (ebxType)
        {
            case TypeFlags.TypeEnum.Class:
            case TypeFlags.TypeEnum.CString:
                fieldSize = 4;
                alignment = 4;
                break;
            case TypeFlags.TypeEnum.Array:
                classRef = ProcessClass(propType);
                alignment = m_typeDescriptors[classRef].Alignment;
                fieldSize = m_typeDescriptors[classRef].Size;
                break;
            case TypeFlags.TypeEnum.Struct:
            case TypeFlags.TypeEnum.Enum:
                classRef = ProcessClass(propType);
                alignment = m_typeDescriptors[classRef].Alignment;
                fieldSize = m_typeDescriptors[classRef].Size;
                break;
        }

        while (typeDesc.Size % alignment != 0)
        {
            typeDesc.Size++;
        }

        AddField(objField.GetCustomAttribute<NameHashAttribute>()!.Hash,
            fieldMeta.Flags,
            classRef,
            typeDesc.Size,
            fieldIndex);

        // update size and alignment
        typeDesc.Size += fieldSize;
        typeDesc.Alignment = Math.Max(typeDesc.Alignment, alignment);
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

        Block<byte> data = new(10);
        using (BlockStream writer = new(data, true))
        {
            for (int i = 0; i < m_objsSorted.Count; i++)
            {
                AssetClassGuid guid = ((dynamic)m_objsSorted[i]).GetInstanceGuid();

                Type type = m_objsSorted[i].GetType();
                int classIdx = FindExistingType(type);
                var typeDescriptor = m_typeDescriptors[classIdx];

                uniqueTypes.Add(type);

                writer.Pad(typeDescriptor.Alignment);

                if (guid.IsExported)
                {
                    writer.WriteGuid(guid.ExportedGuid);
                }

                //m_exportOffsets.Add((uint)writer.Position);
                long classStartOffset = writer.Position;

                writer.WriteInt64(classIdx);
                if (typeDescriptor.Alignment != 0x04)
                {
                    writer.WriteUInt64(0);
                }

                writer.WriteUInt32(2); // seems to always be the same (needs more testing)

                Flags flags = Flags.Aggregated | Flags.ReadOnly;
                if (guid.IsExported)
                {
                    flags |= Flags.HasGuid | Flags.Exported;
                }

                writer.WriteUInt32((uint)flags);

                //WriteClass(m_sortedObjs[i], type, classStartOffset, writer);
                //count++;
            }
        }

        //m_uniqueClassCount = (ushort)uniqueTypes.Count;
    }

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

        return m_typeToDescriptor.GetValueOrDefault(hash, -1);
    }

    private void ReserveFields(int count)
    {
        for (int i = 0; i < count; i++)
        {
            m_fieldTypes.Add(new EbxFieldDescriptor());
        }
    }

    private int AddClass(uint nameHash, int fieldIndex, byte fieldCount, byte alignment, ushort typeFlags,
        ushort size)
    {
        m_typeDescriptors.Add(new EbxTypeDescriptor
        {
            NameHash = nameHash,
            FieldIndex = fieldIndex,
            FieldCount = fieldCount,
            Flags = typeFlags,
            Alignment = alignment,
            Size = size
        });

        m_typeToDescriptor.Add(nameHash, m_typeDescriptors.Count - 1);

        return m_typeDescriptors.Count - 1;
    }

    private void AddField(uint nameHash, TypeFlags typeFlags, ushort classRef, uint dataOffset, int index)
    {
        m_fieldTypes[index] = new EbxFieldDescriptor
        {
            NameHash = nameHash,
            Flags = typeFlags,
            TypeDescriptorRef = classRef,
            DataOffset = dataOffset
        };
    }
}