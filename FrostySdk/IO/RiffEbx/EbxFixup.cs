using System;
using System.Collections.Generic;
using Frosty.Sdk.IO.Ebx;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO.RiffEbx;

internal struct EbxFixup
{
    public Guid PartitionGuid;
    public IList<Guid> TypeGuids;
    public IList<uint> TypeSignatures;
    public int ExportedInstanceCount;
    public IList<uint> InstanceOffsets;
    public Dictionary<uint, int> InstanceMapping;
    public IList<uint> PointerOffsets;
    public IList<uint> ResourceRefOffsets;
    public IList<EbxImportReference> Imports;
    public HashSet<Guid> Dependencies;
    public IList<uint> ImportOffsets;
    public IList<uint> TypeInfoOffsets;
    public uint ArrayOffset;
    public uint BoxedValueRefOffset;
    public uint StringOffset;

    public static EbxFixup ReadFixup(DataStream inStream)
    {
        EbxFixup fixup = new()
        {
            PartitionGuid = inStream.ReadGuid(),
            TypeGuids = new Guid[inStream.ReadInt32()]
        };

        for (int i = 0; i < fixup.TypeGuids.Count; i++)
        {
            fixup.TypeGuids[i] = inStream.ReadGuid();
        }

        fixup.TypeSignatures = new uint[inStream.ReadInt32()];
        for (int i = 0; i < fixup.TypeSignatures.Count; i++)
        {
            fixup.TypeSignatures[i] = inStream.ReadUInt32();
        }

        fixup.ExportedInstanceCount = inStream.ReadInt32();

        fixup.InstanceOffsets = new uint[inStream.ReadInt32()];
        fixup.InstanceMapping = new Dictionary<uint, int>(fixup.InstanceOffsets.Count);
        for (int i = 0; i < fixup.InstanceOffsets.Count; i++)
        {
            fixup.InstanceOffsets[i] = inStream.ReadUInt32();
            fixup.InstanceMapping.Add(fixup.InstanceOffsets[i], i);
        }

        fixup.PointerOffsets = new uint[inStream.ReadInt32()];
        for (int i = 0; i < fixup.PointerOffsets.Count; i++)
        {
            fixup.PointerOffsets[i] = inStream.ReadUInt32();
        }

        fixup.ResourceRefOffsets = new uint[inStream.ReadInt32()];
        for (int i = 0; i < fixup.ResourceRefOffsets.Count; i++)
        {
            fixup.ResourceRefOffsets[i] = inStream.ReadUInt32();
        }

        fixup.Imports = new EbxImportReference[inStream.ReadInt32()];
        fixup.Dependencies = new HashSet<Guid>(fixup.Imports.Count);
        for (int i = 0; i < fixup.Imports.Count; i++)
        {
            EbxImportReference import = new()
            {
                PartitionGuid = inStream.ReadGuid(),
                InstanceGuid = inStream.ReadGuid()
            };

            fixup.Imports[i] = import;
            fixup.Dependencies.Add(import.PartitionGuid);
        }

        fixup.ImportOffsets = new uint[inStream.ReadInt32()];
        for (int i = 0; i < fixup.ImportOffsets.Count; i++)
        {
            fixup.ImportOffsets[i] = inStream.ReadUInt32();
        }

        fixup.TypeInfoOffsets = new uint[inStream.ReadInt32()];
        for (int i = 0; i < fixup.TypeInfoOffsets.Count; i++)
        {
            fixup.TypeInfoOffsets[i] = inStream.ReadUInt32();
        }

        fixup.ArrayOffset = inStream.ReadUInt32();
        fixup.BoxedValueRefOffset = inStream.ReadUInt32();
        fixup.StringOffset = inStream.ReadUInt32();
        if (ProfilesLibrary.FrostbiteVersion >= "2021")
        {
            inStream.ReadUInt32();
        }

        return fixup;
    }

    public static Block<byte> WriteFixup(EbxFixup inFixup)
    {
        Block<byte> retVal = new((1 + inFixup.TypeGuids.Count + 2 * inFixup.Imports.Count) * 16 + (12 +
                inFixup.TypeSignatures.Count + inFixup.InstanceOffsets.Count + inFixup.PointerOffsets.Count +
                inFixup.ResourceRefOffsets.Count + inFixup.ImportOffsets.Count + inFixup.TypeInfoOffsets.Count) *
            sizeof(int));
        using (BlockStream stream = new(retVal, true))
        {
            stream.WriteGuid(inFixup.PartitionGuid);

            stream.WriteInt32(inFixup.TypeGuids.Count);
            foreach (Guid typeGuid in inFixup.TypeGuids)
            {
                stream.WriteGuid(typeGuid);
            }

            stream.WriteInt32(inFixup.TypeSignatures.Count);
            foreach (uint typeSignature in inFixup.TypeSignatures)
            {
                stream.WriteUInt32(typeSignature);
            }

            stream.WriteInt32(inFixup.ExportedInstanceCount);

            stream.WriteInt32(inFixup.InstanceOffsets.Count);
            foreach (uint offset in inFixup.InstanceOffsets)
            {
                stream.WriteUInt32(offset);
            }

            stream.WriteInt32(inFixup.PointerOffsets.Count);
            foreach (uint offset in inFixup.PointerOffsets)
            {
                stream.WriteUInt32(offset);
            }

            stream.WriteInt32(inFixup.ResourceRefOffsets.Count);
            foreach (uint offset in inFixup.ResourceRefOffsets)
            {
                stream.WriteUInt32(offset);
            }

            stream.WriteInt32(inFixup.Imports.Count);
            foreach (EbxImportReference import in inFixup.Imports)
            {
                stream.WriteGuid(import.PartitionGuid);
                stream.WriteGuid(import.InstanceGuid);
            }

            stream.WriteInt32(inFixup.ImportOffsets.Count);
            foreach (uint offset in inFixup.ImportOffsets)
            {
                stream.WriteUInt32(offset);
            }

            stream.WriteInt32(inFixup.TypeInfoOffsets.Count);
            foreach (uint offset in inFixup.TypeInfoOffsets)
            {
                stream.WriteUInt32(offset);
            }

            stream.WriteUInt32(inFixup.ArrayOffset);
            stream.WriteUInt32(inFixup.BoxedValueRefOffset);
            stream.WriteUInt32(inFixup.StringOffset);
            if (ProfilesLibrary.FrostbiteVersion >= "2021")
            {
                stream.WriteUInt32(0);
            }
        }

        return retVal;
    }
}