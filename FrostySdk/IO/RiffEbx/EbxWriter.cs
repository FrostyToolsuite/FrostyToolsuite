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

    protected override void InternalWriteEbx(Guid inPartitionGuid, int inExportedInstanceCount)
    {
        m_fixup.PartitionGuid = inPartitionGuid;
        m_fixup.ExportedInstanceCount = inExportedInstanceCount;

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
        int i = 0;
        foreach (EbxImportReference import in m_imports)
        {
            m_fixup.Imports[i++] = import;
        }

        (m_fixup.InstanceOffsets as List<uint>)!.Sort();
        (m_fixup.PointerOffsets as List<uint>)!.Sort();
        (m_fixup.ResourceRefOffsets as List<uint>)!.Sort();
        (m_fixup.ImportOffsets as List<uint>)!.Sort();
        (m_fixup.TypeInfoOffsets as List<uint>)!.Sort();

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

    protected override int CompareObjects(object inA, object inB)
    {
        byte[] bA = inA.GetType().GetGuid().ToByteArray();
        byte[] bB = inB.GetType().GetGuid().ToByteArray();

        uint idA = (uint)(bA[0] << 24 | bA[1] << 16 | bA[2] << 8 | bA[3]);
        uint idB = (uint)(bB[0] << 24 | bB[1] << 16 | bB[2] << 8 | bB[3]);

        return idA.CompareTo(idB);
    }

    protected override int AddType(Type inType)
    {
        if (inType.Name.Equals(s_collectionName))
        {
            return -1;
        }
        return AddType(inType, true);
    }

    private Block<byte> ProcessData()
    {
        Block<byte> data = new(1);
        using (BlockStream writer = new(data, true))
        {
            for (int i = 0; i < m_objsSorted.Count; i++)
            {
                AssetClassGuid guid = ((dynamic)m_objsSorted[i]).GetInstanceGuid();

                Type type = m_objsSorted[i].GetType();
                int typeDescriptorRef = FindExistingType(type);
                EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(typeDescriptorRef);

                writer.Pad(typeDescriptor.Alignment);

                if (guid.IsExported)
                {
                    writer.WriteGuid(guid.ExportedGuid);
                }
                m_fixup.InstanceOffsets.Add((uint)writer.Position);
                long classStartOffset = writer.Position;

                writer.WriteInt64(typeDescriptorRef);
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

                WriteType(m_objsSorted[i], m_typeResolver.ResolveType(typeDescriptorRef), writer, classStartOffset);
            }

            writer.Pad(16);

            m_fixup.ArrayOffset = (uint)writer.Position;
            ReadOnlySpan<byte> emptyArray = stackalloc byte[32];
            writer.Write(emptyArray);
            if (m_arrayData.Count > 0)
            {
                for (int i = 0; i < m_arrays.Count; i++)
                {
                    EbxExtra array = m_arrays[i];
                    ushort alignment;

                    switch (array.Flags.GetTypeEnum())
                    {
                        case TypeFlags.TypeEnum.Struct:
                            EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(array.TypeDescriptorRef);
                            alignment = typeDescriptor.Alignment;
                            break;
                        case TypeFlags.TypeEnum.Float64:
                        case TypeFlags.TypeEnum.Int64:
                        case TypeFlags.TypeEnum.UInt64:
                        case TypeFlags.TypeEnum.CString:
                        case TypeFlags.TypeEnum.FileRef:
                        case TypeFlags.TypeEnum.Delegate:
                        case TypeFlags.TypeEnum.Class:
                        case TypeFlags.TypeEnum.ResourceRef:
                        case TypeFlags.TypeEnum.BoxedValueRef:
                            alignment = 8; break;

                        default: alignment = 4; break;
                    }

                    // make sure the array data is padded correctly for the first item
                    writer.Position += sizeof(int);
                    writer.Pad(alignment);
                    writer.Position -= sizeof(int);
                    writer.WriteInt32(array.Count);

                    array.Offset = (uint)writer.Position;
                    writer.Write(m_arrayData[i]);
                    m_arrayData[i].Dispose();
                    m_arrays[i] = array;
                }
                writer.Pad(16);
            }

            m_fixup.BoxedValueRefOffset = (uint)writer.Position;
            if (m_boxedValueData.Count > 0)
            {
                for (int i = 0; i < m_boxedValues.Count; i++)
                {
                    EbxExtra boxedValue = m_boxedValues[i];
                    ushort alignment;

                    switch (boxedValue.Flags.GetTypeEnum())
                    {
                        case TypeFlags.TypeEnum.Struct:
                            EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(boxedValue.TypeDescriptorRef);
                            alignment = typeDescriptor.Alignment;
                            break;
                        case TypeFlags.TypeEnum.Float64:
                        case TypeFlags.TypeEnum.Int64:
                        case TypeFlags.TypeEnum.UInt64:
                        case TypeFlags.TypeEnum.CString:
                        case TypeFlags.TypeEnum.FileRef:
                        case TypeFlags.TypeEnum.Delegate:
                        case TypeFlags.TypeEnum.Class:
                        case TypeFlags.TypeEnum.ResourceRef:
                        case TypeFlags.TypeEnum.BoxedValueRef:
                            alignment = 8; break;

                        default: alignment = 4; break;
                    }

                    // make sure the array data is padded correctly for the first item
                    writer.Pad(alignment);

                    boxedValue.Offset = (uint)writer.Position;
                    writer.Write(m_boxedValueData[i]);
                    m_boxedValueData[i].Dispose();
                    m_boxedValues[i] = boxedValue;
                }
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

    private int AddType(Type inType, bool inAddSignature)
    {
        m_typeToDescriptor.Add(inType.GetNameHash(), m_fixup.TypeGuids.Count);

        m_fixup.TypeGuids.Add(inType.GetGuid());

        if (inAddSignature)
        {
            m_fixup.TypeSignatures.Add(inType.GetSignature());
        }

        return m_fixup.TypeGuids.Count - 1;
    }

    private int AddType(IType inType, bool inAddSignature)
    {
        m_typeToDescriptor.Add(inType.NameHash, m_fixup.TypeGuids.Count);

        m_fixup.TypeGuids.Add(inType.Guid);

        if (inAddSignature)
        {
            m_fixup.TypeSignatures.Add(inType.Signature);
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
                WriteArray(ebxProperty.GetValue(obj)!, field, writer);
            }
            else
            {
                switch (field.Flags.GetTypeEnum())
                {
                    case TypeFlags.TypeEnum.Void:
                        WriteType(obj, m_typeResolver.ResolveTypeFromField(field.TypeDescriptorRef), writer, startPos);
                        break;
                    default:
                        if (ebxProperty is null)
                        {
                            continue;
                        }
                        WriteField(ebxProperty.GetValue(obj)!, field, writer);
                        break;

                }
            }
        }

        writer.Position = startPos + type.Size;
    }

    private void SharedWriteField(object ebxObj,
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

    private void WriteField(object ebxObj, EbxFieldDescriptor inFieldDescriptor, DataStream writer)
    {
        switch (inFieldDescriptor.Flags.GetTypeEnum())
        {
            case TypeFlags.TypeEnum.Struct:
                EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveTypeFromField(inFieldDescriptor.TypeDescriptorRef);
                writer.Pad(typeDescriptor.Alignment);

                WriteType(ebxObj, typeDescriptor, writer, writer.Position);
                break;
            default:
                SharedWriteField(ebxObj, inFieldDescriptor.Flags.GetTypeEnum(), writer);
                break;
        }
    }

    private void WriteBoxedField(object ebxObj, TypeFlags.TypeEnum inType, int inTypeDescriptorRef, DataStream writer)
    {
        switch (inType)
        {
            case TypeFlags.TypeEnum.Struct:
                EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(inTypeDescriptorRef);
                writer.Pad(typeDescriptor.Alignment);

                WriteType(ebxObj, typeDescriptor, writer, writer.Position);
                break;
            default:
                SharedWriteField(ebxObj, inType, writer);
                break;
        }
    }

    private void WriteBoxedValueRef(BoxedValueRef inBoxedValueRef, DataStream inWriter)
    {
        Type? type = inBoxedValueRef.Value?.GetType();
        TypeRef typeRef = type is null ? new TypeRef() : new TypeRef(new SdkType(type));
        (TypeFlags Flags, ushort TypeDescriptorRef) tiPair = WriteTypeRef(typeRef, inWriter, true);

        int index = m_boxedValues.Count;
        if (inBoxedValueRef.Value is not null)
        {
            EbxExtra boxedValue = new()
            {
                Count = 1,
                Flags = tiPair.Flags,
                TypeDescriptorRef = tiPair.TypeDescriptorRef
            };
            m_boxedValues.Add(boxedValue);

            Block<byte> data = new(0);
            m_boxedValueData.Add(data);
            using (BlockStream stream = new(data, true))
            {
                if (boxedValue.Flags.GetCategoryEnum() == TypeFlags.CategoryEnum.Array)
                {
                    WriteBoxedArray(inBoxedValueRef.Value, inBoxedValueRef.Type, boxedValue.TypeDescriptorRef, stream);
                }
                else
                {
                    WriteBoxedField(inBoxedValueRef.Value, inBoxedValueRef.Type, boxedValue.TypeDescriptorRef, stream);
                }
            }
        }
        else
        {
            index = 0;
        }

        inWriter.WriteInt64(index);
    }

    private (TypeFlags, ushort) WriteTypeRef(TypeRef typeRef, DataStream writer, bool inAddSignature)
    {
        if (typeRef.IsNull())
        {
            writer.WriteInt32(0x00);
            writer.WriteInt32(0x00);
            return (0, 0);
        }

        ushort typeIdx;

        TypeFlags typeFlags = typeRef.m_type!.GetFlags();
        if (typeRef.m_type is not TypeInfoAsset &&
            (typeRef.Type!.IsAssignableTo(typeof(IPrimitive)) || typeRef.Type == s_pointerType))
        {
            typeIdx = ushort.MaxValue;
            writer.WriteUInt32(typeFlags | 0x80000000);
            writer.WriteInt32(-1);
        }
        else
        {
            typeIdx = (ushort)FindExistingType(typeRef.m_type!);
            if (typeIdx == ushort.MaxValue)
            {
                // boxed value type refs shouldn't end up here, as they're already handled when processing classes
                typeIdx = (ushort)AddType(typeRef.m_type!, inAddSignature);
            }

            writer.WriteUInt32((uint)(typeIdx << 2) | 2);
            writer.WriteInt32(0);
        }

        return (typeFlags, typeIdx);
    }

    private void WriteArray(object inObj, EbxFieldDescriptor inFieldDescriptor, DataStream writer)
    {
        // cast to IList to avoid having to invoke methods manually
        IList arrayObj = (IList)inObj;
        int count = arrayObj.Count;
        int arrayIdx = 0;

        if (count > 0)
        {
            arrayIdx = m_arrays.Count;
            m_arrays.Add(
                new EbxExtra
                {
                    Count = count,
                    TypeDescriptorRef = (ushort)FindExistingType(inObj.GetType().GenericTypeArguments[0]),
                    Flags = inFieldDescriptor.Flags
                });

            Block<byte> data = new(0);
            m_arrayData.Add(data);
            using (BlockStream arrayWriter = new(data, true))
            {
                for (int i = 0; i < count; i++)
                {
                    object subValue = arrayObj[i]!;

                    WriteField(subValue, inFieldDescriptor, arrayWriter);
                }
            }
        }
        writer.WriteInt64(arrayIdx);
    }

    private void WriteBoxedArray(object inObj, TypeFlags.TypeEnum inType, int inTypeDescriptorRef, DataStream writer)
    {
        // cast to IList to avoid having to invoke methods manually
        IList arrayObj = (IList)inObj;
        int count = arrayObj.Count;
        int arrayIdx = 0;

        if (count > 0)
        {
            arrayIdx = m_arrays.Count;
            m_arrays.Add(
                new EbxExtra
                {
                    Count = count,
                    TypeDescriptorRef = (ushort)FindExistingType(inObj.GetType().GenericTypeArguments[0]),
                    Flags = new TypeFlags(inType, TypeFlags.CategoryEnum.Array, unk: 0)
                });

            Block<byte> data = new(0);
            m_arrayData.Add(data);
            using (BlockStream arrayWriter = new(data, true))
            {
                for (int i = 0; i < count; i++)
                {
                    object subValue = arrayObj[i]!;

                    WriteBoxedField(subValue, inType, inTypeDescriptorRef, arrayWriter);
                }
            }
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
                FixupArray(ebxProperty.GetValue(obj)!, field, writer);
            }
            else
            {
                switch (field.Flags.GetTypeEnum())
                {
                    case TypeFlags.TypeEnum.Void:
                        FixupType(obj, m_typeResolver.ResolveTypeFromField(field.TypeDescriptorRef), writer, startPos);
                        break;
                    default:
                        if (ebxProperty is null)
                        {
                            continue;
                        }
                        FixupField(ebxProperty.GetValue(obj)!, field, writer);
                        break;

                }
            }
        }

        writer.Position = startPos + type.Size;
    }

    private void SharedFixupField(object obj, TypeFlags.TypeEnum inType, DataStream writer)
    {
        switch (inType)
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

    private void FixupField(object obj, EbxFieldDescriptor inFieldDescriptor, DataStream writer)
    {
        switch (inFieldDescriptor.Flags.GetTypeEnum())
        {
            case TypeFlags.TypeEnum.Struct:
            {
                EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveTypeFromField(inFieldDescriptor.TypeDescriptorRef);
                writer.Pad(typeDescriptor.Alignment);

                FixupType(obj, typeDescriptor, writer, writer.Position);
                break;
            }
            default:
                SharedFixupField(obj, inFieldDescriptor.Flags.GetTypeEnum(), writer);
                break;
        }
    }

    private void FixupBoxedField(object obj, TypeFlags.TypeEnum inType, int inTypeDescriptorRef, DataStream writer)
    {
        switch (inType)
        {
            case TypeFlags.TypeEnum.Struct:
            {
                EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(inTypeDescriptorRef);
                writer.Pad(typeDescriptor.Alignment);

                FixupType(obj, typeDescriptor, writer, writer.Position);
                break;
            }
            default:
                SharedFixupField(obj, inType, writer);
                break;
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

    private void FixupArray(object obj, EbxFieldDescriptor inFieldDescriptor, DataStream writer)
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
            long offset = array.Offset;
            offset -= writer.Position;
            // file pointers only use 32 bits, but runtime pointers need 64 bits when being patched
            // so just cast down and zero out the other 32 bits
            writer.WriteInt32((int)offset);
            writer.WriteInt32(0);

            writer.StepIn(array.Offset);
            for (int i = 0; i < array.Count; i++)
            {
                object subValue = arrayObj[i]!;
                FixupField(subValue, inFieldDescriptor, writer);
            }
            writer.StepOut();
        }
    }

    private void FixupBoxedArray(object obj, TypeFlags.TypeEnum inType, int inTypeDescriptorRef, DataStream writer)
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
            long offset = array.Offset;
            offset -= writer.Position;
            // file pointers only use 32 bits, but runtime pointers need 64 bits when being patched
            // so just cast down and zero out the other 32 bits
            writer.WriteInt32((int)offset);
            writer.WriteInt32(0);

            writer.StepIn(array.Offset);
            for (int i = 0; i < array.Count; i++)
            {
                object subValue = arrayObj[i]!;
                FixupBoxedField(subValue, inType, inTypeDescriptorRef, writer);
            }

            writer.StepOut();
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

        if (boxedVal.Flags.GetCategoryEnum() == TypeFlags.CategoryEnum.Array)
        {
            FixupBoxedArray(value.Value, boxedVal.Flags.GetTypeEnum(), boxedVal.TypeDescriptorRef, writer);
        }
        else
        {
            FixupBoxedField(value.Value, boxedVal.Flags.GetTypeEnum(), boxedVal.TypeDescriptorRef, writer);
        }

        writer.Position = oldPos;
    }
}