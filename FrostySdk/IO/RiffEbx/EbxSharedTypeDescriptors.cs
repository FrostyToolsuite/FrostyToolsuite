using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO.RiffEbx;

internal static class EbxSharedTypeDescriptors
{
    private static bool s_isInitialized;

    private static Dictionary<(Guid, uint), int> s_mapping = new();
    private static List<EbxFieldDescriptor> s_fieldDescriptors = new();
    private static List<EbxTypeDescriptor> s_typeDescriptors = new();

    public static void Initialize()
    {
        if (s_isInitialized)
        {
            return;
        }

        if (FileSystemManager.HasFileInMemoryFs("SharedTypeDescriptors.ebx"))
        {
            Read(FileSystemManager.GetFileFromMemoryFs("SharedTypeDescriptors.ebx"));
        }

        if (FileSystemManager.HasFileInMemoryFs("SharedTypeDescriptors_patch.ebx"))
        {
            Read(FileSystemManager.GetFileFromMemoryFs("SharedTypeDescriptors_patch.ebx"));
        }

        s_isInitialized = true;
    }

    public static EbxTypeDescriptor GetTypeDescriptor(Guid inGuid, uint inSignature) => s_typeDescriptors[s_mapping[(inGuid, inSignature)]];

    public static EbxTypeDescriptor GetTypeDescriptor(ushort inIndex) => s_typeDescriptors[inIndex];

    public static EbxFieldDescriptor GetFieldDescriptors(int inIndex) => s_fieldDescriptors[inIndex];

    private static void Read(Block<byte> inFile)
    {
        using (BlockStream stream = new(inFile, true))
        {
            RiffStream riffStream = new(stream);
            riffStream.ReadHeader(out FourCC fourCc, out uint size);

            if (fourCc != "EBXT")
            {
                throw new InvalidDataException("Not a valid EBXT chunk");
            }

            // read REFL chunk
            riffStream.ReadNextChunk(ref size, ProcessChunk);

            Debug.Assert(riffStream.Eof);
        }
    }

    private static void ProcessChunk(DataStream inStream, FourCC inFourCc, uint inSize)
    {
        if (inFourCc != "REFL" && inFourCc != "RFL2")
        {
            return;
        }

        int startFields = s_fieldDescriptors.Count;
        int startTypes = s_typeDescriptors.Count;

        int typeSigCount = inStream.ReadInt32();
        Dictionary<int, int> mapping = new();
        for (int i = 0; i < typeSigCount; i++)
        {
            (Guid, uint) sig = (inStream.ReadGuid(), inStream.ReadUInt32());
            if (!s_mapping.TryAdd(sig, i + startTypes))
            {
                mapping.Add(i, s_mapping[sig]);
            }
        }

        int typeDescriptorCount = inStream.ReadInt32();
        s_typeDescriptors.Capacity = typeDescriptorCount + s_typeDescriptors.Count;
        for (int i = 0; i < typeDescriptorCount; i++)
        {
            s_typeDescriptors.Add(new EbxTypeDescriptor
            {
                NameHash = inStream.ReadUInt32(),
                FieldIndex = inStream.ReadInt32() + startFields,
                FieldCount = inStream.ReadUInt16(),
                Flags = inStream.ReadUInt16(),
                Size = inStream.ReadUInt16(),
                Alignment = inStream.ReadUInt16(),
            });
        }

        int fieldDescriptorCount = inStream.ReadInt32();
        s_fieldDescriptors.Capacity = fieldDescriptorCount + s_fieldDescriptors.Count;
        for (int i = 0; i < fieldDescriptorCount; i++)
        {
            EbxFieldDescriptor field = new()
            {
                NameHash = inStream.ReadUInt32(),
                DataOffset = inStream.ReadUInt32(),
                Flags = inStream.ReadUInt16(),
                TypeDescriptorRef = inStream.ReadUInt16()
            };
            if (field.TypeDescriptorRef != ushort.MaxValue)
            {
                if (mapping.TryGetValue(field.TypeDescriptorRef, out int actual))
                {
                    field.TypeDescriptorRef = (ushort)actual;
                }
                else
                {
                    field.TypeDescriptorRef += (ushort)startTypes;
                }
            }
            s_fieldDescriptors.Add(field);
        }

        int unkCount = inStream.ReadInt32();
        for (int i = 0; i < unkCount; i++)
        {
            uint unk = inStream.ReadUInt32();
            int count = inStream.ReadInt32(); // count of unk2
            uint index = inStream.ReadUInt32(); // maps into unk2
        }

        int unk2Count = inStream.ReadInt32();
        for (int i = 0; i < unk2Count; i++)
        {
            uint offset = inStream.ReadUInt32(); // stringtable offset?
            int index = inStream.ReadInt32(); // maps into signatures
        }

        if (inFourCc == "RFL2")
        {
            int unk3Count = inStream.ReadInt32();
        }
    }
}