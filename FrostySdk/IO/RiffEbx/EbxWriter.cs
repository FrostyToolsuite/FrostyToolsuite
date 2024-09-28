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

namespace Frosty.Sdk.IO.RiffEbx;

public class EbxWriter : BaseEbxWriter
{
    private readonly List<EbxExtra> m_arrays = new();
    private readonly List<EbxExtra> m_boxedValues = new();

    private readonly EbxTypeResolver m_typeResolver;

    private EbxFixup m_fixup;

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
        m_fixup = new()
        {
            TypeGuids = new List<Guid>(),
            TypeSignatures = new List<uint>(),
            InstanceOffsets = new List<uint>(),
            PointerOffsets = new List<uint>(),
            ResourceRefOffsets = new List<uint>(),
            Imports = new List<EbxImportReference>(),
            ImportOffsets = new List<uint>(),
            TypeInfoOffsets = new List<uint>()
        };
        m_typeResolver = new EbxTypeResolver(m_fixup.TypeGuids, m_fixup.TypeSignatures);
    }

    protected override void InternalWriteEbx(Guid inPartitionGuid)
    {
        m_fixup.PartitionGuid = inPartitionGuid;
        foreach (Type objTypes in m_typesToProcess)
        {
            ProcessType(objTypes);
        }

        using Block<byte> payload = ProcessData();

        RiffStream stream = new(m_stream);
        stream.WriteHeader("RIFF", "EBX\0");

        // align data of EBXD chunk to 16 bytes
        using Block<byte> data = new(payload.Size + 12);
        data.Clear();
        data.Shift(12);
        payload.CopyTo(data, payload.Size);
        data.ResetShift();

        stream.WriteChunk("EBXD", data);

        m_fixup.Imports = new EbxImportReference[m_imports.Count];
        foreach (EbxImportReference import in m_imports)
        {
            m_fixup.Imports[0] = import;
        }

        using Block<byte> fixup = EbxFixup.WriteFixup(m_fixup);
        stream.WriteChunk("EFIX", fixup);

        using Block<byte> x = new(sizeof(int) * 2 + m_arrays.Count + m_boxedValues.Count *
                            (sizeof(uint) + sizeof(int) + sizeof(uint) + sizeof(ushort) + sizeof(ushort)));

        using (BlockStream subStream = new(x, true))
        {
            subStream.WriteInt32(m_arrays.Count);
            subStream.WriteInt32(m_boxedValues.Count);

            foreach (EbxExtra array in m_arrays)
            {
                subStream.WriteUInt32(array.Offset);
                subStream.WriteInt32(array.Count);
                subStream.WriteUInt32(array.Hash);
                subStream.WriteUInt16(array.Flags);
                subStream.WriteUInt16(array.TypeDescriptorRef);
            }

            foreach (EbxExtra boxedValue in m_boxedValues)
            {
                subStream.WriteUInt32(boxedValue.Offset);
                subStream.WriteInt32(boxedValue.Count);
                subStream.WriteUInt32(boxedValue.Hash);
                subStream.WriteUInt16(boxedValue.Flags);
                subStream.WriteUInt16(boxedValue.TypeDescriptorRef);
            }
        }

        stream.WriteChunk("EBXX", x);

        stream.Fixup();
    }

    private void ProcessType(Type objType, bool inAddType = true)
    {
        if (FindExistingType(objType) != -1)
        {
            return;
        }

        if (objType.IsEnum)
        {
            AddClass(objType);
        }
        else if (objType.Name.Equals(s_collectionName))
        {
            Type elementType = objType.GenericTypeArguments[0].Name == "PointerRef" ? s_dataContainerType : objType.GenericTypeArguments[0];

            AddClass(objType);

            if (!typeof(IPrimitive).IsAssignableFrom(elementType))
            {
                ProcessType(elementType);
            }
        }
        else if (objType.IsClass)
        {
            if (objType.BaseType!.Namespace!.StartsWith(s_ebxNamespace))
            {
                ProcessType(objType.BaseType, false);
            }

            PropertyInfo[] allProps = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (inAddType)
            {
                AddClass(objType);
            }

            foreach (PropertyInfo pi in allProps)
            {
                // ignore transients if saving to project
                if (pi.GetCustomAttribute<IsTransientAttribute>() is not null)
                {
                    continue;
                }

                EbxFieldMetaAttribute? fieldMeta = pi.GetCustomAttribute<EbxFieldMetaAttribute>();
                TypeFlags.TypeEnum ebxType = fieldMeta!.Flags.GetTypeEnum();

                Type propType = pi.PropertyType;

                switch (ebxType)
                {
                    case TypeFlags.TypeEnum.Array:
                    case TypeFlags.TypeEnum.Struct:
                    case TypeFlags.TypeEnum.Enum:
                        ProcessType(propType);
                        break;
                }
            }
        }
        else if (objType.IsValueType)
        {
            PropertyInfo[] allProps = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            AddClass(objType);

            foreach (PropertyInfo pi in allProps)
            {
                // ignore transients
                if (pi.GetCustomAttribute<IsTransientAttribute>() is not null)
                {
                    continue;
                }

                EbxFieldMetaAttribute? fieldMeta = pi.GetCustomAttribute<EbxFieldMetaAttribute>();
                TypeFlags.TypeEnum ebxType = fieldMeta!.Flags.GetTypeEnum();

                Type propType = pi.PropertyType;

                switch (ebxType)
                {
                    case TypeFlags.TypeEnum.Array:
                    case TypeFlags.TypeEnum.Struct:
                    case TypeFlags.TypeEnum.Enum:
                        ProcessType(propType);
                        break;
                }
            }
        }
    }

    private Block<byte> ProcessData()
    {
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

        m_fixup.ExportedInstanceCount = exportedObjs.Count;
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

        Block<byte> data = new(1);
        using (BlockStream writer = new(data, true))
        {
            for (int i = 0; i < m_objsSorted.Count; i++)
            {
                AssetClassGuid guid = ((dynamic)m_objsSorted[i]).GetInstanceGuid();

                Type type = m_objsSorted[i].GetType();
                int classIdx = FindExistingType(type);
                EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(classIdx);

                writer.Pad(typeDescriptor.Alignment);

                if (guid.IsExported)
                {
                    writer.WriteGuid(guid.ExportedGuid);
                }
                m_fixup.InstanceOffsets.Add((uint)writer.Position);
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

                WriteType(m_objsSorted[i], m_typeResolver.ResolveType(FindExistingType(type)), writer, classStartOffset);
            }

            writer.Pad(16);

            m_fixup.ArrayOffset = (uint)writer.Position;
            ReadOnlySpan<byte> emptyArray = stackalloc byte[32];
            writer.Write(emptyArray);
            if (m_arrayData is not null)
            {
                m_arrayWriter?.Dispose();
                writer.Write(m_arrayData);
                m_arrayData.Dispose();
                writer.Pad(16);
            }

            m_fixup.BoxedValueRefOffset = (uint)writer.Position;
            if (m_boxedValueData is not null)
            {
                m_boxedValueWriter?.Dispose();
                m_stream.Write(m_boxedValueData);
                m_boxedValueData.Dispose();
            }

            m_fixup.StringOffset = (uint)writer.Position;
            foreach (string str in m_strings)
            {
                writer.WriteNullTerminatedString(str);
            }

            FixupPointers(writer);
        }

        return data;
    }

    private int AddClass(Type inType, bool inAddSignature = true)
    {
        m_typeToDescriptor.Add(inType.GetNameHash(), m_fixup.TypeGuids.Count);

        m_fixup.TypeGuids.Add(inType.GetGuid());

        if (inAddSignature)
        {
            m_fixup.TypeSignatures.Add(inType.GetSignature());
        }

        return m_fixup.TypeGuids.Count - 1;
    }

    private void WriteType(object obj, EbxTypeDescriptor type, DataStream writer, long startPos)
    {
        Type objType = obj.GetType();
        PropertyInfo[] properties = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        for (int i = 0; i < type.FieldCount; i++)
        {
            EbxFieldDescriptor field = m_typeResolver.ResolveField(type.FieldIndex + i);
            PropertyInfo? ebxProperty = properties.FirstOrDefault(p => p.GetNameHash() == field.NameHash);

            writer.Position = startPos + field.DataOffset;

            if (field.Flags.GetCategoryEnum() == TypeFlags.CategoryEnum.Array)
            {
                if (ebxProperty is null)
                {
                    continue;
                }
                WriteArray(ebxProperty.GetValue(obj)!, writer);
            }
            else
            {
                switch (field.Flags.GetTypeEnum())
                {
                    case TypeFlags.TypeEnum.Inherited:
                        WriteType(obj, m_typeResolver.ResolveType(field.TypeDescriptorRef), writer, startPos);
                        break;
                    default:
                        if (ebxProperty is null)
                        {
                            continue;
                        }
                        WriteField(ebxProperty.GetValue(obj)!, field.Flags.GetTypeEnum(), writer);
                        break;

                }
            }
        }

        writer.Position = startPos + type.Size;
    }

    private void WriteField(object ebxObj,
        TypeFlags.TypeEnum ebxType,
        DataStream writer)
    {
        switch (ebxType)
        {
            case TypeFlags.TypeEnum.TypeRef:
                WriteTypeRef((TypeRef)((IPrimitive)ebxObj).ToActualType(), writer, false);
                break;
            case TypeFlags.TypeEnum.FileRef:
                writer.WriteUInt64(AddString((FileRef)((IPrimitive)ebxObj).ToActualType()));
                break;
            case TypeFlags.TypeEnum.CString:
                writer.WriteUInt64(AddString((string)((IPrimitive)ebxObj).ToActualType()));
                break;
            case TypeFlags.TypeEnum.Class:
            {
                PointerRef pointer = (PointerRef)ebxObj;
                ulong pointerIndex = 0;

                if (pointer.Type == PointerRefType.External)
                {
                    int importIdx = m_importOrderFw[pointer.External];

                    // the import list in the EBX counts both GUIDs of each import separately
                    // what we want is the index to the class GUID
                    pointerIndex = (ulong)(importIdx * 2 + 1);
                }
                else if (pointer.Type == PointerRefType.Internal)
                {
                    pointerIndex = (ulong)m_objsSorted.IndexOf(pointer.Internal!);
                }

                writer.WriteUInt64(pointerIndex);
                break;
            }
            case TypeFlags.TypeEnum.Struct:
            {
                EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(FindExistingType(ebxObj.GetType()));
                writer.Pad(typeDescriptor.Alignment);

                WriteType(ebxObj, typeDescriptor, writer, writer.Position);
                break;
            }
            case TypeFlags.TypeEnum.Enum:
                writer.WriteInt32((int)ebxObj);
                break;
            case TypeFlags.TypeEnum.Float32:
                writer.WriteSingle((float)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeFlags.TypeEnum.Float64:
                writer.WriteDouble((double)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeFlags.TypeEnum.Boolean:
                writer.WriteBoolean((bool)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeFlags.TypeEnum.Int8:
                writer.WriteSByte((sbyte)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeFlags.TypeEnum.UInt8:
                writer.WriteByte((byte)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeFlags.TypeEnum.Int16:
                writer.WriteInt16((short)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeFlags.TypeEnum.UInt16:
                writer.WriteUInt16((ushort)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeFlags.TypeEnum.Int32:
                writer.WriteInt32((int)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeFlags.TypeEnum.UInt32:
                writer.WriteUInt32((uint)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeFlags.TypeEnum.Int64:
                writer.WriteInt64((long)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeFlags.TypeEnum.UInt64:
                writer.WriteUInt64((ulong)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeFlags.TypeEnum.Guid:
                writer.WriteGuid((Guid)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeFlags.TypeEnum.Sha1:
                writer.WriteSha1((Sha1)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeFlags.TypeEnum.String:
                writer.WriteFixedSizedString((string)((IPrimitive)ebxObj).ToActualType(), 32);
                break;
            case TypeFlags.TypeEnum.ResourceRef:
                writer.WriteUInt64((ResourceRef)((IPrimitive)ebxObj).ToActualType());
                break;
            case TypeFlags.TypeEnum.BoxedValueRef:
                WriteBoxedValueRef((BoxedValueRef)((IPrimitive)ebxObj).ToActualType(), writer);
                break;
            case TypeFlags.TypeEnum.Array:
                throw new InvalidDataException("Array");
            case TypeFlags.TypeEnum.DbObject:
                throw new InvalidDataException("DbObject");
            default:
                throw new InvalidDataException("Unknown");
        }
    }

    private void WriteBoxedValueRef(BoxedValueRef inBoxedValueRef, DataStream inWriter)
    {
        Type? type = inBoxedValueRef.Value?.GetType();
        TypeRef typeRef = type is null ? new TypeRef() : new TypeRef(type);
        (ushort, ushort) tiPair = WriteTypeRef(typeRef, inWriter, true);


        int index = m_boxedValues.Count;
        if (inBoxedValueRef.Value is not null)
        {
            EbxExtra boxedValue = new()
            {
                Count = 1,
                Offset = (uint)(m_boxedValueWriter?.Position ?? 0),
                Flags = tiPair.Item1,
                TypeDescriptorRef = tiPair.Item2
            };
            m_boxedValues.Add(boxedValue);
            m_boxedValueWriter ??= new BlockStream(m_boxedValueData = new Block<byte>(1), true);
            WriteField(inBoxedValueRef.Value, inBoxedValueRef.Type, m_boxedValueWriter);
        }
        else
        {
            index = 0;
        }

        inWriter.WriteInt64(index);
    }

    private (ushort, ushort) WriteTypeRef(TypeRef typeRef, DataStream writer, bool inAddSignature)
    {
        if (typeRef.IsNull() || typeRef.Name == "0" || typeRef.Name == "Inherited")
        {
            writer.WriteInt32(0x00);
            writer.WriteInt32(0x00);
            return (0, 0);
        }

        Type typeRefType = typeRef.Type!;
        int typeIdx = FindExistingType(typeRefType);
        EbxTypeMetaAttribute? meta = typeRefType.GetCustomAttribute<EbxTypeMetaAttribute>();

        TypeFlags type = meta?.Flags ?? new TypeFlags();

        (ushort, ushort) tiPair;
        tiPair.Item1 = type;

        uint typeFlags = type;
        if (typeRefType.IsAssignableTo(typeof(IPrimitive)))
        {
            typeFlags |= 0x80000000;
            tiPair.Item2 = (ushort)typeIdx;
        }
        else
        {
            if (typeIdx == -1)
            {
                // boxed value type refs shouldn't end up here, as they're already handled when processing classes
                typeIdx = AddClass(typeRefType, inAddSignature);
            }
            typeFlags = (uint)(typeIdx << 2);
            typeFlags |= 2;
            // boxed value info in the EBXX section needs the class index
            // the type ref just sets it to zero
            tiPair.Item2 = (ushort)typeIdx;
            typeIdx = 0;
        }
        writer.WriteUInt32(typeFlags);
        writer.WriteInt32(typeIdx);
        return tiPair;
    }

    private void WriteArray(object inObj, DataStream writer)
    {
        Type type = inObj.GetType();
        int typeIndex = FindExistingType(type.GenericTypeArguments[0].Name == "PointerRef" ? s_dataContainerType : type.GenericTypeArguments[0]);
        int arrayIdx = 0;

        EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(typeIndex);

        TypeFlags.TypeEnum ebxType = typeDescriptor.Flags.GetTypeEnum();

        // cast to IList to avoid having to invoke methods manually
        IList arrayObj = (IList)inObj;
        int count = arrayObj.Count;

        if (count > 0)
        {
            m_arrayWriter ??= new BlockStream(m_arrayData = new Block<byte>(1), true);
            m_arrayWriter.WriteInt32(count);

            arrayIdx = m_arrays.Count;
            m_arrays.Add(
                new EbxExtra
                {
                    Count = count,
                    TypeDescriptorRef = (ushort)typeIndex,
                    Flags = typeDescriptor.Flags,
                    Offset = (uint)m_arrayWriter.Position + 32
                });

            for (int i = 0; i < count; i++)
            {
                object subValue = arrayObj[i]!;

                WriteField(subValue, ebxType, m_arrayWriter);
            }
            m_arrayWriter.Pad(16);
        }
        writer.WriteInt64(arrayIdx);
    }

    private void FixupPointers(DataStream writer)
    {
        for (int i = 0; i < m_objsSorted.Count; i++)
        {
            Type type = m_objsSorted[i].GetType();
            int classIdx = FindExistingType(type);
            EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(classIdx);

            writer.Position = m_fixup.InstanceOffsets[i];

            writer.Position += sizeof(long);
            if (typeDescriptor.Alignment != 0x04)
            {
                writer.Position += sizeof(long);
            }

            writer.Position += sizeof(int);
            writer.Position += sizeof(int);

            FixupType(m_objsSorted[i], typeDescriptor, writer, m_fixup.InstanceOffsets[i]);
        }
    }

    private void FixupType(object obj, EbxTypeDescriptor type, DataStream writer, long startPos)
    {
        // sweep through the EBX data after writing it to patch pointers/offsets
        Type objType = obj.GetType();
        PropertyInfo[] properties = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        for (int i = 0; i < type.FieldCount; i++)
        {
            EbxFieldDescriptor field = m_typeResolver.ResolveField(type.FieldIndex + i);
            PropertyInfo? ebxProperty = properties.FirstOrDefault(p => p.GetNameHash() == field.NameHash);

            writer.Position = startPos + field.DataOffset;

            if (field.Flags.GetCategoryEnum() == TypeFlags.CategoryEnum.Array)
            {
                if (ebxProperty is null)
                {
                    continue;
                }
                FixupArray(ebxProperty.GetValue(obj)!, writer);
            }
            else
            {
                switch (field.Flags.GetTypeEnum())
                {
                    case TypeFlags.TypeEnum.Inherited:
                        FixupType(obj, m_typeResolver.ResolveType(field.TypeDescriptorRef), writer, startPos);
                        break;
                    default:
                        if (ebxProperty is null)
                        {
                            continue;
                        }
                        FixupField(ebxProperty.GetValue(obj)!, field.Flags.GetTypeEnum(), writer);
                        break;

                }
            }
        }

        writer.Position = startPos + type.Size;
    }

    private void FixupField(object obj, TypeFlags.TypeEnum ebxType, DataStream writer)
    {
        switch (ebxType)
        {
            case TypeFlags.TypeEnum.TypeRef:
                FixupTypeRef(writer);
                break;
            case TypeFlags.TypeEnum.FileRef:
            case TypeFlags.TypeEnum.CString:
                FixupPointer(m_fixup.StringOffset, writer);
                break;
            case TypeFlags.TypeEnum.Class:
                {
                    PointerRef pointer = (PointerRef)obj;

                    if (pointer.Type == PointerRefType.External)
                    {
                        m_fixup.ImportOffsets.Add((uint)writer.Position);
                        writer.Position += sizeof(long);
                    }
                    else if (pointer.Type == PointerRefType.Internal)
                    {
                        FixupInternalRef(writer);
                    }
                    else if (pointer.Type == PointerRefType.Null)
                    {
                        writer.Position += sizeof(long);
                    }
                }
                break;
            case TypeFlags.TypeEnum.Struct:
            {
                EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(FindExistingType(obj.GetType()));
                writer.Pad(typeDescriptor.Alignment);

                FixupType(obj, typeDescriptor, writer, writer.Position);
                break;
            }
            case TypeFlags.TypeEnum.ResourceRef:
                FixupResourceRef(writer);
                break;
            case TypeFlags.TypeEnum.BoxedValueRef:
                FixupTypeRef(writer);
                FixupBoxedValue(obj, writer);
                break;
            case TypeFlags.TypeEnum.Enum:
                writer.Position += sizeof(int);
                break;
            case TypeFlags.TypeEnum.Float32:
                writer.Position += sizeof(float);
                break;
            case TypeFlags.TypeEnum.Float64:
                writer.Position += sizeof(double);
                break;
            case TypeFlags.TypeEnum.Boolean:
                writer.Position += sizeof(bool);
                break;
            case TypeFlags.TypeEnum.Int8:
                writer.Position += sizeof(byte);
                break;
            case TypeFlags.TypeEnum.UInt8:
                writer.Position += sizeof(byte);
                break;
            case TypeFlags.TypeEnum.Int16:
                writer.Position += sizeof(short);
                break;
            case TypeFlags.TypeEnum.UInt16:
                writer.Position += sizeof(short);
                break;
            case TypeFlags.TypeEnum.Int32:
                writer.Position += sizeof(int);
                break;
            case TypeFlags.TypeEnum.UInt32:
                writer.Position += sizeof(int);
                break;
            case TypeFlags.TypeEnum.Int64:
                writer.Position += sizeof(long);
                break;
            case TypeFlags.TypeEnum.UInt64:
                writer.Position += sizeof(long);
                break;
            case TypeFlags.TypeEnum.Guid:
                writer.Position += 16;
                break;
            case TypeFlags.TypeEnum.Sha1:
                writer.Position += 20;
                break;
            case TypeFlags.TypeEnum.String:
                writer.Position += 32;
                break;
            case TypeFlags.TypeEnum.Array:
                throw new InvalidDataException("Array");
            case TypeFlags.TypeEnum.DbObject:
                throw new InvalidDataException("DbObject");
            default:
                throw new InvalidDataException("Unknown");
        }
    }

    private void FixupPointer(long sectionOffset, DataStream writer)
    {
        long fieldOffset = writer.Position;
        m_fixup.PointerOffsets.Add((uint)fieldOffset);
        long offset = sectionOffset + writer.ReadInt64();
        writer.Position = fieldOffset;
        offset -= writer.Position;
        // file pointers only use 32 bits, but runtime pointers need 64 bits when being patched
        // so just cast down and zero out the other 32 bits
        writer.WriteInt32((int)offset);
        writer.WriteInt32(0);
    }

    private void FixupInternalRef(DataStream writer)
    {
        long fieldOffset = writer.Position;
        m_fixup.PointerOffsets.Add((uint)fieldOffset);

        int refIdx = (int)writer.ReadInt64();
        writer.Position = fieldOffset;

        long offset = m_fixup.InstanceOffsets[refIdx];
        offset -= writer.Position;
        // file pointers only use 32 bits, but runtime pointers need 64 bits when being patched
        // so just cast down and zero out the other 32 bits
        writer.WriteInt32((int)offset);
        writer.WriteInt32(0);
    }

    private void FixupArray(object obj, DataStream writer)
    {
        // array pointers need to be in the pointer list
        long fieldOffset = writer.Position;
        m_fixup.PointerOffsets.Add((uint)fieldOffset);
        int arrayIdx = (int)writer.ReadInt64();
        writer.Position = fieldOffset;

        IList arrayObj = (IList)obj;
        if (arrayObj.Count == 0)
        {
            // arrays with zero elements always point to an empty value 16 bytes into the array section
            long offset = m_fixup.ArrayOffset + 0x10;
            offset -= writer.Position;
            writer.WriteInt32((int)offset);
            writer.WriteInt32(0);
        }
        else
        {
            EbxExtra array = m_arrays[arrayIdx];
            array.Offset += m_fixup.ArrayOffset;
            long offset = array.Offset;
            offset -= writer.Position;
            // file pointers only use 32 bits, but runtime pointers need 64 bits when being patched
            // so just cast down and zero out the other 32 bits
            writer.WriteInt32((int)offset);
            writer.WriteInt32(0);

            long oldPos = writer.Position;
            writer.Position = array.Offset;
            for (int i = 0; i < array.Count; i++)
            {
                object subValue = arrayObj[i]!;
                TypeFlags.TypeEnum arrayType = array.Flags.GetTypeEnum();
                FixupField(subValue, arrayType, writer);
            }

            writer.Position = oldPos;
        }
    }

    private void FixupTypeRef(DataStream writer)
    {
        long fieldOffset = writer.Position;
        uint type = writer.ReadUInt32();
        int typeRef = writer.ReadInt32();

        if (type == 0 && typeRef == 0)
        {
            return;
        }

        m_fixup.TypeInfoOffsets.Add((uint)fieldOffset);
    }

    private void FixupResourceRef(DataStream writer)
    {
        long fieldOffset = writer.Position;
        ulong resourceRef = writer.ReadUInt64();

        if (resourceRef == 0)
        {
            return;
        }

        m_fixup.ResourceRefOffsets.Add((uint)fieldOffset);
    }

    private void FixupBoxedValue(object obj, DataStream writer)
    {
        long fieldOffset = writer.Position;
        BoxedValueRef value = (BoxedValueRef)((IPrimitive)obj).ToActualType();

        int boxedValIdx = (int)writer.ReadInt64();
        writer.Position = fieldOffset;
        // null boxed values always have an offset of zero
        if (value.Value == null)
        {
            writer.WriteUInt64(0);
            return;
        }

        // boxed value pointers need to be in the pointer list
        m_fixup.PointerOffsets.Add((uint)fieldOffset);
        EbxExtra boxedVal = m_boxedValues[boxedValIdx];

        long offset = boxedVal.Offset;
        offset -= writer.Position;
        // file pointers only use 32 bits, but runtime pointers need 64 bits when being patched
        // so just cast down and zero out the other 32 bits
        writer.WriteUInt32((uint)offset);
        writer.WriteInt32(0);

        long oldPos = writer.Position;
        writer.Position = boxedVal.Offset;

        FixupField(value.Value, boxedVal.Flags.GetTypeEnum(), writer);

        writer.Position = oldPos;
    }
}