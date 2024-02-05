using System;
using System.Collections.Generic;
using System.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO.PartitionEbx;

public static class EbxSharedTypeDescriptors
{
    private static bool s_isInitialized;

    private static Dictionary<uint, Guid> s_typeKeyMapping = new();
    private static Dictionary<Guid, int> s_keyTypeMapping = new();
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

    public static EbxTypeDescriptor GetTypeDescriptor(Guid key)
    {
        return s_typeDescriptors[s_keyTypeMapping[key]];
    }

    public static EbxTypeDescriptor GetTypeDescriptor(short index)
    {
        return s_typeDescriptors[index];
    }

    public static EbxFieldDescriptor GetFieldDescriptor(int index)
    {
        return s_fieldDescriptors[index];
    }

    public static EbxTypeDescriptor GetKey(EbxTypeDescriptor inTypeDescriptor)
    {
        return new EbxTypeDescriptor(s_typeKeyMapping[inTypeDescriptor.NameHash]);
    }

    private static void Read(Block<byte> file)
    {
        using (BlockStream stream = new(file))
        {
            EbxVersion magic = (EbxVersion)stream.ReadUInt32();

            if (magic != EbxVersion.Version4)
            {
                throw new InvalidDataException("magic");
            }

            ushort typeDescriptorCount = stream.ReadUInt16();
            ushort fieldDescriptorCount = stream.ReadUInt16();

            s_typeDescriptors.Capacity = typeDescriptorCount + s_typeDescriptors.Count;
            s_fieldDescriptors.Capacity = fieldDescriptorCount + s_fieldDescriptors.Count;

            int startFields = s_fieldDescriptors.Count;

            for (int i = 0; i < fieldDescriptorCount; i++)
            {
                s_fieldDescriptors.Add(new EbxFieldDescriptor()
                {
                    NameHash = stream.ReadUInt32(),
                    Flags = stream.ReadUInt16(),
                    TypeDescriptorRef = stream.ReadUInt16(),
                    DataOffset = stream.ReadUInt32(),
                    SecondOffset = stream.ReadUInt32(),
                });
            }

            for (int i = 0; i < typeDescriptorCount; i++)
            {
                long offset = stream.Position;

                Guid key = stream.ReadGuid();

                EbxTypeDescriptor typeDescriptor = new()
                {
                    NameHash = stream.ReadUInt32(),
                    FieldIndex = stream.ReadInt32(),
                    FieldCount = stream.ReadByte(),
                    Alignment = stream.ReadByte(),
                    Flags = stream.ReadUInt16(),
                    Size = stream.ReadUInt16(),
                    SecondSize = stream.ReadUInt16(),
                    Index = s_typeDescriptors.Count
                };

                if (typeDescriptor.Index == 6459)
                {

                }

                typeDescriptor.Name = TypeLibrary.GetType(typeDescriptor.NameHash)?.Name ?? string.Empty;

                if (typeDescriptor.IsSharedTypeDescriptorKey())
                {
                    // reference to already existing type descriptor
                    typeDescriptor = s_typeDescriptors[s_keyTypeMapping[key]];
                }
                else
                {
                    // its a relative offset to the field, so we have to calculate the index
                    typeDescriptor.FieldIndex = (int)((offset - (typeDescriptor.FieldIndex - 0x08)) / 0x10 + startFields);
                    s_keyTypeMapping.Remove(key);
                    s_keyTypeMapping.Add(key, s_typeDescriptors.Count);
                    s_typeKeyMapping.Remove(typeDescriptor.NameHash);
                    s_typeKeyMapping.Add(typeDescriptor.NameHash, key);
                }

                s_typeDescriptors.Add(typeDescriptor);
            }
        }
    }
}