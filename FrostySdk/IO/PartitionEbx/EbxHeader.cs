using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Frosty.Sdk.IO.Ebx;

namespace Frosty.Sdk.IO.PartitionEbx;

internal struct EbxHeader
{
    public Endian Endian;
    public EbxVersion Magic;
    public uint StringsOffset;
    public uint StringsAndDataLength;
    public int ImportCount;
    public ushort InstanceCount;
    public ushort ExportedInstanceCount;
    public ushort UniqueTypeCount;
    public ushort TypeDescriptorCount;
    public ushort FieldDescriptorCount;
    public ushort TypeNameTableLength;

    public uint StringTableLength;

    public int ArrayCount;
    public uint ArrayOffset;

    public uint DataLength;

    public Guid PartitionGuid;

    // Only in Version4
    public int BoxedValueCount;
    public uint BoxedValueOffset;

    public EbxImportReference[] Imports;
    public HashSet<Guid> Dependencies;

    public EbxFieldDescriptor[] FieldDescriptors;
    public EbxTypeDescriptor[] TypeDescriptors;

    public EbxInstance[] Instances;

    public EbxArray[] Arrays;

    // Only in Version4
    public EbxBoxedValue[] BoxedValues;

    public static EbxHeader ReadHeader(DataStream inStream)
    {
        EbxHeader header = new();
        header.Magic = (EbxVersion)inStream.ReadUInt32();
        if (Enum.IsDefined(typeof(EbxVersion), header.Magic))
        {
            header.Endian = Endian.Little;
        }
        else
        {
            header.Magic = (EbxVersion)BinaryPrimitives.ReverseEndianness((uint)header.Magic);
            if (!Enum.IsDefined(typeof(EbxVersion), header.Magic))
            {
                throw new Exception();
            }
            FrostyLogger.Logger?.LogWarning("Big endian ebx, might not work correctly.");
            header.Endian = Endian.Big;
        }
        header.StringsOffset = inStream.ReadUInt32(header.Endian);
        header.StringsAndDataLength = inStream.ReadUInt32(header.Endian);
        header.ImportCount = inStream.ReadInt32(header.Endian);
        header.InstanceCount = inStream.ReadUInt16(header.Endian);
        header.ExportedInstanceCount = inStream.ReadUInt16(header.Endian);
        header.UniqueTypeCount = inStream.ReadUInt16(header.Endian);
        header.TypeDescriptorCount = inStream.ReadUInt16(header.Endian);
        header.FieldDescriptorCount = inStream.ReadUInt16(header.Endian);
        header.TypeNameTableLength = inStream.ReadUInt16(header.Endian);
        header.StringTableLength = inStream.ReadUInt32(header.Endian);
        header.ArrayCount = inStream.ReadInt32(header.Endian);
        header.DataLength = inStream.ReadUInt32(header.Endian);
        header.PartitionGuid = inStream.ReadGuid(header.Endian);

        header.ArrayOffset = header.StringsOffset + header.StringTableLength + header.DataLength;

        if (header.Magic == EbxVersion.Version4)
        {
            header.BoxedValueCount = inStream.ReadInt32(header.Endian);
            header.BoxedValueOffset = inStream.ReadUInt32(header.Endian);
            header.BoxedValueOffset += header.StringsOffset + header.StringTableLength;
        }
        else
        {
            inStream.Pad(16);
        }

        header.Imports = new EbxImportReference[header.ImportCount];
        header.Dependencies = new HashSet<Guid>(header.ImportCount);
        for (int i = 0; i < header.ImportCount; i++)
        {
            EbxImportReference import = new()
            {
                FileGuid = inStream.ReadGuid(header.Endian),
                ClassGuid = inStream.ReadGuid(header.Endian)
            };

            header.Imports[i] = import;
            header.Dependencies.Add(import.FileGuid);
        }

        Dictionary<int, string> typeNames = new();

        long typeNamesOffset = inStream.Position;
        while (inStream.Position - typeNamesOffset < header.TypeNameTableLength)
        {
            string typeName = inStream.ReadNullTerminatedString();
            int hash = Utils.Utils.HashString(typeName);

            typeNames.TryAdd(hash, typeName);
        }

        header.FieldDescriptors = new EbxFieldDescriptor[header.FieldDescriptorCount];
        for (int i = 0; i < header.FieldDescriptorCount; i++)
        {
            EbxFieldDescriptor fieldDescriptor = new()
            {
                NameHash = inStream.ReadUInt32(header.Endian),
                Flags = inStream.ReadUInt16(header.Endian),
                TypeDescriptorRef = inStream.ReadUInt16(header.Endian),
                DataOffset = inStream.ReadUInt32(header.Endian),
                SecondOffset = inStream.ReadUInt32(header.Endian),
            };

            fieldDescriptor.Name = typeNames.TryGetValue((int)fieldDescriptor.NameHash, out string? value)
                ? value
                : string.Empty;

            header.FieldDescriptors[i] = fieldDescriptor;
        }

        header.TypeDescriptors = new EbxTypeDescriptor[header.TypeDescriptorCount];
        for (int i = 0; i < header.TypeDescriptorCount; i++)
        {
            EbxTypeDescriptor typeDescriptor = new()
            {
                NameHash = inStream.ReadUInt32(header.Endian),
                FieldIndex = inStream.ReadInt32(header.Endian),
                FieldCount = inStream.ReadByte(),
                Alignment = inStream.ReadByte(),
                Flags = inStream.ReadUInt16(header.Endian),
                Size = inStream.ReadUInt16(header.Endian),
                SecondSize = inStream.ReadUInt16(header.Endian),
                Index = -1
            };

            typeDescriptor.Name = typeNames.TryGetValue((int)typeDescriptor.NameHash, out string? value)
                ? value
                : string.Empty;

            header.TypeDescriptors[i] = typeDescriptor;
        }

        header.Instances = new EbxInstance[header.InstanceCount];
        for (int i = 0; i < header.InstanceCount; i++)
        {
            EbxInstance inst = new()
            {
                TypeDescriptorRef = inStream.ReadUInt16(header.Endian),
                Count = inStream.ReadUInt16(header.Endian)
            };

            if (i < header.ExportedInstanceCount)
            {
                inst.IsExported = true;
            }

            header.Instances[i] = inst;
        }

        inStream.Pad(16);

        header.Arrays = new EbxArray[header.ArrayCount];
        for (int i = 0; i < header.ArrayCount; i++)
        {
            header.Arrays[i] = new EbxArray
            {
                Offset = inStream.ReadUInt32(header.Endian),
                Count = inStream.ReadUInt32(header.Endian),
                TypeDescriptorRef = inStream.ReadInt32(header.Endian)
            };
        }

        inStream.Pad(16);

        header.BoxedValues = new EbxBoxedValue[header.BoxedValueCount];
        for (int i = 0; i < header.BoxedValueCount; i++)
        {
            header.BoxedValues[i] = new EbxBoxedValue
            {
                Offset = inStream.ReadUInt32(header.Endian),
                TypeDescriptorRef = inStream.ReadUInt16(header.Endian),
                Type = inStream.ReadUInt16(header.Endian)
            };
        }

        return header;
    }
}