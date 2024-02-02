using System;
using System.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO.RiffEbx;

public static class EbxSharedTypeDescriptors
{
    private static bool s_isInitialized;

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

    public static EbxTypeDescriptor GetTypeDescriptor(Guid inGuid, uint inSignature)
    {
        throw new NotImplementedException();
    }

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
        }
    }

    private static void ProcessChunk(DataStream inStream, FourCC inFourCc, uint inSize)
    {
        if (inFourCc != "REFL")
        {
            return;
        }

        int typeGuidCount = inStream.ReadInt32();
        for (int i = 0; i < typeGuidCount; i++)
        {
            uint signature = inStream.ReadUInt32();
            Guid guid = inStream.ReadGuid();
        }

        int typeDescriptorCount = inStream.ReadInt32();
        for (int i = 0; i < typeDescriptorCount; i++)
        {
            EbxTypeDescriptor typeDescriptor = new()
            {
                NameHash = inStream.ReadUInt32(),
                FieldIndex = inStream.ReadInt32(),
                FieldCount = inStream.ReadUInt16(),
                Flags = inStream.ReadUInt16(),
                Size = inStream.ReadUInt16(),
                Alignment = inStream.ReadUInt16(),
            };
        }

        int fieldDescriptorCount = inStream.ReadInt32();
        for (int i = 0; i < fieldDescriptorCount; i++)
        {
            EbxFieldDescriptor fieldDescriptor = new()
            {
                NameHash = inStream.ReadUInt32(),
                DataOffset = inStream.ReadUInt32(),
                Flags = inStream.ReadUInt16(),
                TypeRef = inStream.ReadUInt16()
            };
        }
    }
}