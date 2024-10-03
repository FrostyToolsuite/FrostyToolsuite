using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

            if (inAddType)
            {
                AddClass(objType);
            }
        }
        else if (objType.IsValueType)
        {
            AddClass(objType);
        }
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
                writer.Write(m_boxedValueData);
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
                WriteArray(ebxProperty.GetValue(obj)!, field.Flags.GetTypeEnum(), field.TypeDescriptorRef, writer);
            }
            else
            {
                switch (field.Flags.GetTypeEnum())
                {
                    case TypeFlags.TypeEnum.Inherited:
                        WriteType(obj, m_typeResolver.ResolveTypeFromField(field.TypeDescriptorRef), writer, startPos);
                        break;
                    default:
                        if (ebxProperty is null)
                        {
                            continue;
                        }
                        WriteField(ebxProperty.GetValue(obj)!, field.Flags.GetTypeEnum(), field.TypeDescriptorRef, writer);
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

    private void WriteField(object ebxObj, TypeFlags.TypeEnum ebxType, int inTypeDescriptorRef, DataStream writer)
    {
        if (ebxType == TypeFlags.TypeEnum.Struct)
        {
            EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(inTypeDescriptorRef);
            writer.Pad(typeDescriptor.Alignment);

            WriteType(ebxObj, typeDescriptor, writer, writer.Position);
        }
        else
        {
            WriteField(ebxObj, ebxType, writer);
        }
    }

    private void WriteField(object ebxObj, TypeFlags.TypeEnum ebxType, ushort inTypeDescriptorRef, DataStream writer)
    {
        if (ebxType == TypeFlags.TypeEnum.Struct)
        {
            EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveTypeFromField(inTypeDescriptorRef);
            writer.Pad(typeDescriptor.Alignment);

            WriteType(ebxObj, typeDescriptor, writer, writer.Position);
        }
        else
        {
            WriteField(ebxObj, ebxType, writer);
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
            m_boxedValueWriter ??= new BlockStream(m_boxedValueData = new Block<byte>(0), true);
            EbxExtra boxedValue = new()
            {
                Count = 1,
                Offset = (uint)m_boxedValueWriter.Position,
                Flags = tiPair.Item1,
                TypeDescriptorRef = tiPair.Item2
            };
            m_boxedValues.Add(boxedValue);
            WriteField(inBoxedValueRef.Value, inBoxedValueRef.Type, (int)boxedValue.TypeDescriptorRef, m_boxedValueWriter);
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
        EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveType(typeIdx);

        TypeFlags type = typeDescriptor.Flags;

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

    private void WriteArray(object inObj, TypeFlags.TypeEnum inType, ushort inTypeDescriptorRef, DataStream writer)
    {
        // cast to IList to avoid having to invoke methods manually
        IList arrayObj = (IList)inObj;
        int count = arrayObj.Count;
        int arrayIdx = 0;

        if (count > 0)
        {
            ushort alignment;

            switch (inType)
            {
                case TypeFlags.TypeEnum.Struct:
                    EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveTypeFromField(inTypeDescriptorRef);
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

            m_arrayWriter ??= new BlockStream(m_arrayData = new Block<byte>(0), true);

            // make sure the array data is padded correctly for the first item
            if ((m_arrayWriter.Position + 4) % alignment != 0)
            {
                m_arrayWriter.Position += alignment - (m_arrayWriter.Position + 4) % alignment;
            }

            m_arrayWriter.WriteInt32(count);

            arrayIdx = m_arrays.Count;
            m_arrays.Add(
                new EbxExtra
                {
                    Count = count,
                    TypeDescriptorRef = (ushort)FindExistingType(inObj.GetType().GenericTypeArguments[0]),
                    Flags = new TypeFlags(inType, TypeFlags.CategoryEnum.Array, unk: 0),
                    Offset = (uint)m_arrayWriter.Position + 32
                });

            for (int i = 0; i < count; i++)
            {
                object subValue = arrayObj[i]!;

                WriteField(subValue, inType, inTypeDescriptorRef, m_arrayWriter);
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
                FixupArray(ebxProperty.GetValue(obj)!, field.Flags.GetTypeEnum(), field.TypeDescriptorRef, writer);
            }
            else
            {
                switch (field.Flags.GetTypeEnum())
                {
                    case TypeFlags.TypeEnum.Inherited:
                        FixupType(obj, m_typeResolver.ResolveTypeFromField(field.TypeDescriptorRef), writer, startPos);
                        break;
                    default:
                        if (ebxProperty is null)
                        {
                            continue;
                        }
                        FixupField(ebxProperty.GetValue(obj)!, field.Flags.GetTypeEnum(), field.TypeDescriptorRef, writer);
                        break;

                }
            }
        }

        writer.Position = startPos + type.Size;
    }

    private void FixupField(object obj, TypeFlags.TypeEnum ebxType, ushort inTypeDescriptorRef, DataStream writer)
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
                EbxTypeDescriptor typeDescriptor = m_typeResolver.ResolveTypeFromField(inTypeDescriptorRef);
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

    private void FixupArray(object obj, TypeFlags.TypeEnum inType, ushort inTypeDescriptorRef, DataStream writer)
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
                FixupField(subValue, inType, inTypeDescriptorRef, writer);
            }

            writer.Position = oldPos;
            m_arrays[arrayIdx] = array;
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

        if (fieldOffset == 0)
        {

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

        FixupField(value.Value, boxedVal.Flags.GetTypeEnum(), boxedVal.TypeDescriptorRef, writer);

        writer.Position = oldPos;
    }
}