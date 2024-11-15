using System;
using System.Collections.Generic;
using Frosty.Sdk.IO.Ebx;

namespace Frosty.Sdk.IO.PartitionEbx;

internal struct EbxHeader
{
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
        EbxHeader header = new()
        {
            Magic = (EbxVersion)inStream.ReadUInt32(),
            StringsOffset = inStream.ReadUInt32(),
            StringsAndDataLength = inStream.ReadUInt32(),
            ImportCount = inStream.ReadInt32(),
            InstanceCount = inStream.ReadUInt16(),
            ExportedInstanceCount = inStream.ReadUInt16(),
            UniqueTypeCount = inStream.ReadUInt16(),
            TypeDescriptorCount = inStream.ReadUInt16(),
            FieldDescriptorCount = inStream.ReadUInt16(),
            TypeNameTableLength = inStream.ReadUInt16(),
            StringTableLength = inStream.ReadUInt32(),
            ArrayCount = inStream.ReadInt32(),
            DataLength = inStream.ReadUInt32(),
            PartitionGuid = inStream.ReadGuid()
        };

        header.ArrayOffset = header.StringsOffset + header.StringTableLength + header.DataLength;

        if (header.Magic == EbxVersion.Version4)
        {
            header.BoxedValueCount = inStream.ReadInt32();
            header.BoxedValueOffset = inStream.ReadUInt32();
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
                PartitionGuid = inStream.ReadGuid(),
                InstanceGuid = inStream.ReadGuid()
            };

            header.Imports[i] = import;
            header.Dependencies.Add(import.PartitionGuid);
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
                NameHash = inStream.ReadUInt32(),
                Flags = inStream.ReadUInt16(),
                TypeDescriptorRef = inStream.ReadUInt16(),
                DataOffset = inStream.ReadUInt32(),
                SecondOffset = inStream.ReadUInt32(),
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
                NameHash = inStream.ReadUInt32(),
                FieldIndex = inStream.ReadInt32(),
                FieldCount = inStream.ReadByte(),
                Alignment = inStream.ReadByte(),
                Flags = inStream.ReadUInt16(),
                Size = inStream.ReadUInt16(),
                SecondSize = inStream.ReadUInt16(),
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
                TypeDescriptorRef = inStream.ReadUInt16(),
                Count = inStream.ReadUInt16()
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
                Offset = inStream.ReadUInt32(),
                Count = inStream.ReadInt32(),
                TypeDescriptorRef = inStream.ReadInt32()
            };
        }

        inStream.Pad(16);

        header.BoxedValues = new EbxBoxedValue[header.BoxedValueCount];
        for (int i = 0; i < header.BoxedValueCount; i++)
        {
            header.BoxedValues[i] = new EbxBoxedValue
            {
                Offset = inStream.ReadUInt32(),
                TypeDescriptorRef = inStream.ReadUInt16(),
                Type = inStream.ReadUInt16()
            };
        }

        return header;
    }
}