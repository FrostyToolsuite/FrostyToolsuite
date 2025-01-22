using System;
using System.Collections.Generic;
using System.IO;
using Frosty.ModSupport.Interfaces;
using Frosty.ModSupport.Mod.Resources;
using Frosty.Sdk;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Utils;

namespace Frosty.ModSupport.Mod;

public class FrostyMod : IResourceContainer
{
    /// <summary>
    /// Mod Format Versions:
    /// <para>  FBMOD   - Initial Version</para>
    /// <para>  FBMODV1 - (Unknown)</para>
    /// <para>  FBMODV2 - Special action added for chunks bundles</para>
    ///
    /// <para>  1 - Start of new binary format</para>
    /// <para>    - Support for custom data handlers (only for legacy files for now)</para>
    /// <para>  2 - Merging of defined res files (eg. ShaderBlockDepot)</para>
    /// <para>  3 - Added user data</para>
    /// <para>  4 - Various structural changes as well as removal of modifiedBundles</para>
    /// <para>  5 - Added link for the ModPage</para>
    /// <para>  6 - Storing of Added/Removed Bundles and SuperBundles</para>
    /// </summary>
    public const uint Version = 6;
    public const ulong Magic = 0x01005954534F5246;


    public IEnumerable<BaseModResource> Resources => m_resources;

    public FrostyModDetails ModDetails { get; }

    public uint Head { get; }

    public Sha1 Sha1 { get; }

    private BaseModResource[] m_resources;
    private ResourceData[] m_data;

    private FrostyMod(FrostyModDetails inModDetails, uint inHead, Sha1 inSha1, BaseModResource[] inResources, ResourceData[] inData)
    {
        ModDetails = inModDetails;
        Head = inHead;
        Sha1 = inSha1;
        m_resources = inResources;
        m_data = inData;
    }

    public ResourceData GetData(int inIndex)
    {
        return m_data[inIndex];
    }

    /// <summary>
    /// Loads a mod from a file.
    /// </summary>
    /// <param name="inPath">The path to the file.</param>
    /// <returns>The mod or null if the file does not exist, its not a fbmod, its an older format or it was made for another profile.</returns>
    /// <exception cref="Exception">When the mod file is corrupted.</exception>
    public static FrostyMod? Load(string inPath)
    {
        FileInfo fileInfo = new(inPath);
        if (!fileInfo.Exists)
        {
            return null;
        }

        using (BlockStream stream = BlockStream.FromFile(fileInfo.FullName, false))
        {
            // read header
            if (Magic != stream.ReadUInt64())
            {
                return null;
            }

            if (Version != stream.ReadUInt32())
            {
                return null;
            }

            long dataOffset = stream.ReadInt64();
            int dataCount = stream.ReadInt32();

            if (ProfilesLibrary.ProfileName != stream.ReadNullTerminatedString())
            {
                return null;
            }

            uint head = stream.ReadUInt32();

            FrostyModDetails modDetails = new(stream.ReadNullTerminatedString(), stream.ReadNullTerminatedString(),
                stream.ReadNullTerminatedString(), stream.ReadNullTerminatedString(), stream.ReadNullTerminatedString(),
                stream.ReadNullTerminatedString());

            // read resources
            Sha1 sha1 = stream.ReadSha1();
            int resourceCount = stream.ReadInt32();
            BaseModResource[] resources = new BaseModResource[resourceCount];
            for (int i = 0; i < resourceCount; i++)
            {
                ModResourceType type = (ModResourceType)stream.ReadByte();
                switch (type)
                {
                    case ModResourceType.Embedded:
                        resources[i] = new EmbeddedModResource(stream);
                        break;
                    case ModResourceType.Bundle:
                        resources[i] = new BundleModResource(stream);
                        break;
                    case ModResourceType.Ebx:
                        resources[i] = new EbxModResource(stream);
                        break;
                    case ModResourceType.Res:
                        resources[i] = new ResModResource(stream);
                        break;
                    case ModResourceType.Chunk:
                        resources[i] = new ChunkModResource(stream);
                        break;
                    case ModResourceType.FsFile:
                        resources[i] = new FsFileModResource(stream);
                        break;
                    case ModResourceType.Invalid:
                        // idk
                        break;
                    default:
                        throw new Exception("Unknown mod resource type");
                }
            }

            // read data
            stream.Position = dataOffset;
            ResourceData[] data = new ResourceData[dataCount];
            long offset = dataOffset + dataCount * 12;
            for (int i = 0; i < dataCount; i++)
            {
                data[i] = new ResourceData(fileInfo.FullName, offset + stream.ReadInt64(), stream.ReadInt32());
            }

            return new FrostyMod(modDetails, head, sha1, resources, data);
        }
    }

    /// <summary>
    /// Gets the ModDetails of a mod.
    /// </summary>
    /// <param name="inPath">The file path of the mod.</param>
    /// <returns>The <see cref="FrostyModDetails"/> of that mod or null if the file doesnt exist, its not an fbmod or an older version of the format.</returns>
    public static FrostyModDetails? GetModDetails(string inPath)
    {
        if (!File.Exists(inPath))
        {
            return null;
        }

        using (BlockStream stream = BlockStream.FromFile(inPath, false))
        {
            // read header
            if (Magic != stream.ReadUInt64())
            {
                return null;
            }

            if (Version != stream.ReadUInt32())
            {
                return null;
            }

            stream.Position += sizeof(long) + sizeof(int);

            if (ProfilesLibrary.ProfileName != stream.ReadNullTerminatedString())
            {
                return null;
            }

            stream.Position += sizeof(uint);

            return new FrostyModDetails(stream.ReadNullTerminatedString(),
                stream.ReadNullTerminatedString(), stream.ReadNullTerminatedString(), stream.ReadNullTerminatedString(),
                stream.ReadNullTerminatedString(), stream.ReadNullTerminatedString());
        }
    }

    /// <summary>
    /// Saves a mod to a file.
    /// </summary>
    /// <param name="inPath">The path of the file.</param>
    /// <param name="inResources">The resources of the mod.</param>
    /// <param name="inData">The data of the resources.</param>
    /// <param name="inModDetails">The details of the mod.</param>
    public static void Save(string inPath, BaseModResource[] inResources, Block<byte>[] inData,
        FrostyModDetails inModDetails)
    {
        Save(inPath, inResources, inData, inModDetails, FileSystemManager.Head);
    }

    internal static void Save(string inPath, BaseModResource[] inResources, Block<byte>[] inData,
        FrostyModDetails inModDetails, uint inHead)
    {
        int headerSize = sizeof(ulong) + sizeof(uint) +
                   sizeof(long) + sizeof(int) +
                   ProfilesLibrary.ProfileName.Length + 1 + sizeof(uint) +
                   inModDetails.Title.Length + 1 + inModDetails.Author.Length + 1 +
                   inModDetails.Version.Length + 1 + inModDetails.Description.Length + 1 +
                   inModDetails.Category.Length + 1 + inModDetails.ModPageLink.Length + 1 + 20;

        Block<byte> resources = new(sizeof(int) + inResources.Length * (4 + 10)); // we just estimate a min size (ResourceIndex + Name(low estimate of 9 chars))
        using (BlockStream stream = new(resources, true))
        {
            stream.WriteInt32(inResources.Length);

            foreach (BaseModResource resource in inResources)
            {
                resource.Write(stream);
            }
        }

        Block<byte> data = new(inData.Length * 100); // low estimate of the actual size
        Block<byte> dataHeader = new(inData.Length * (sizeof(long) + sizeof(int)));
        using (BlockStream stream = new(dataHeader, true))
        using (BlockStream dataStream = new(data, true))
        {
            foreach (Block<byte> subData in inData)
            {
                stream.WriteInt64(dataStream.Position);
                stream.WriteInt32(subData.Size);

                dataStream.Write(subData);
            }
        }

        Block<byte> file = new(headerSize + resources.Size + dataHeader.Size + data.Size);
        using (BlockStream stream = new(file, true))
        {
            stream.WriteUInt64(Magic);
            stream.WriteUInt32(Version);

            stream.WriteInt64(headerSize + resources.Size);
            stream.WriteInt32(inData.Length);

            stream.WriteNullTerminatedString(ProfilesLibrary.ProfileName);
            stream.WriteUInt32(inHead);

            stream.WriteNullTerminatedString(inModDetails.Title);
            stream.WriteNullTerminatedString(inModDetails.Author);
            stream.WriteNullTerminatedString(inModDetails.Category);
            stream.WriteNullTerminatedString(inModDetails.Version);
            stream.WriteNullTerminatedString(inModDetails.Description);
            stream.WriteNullTerminatedString(inModDetails.ModPageLink);

            stream.WriteSha1(Frosty.Sdk.Utils.Utils.GenerateSha1(resources));

            stream.Write(resources);
            resources.Dispose();

            stream.Write(dataHeader);
            stream.Write(data);
            dataHeader.Dispose();
            data.Dispose();
        }

        using (FileStream stream = new(inPath, FileMode.Create, FileAccess.Write))
        {
            stream.Write(file);
            file.Dispose();
        }
    }
}