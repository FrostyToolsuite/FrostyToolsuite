using System;
using System.Collections.Generic;
using Frosty.Sdk.IO.Ebx;

namespace Frosty.Sdk.IO.RiffEbx;

internal struct EbxFixup
{
    public Guid PartitionGuid;
    public Guid[] TypeGuids;
    public uint[] TypeSignatures;
    public int ExportedInstanceCount;
    public uint[] InstanceOffsets;
    public Dictionary<uint, int> InstanceMapping;
    public uint[] PointerOffsets;
    public uint[] ResourceRefOffsets;
    public EbxImportReference[] Imports;
    public HashSet<Guid> Dependencies;
    public uint[] ImportOffsets;
    public uint[] TypeInfoOffsets;
    public uint ArrayOffset;
    public uint BoxedValueRefOffset;
    public uint StringOffset;
    // content/cinematic/stadiummultiset/prefab/sms_lockerroom_setdressing/sms_lockerroom_postmatchdressing
    public static EbxFixup ReadFixup(DataStream inStream)
    {
        EbxFixup fixup = new() { PartitionGuid = inStream.ReadGuid(), };

        fixup.TypeGuids = new Guid[inStream.ReadInt32()];
        for (int i = 0; i < fixup.TypeGuids.Length; i++)
        {
            fixup.TypeGuids[i] = inStream.ReadGuid();
        }

        fixup.TypeSignatures = new uint[inStream.ReadInt32()];
        for (int i = 0; i < fixup.TypeSignatures.Length; i++)
        {
            fixup.TypeSignatures[i] = inStream.ReadUInt32();
        }

        fixup.ExportedInstanceCount = inStream.ReadInt32();

        fixup.InstanceOffsets = new uint[inStream.ReadInt32()];
        fixup.InstanceMapping = new Dictionary<uint, int>(fixup.InstanceOffsets.Length);
        for (int i = 0; i < fixup.InstanceOffsets.Length; i++)
        {
            fixup.InstanceOffsets[i] = inStream.ReadUInt32();
            fixup.InstanceMapping.Add(fixup.InstanceOffsets[i], i);
        }

        fixup.PointerOffsets = new uint[inStream.ReadInt32()];
        for (int i = 0; i < fixup.PointerOffsets.Length; i++)
        {
            fixup.PointerOffsets[i] = inStream.ReadUInt32();
        }

        fixup.ResourceRefOffsets = new uint[inStream.ReadInt32()];
        for (int i = 0; i < fixup.ResourceRefOffsets.Length; i++)
        {
            fixup.ResourceRefOffsets[i] = inStream.ReadUInt32();
        }

        fixup.Imports = new EbxImportReference[inStream.ReadInt32()];
        fixup.Dependencies = new HashSet<Guid>(fixup.Imports.Length);
        for (int i = 0; i < fixup.Imports.Length; i++)
        {
            EbxImportReference import = new()
            {
                FileGuid = inStream.ReadGuid(),
                ClassGuid = inStream.ReadGuid()
            };

            fixup.Imports[i] = import;
            fixup.Dependencies.Add(import.FileGuid);
        }

        fixup.ImportOffsets = new uint[inStream.ReadInt32()];
        for (int i = 0; i < fixup.ImportOffsets.Length; i++)
        {
            fixup.ImportOffsets[i] = inStream.ReadUInt32();
        }

        fixup.TypeInfoOffsets = new uint[inStream.ReadInt32()];
        for (int i = 0; i < fixup.TypeInfoOffsets.Length; i++)
        {
            fixup.TypeInfoOffsets[i] = inStream.ReadUInt32();
        }

        fixup.ArrayOffset = inStream.ReadUInt32();
        fixup.BoxedValueRefOffset = inStream.ReadUInt32();
        fixup.StringOffset = inStream.ReadUInt32();

        return fixup;
    }

}