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

    private static HashSet<Guid> s_guids = new();
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
        using (BlockStream stream = new(inFile))
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
        if (inFourCc != "REFL")
        {
            return;
        }

        int startFields = s_fieldDescriptors.Count;
        int startTypes = s_typeDescriptors.Count;

        int typeSigCount = inStream.ReadInt32();
        for (int i = 0; i < typeSigCount; i++)
        {
            s_mapping.TryAdd((inStream.ReadGuid(), inStream.ReadUInt32()), i + startTypes);
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
            s_fieldDescriptors.Add(new EbxFieldDescriptor
            {
                NameHash = inStream.ReadUInt32(),
                DataOffset = inStream.ReadUInt32(),
                Flags = inStream.ReadUInt16(),
                TypeDescriptorRef = (ushort)(inStream.ReadUInt16() + startTypes)
            });
        }
    }
}