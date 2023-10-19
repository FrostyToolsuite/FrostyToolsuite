using Frosty.ModSupport.Interfaces;
using Frosty.ModSupport.Mod.Resources;
using Frosty.Sdk;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Utils;

namespace Frosty.ModSupport.Mod;

public class FrostyMod : IResourceContainer
{
    public class ResourceData
    {
        private string m_fileName;
        private long m_offset;
        private int m_size;

        public ResourceData(string inFileName, long inOffset, int inSize)
        {
            m_fileName = inFileName;
            m_offset = inOffset;
            m_size = inSize;
        }

        public Block<byte> GetData()
        {
            Block<byte> retVal = new(m_size);
            using (FileStream stream = new(m_fileName, FileMode.Open, FileAccess.Read))
            {
                stream.Position = m_offset;
                stream.ReadExactly(retVal);
            }

            return retVal;
        }
    }
    
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

    private BaseModResource[] m_resources;
    private ResourceData[] m_data;

    private FrostyMod(FrostyModDetails inModDetails, uint inHead, BaseModResource[] inResources, ResourceData[] inData)
    {
        ModDetails = inModDetails;
        m_resources = inResources;
        m_data = inData;
    }
    
    public Block<byte> GetData(int inIndex)
    {
        return m_data[inIndex].GetData();
    }
    
    /// <summary>
    /// Loads a mod from a file.
    /// </summary>
    /// <param name="inPath">The path to the file.</param>
    /// <returns>The mod or null if its not a fbmod/an older format fbmod/ fbmod made for another profile.</returns>
    /// <exception cref="Exception">When the mod file is corrupted.</exception>
    public static FrostyMod? Load(string inPath)
    {
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
            for (int i = 0; i < dataCount; i++)
            {
                data[i] = new ResourceData(inPath, stream.ReadInt64(), stream.ReadInt32());
            }
        
            return new FrostyMod(modDetails, head, resources, data);
        }
    }
    
    /// <summary>
    /// Gets the ModDetails of a mod.
    /// </summary>
    /// <param name="inPath">The file path of the mod.</param>
    /// <returns>The <see cref="FrostyModDetails"/> of that mod.</returns>
    public static FrostyModDetails? GetModDetails(string inPath)
    {
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

            long dataOffset = stream.ReadInt64();
            int dataCount = stream.ReadInt32();

            if (ProfilesLibrary.ProfileName != stream.ReadNullTerminatedString())
            {
                return null;
            }

            if (FileSystemManager.Head != stream.ReadUInt32())
            {
                // made for a different version of the game, may or may not work
            }

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
        int headerSize = sizeof(ulong) + sizeof(uint) +
                   sizeof(long) + sizeof(int) +
                   ProfilesLibrary.ProfileName.Length + 1 + sizeof(uint) +
                   inModDetails.Title.Length + 1 + inModDetails.Author.Length + 1 +
                   inModDetails.Version.Length + 1 + inModDetails.Description.Length + 1 +
                   inModDetails.Category.Length + 1 + inModDetails.ModPageLink.Length + 1;

        Block<byte> resources;
        using (DataStream stream = new(new MemoryStream()))
        {
            stream.WriteInt32(inResources.Length);

            foreach (BaseModResource resource in inResources)
            {
                resource.Write(stream);
            }

            stream.Position = 0;
            resources = new Block<byte>((int)stream.Length);
            stream.ReadExactly(resources);
        }
        
        Block<byte> data;
        Block<byte> dataHeader = new(inData.Length * (sizeof(long) + sizeof(long)));
        using (BlockStream stream = new(dataHeader, true))
        using (DataStream dataStream = new(new MemoryStream()))
        {
            foreach (Block<byte> subData in inData)
            {
                stream.WriteInt64(dataStream.Position);
                stream.WriteInt64(subData.Size);
                
                dataStream.Write(subData);
            }

            dataStream.Position = 0;
            data = new Block<byte>((int)dataStream.Length);
            dataStream.ReadExactly(data);
        }
        
        Block<byte> file = new(headerSize + resources.Size + dataHeader.Size + data.Size);
        using (BlockStream stream = new(file, true))
        {
            stream.WriteUInt64(Magic);
            stream.WriteUInt32(Version);
            
            stream.WriteInt64(headerSize + resources.Size);
            stream.WriteInt32(inData.Length);
            
            stream.WriteNullTerminatedString(ProfilesLibrary.ProfileName);
            stream.WriteUInt32(FileSystemManager.Head);
            
            stream.WriteNullTerminatedString(inModDetails.Title);
            stream.WriteNullTerminatedString(inModDetails.Author);
            stream.WriteNullTerminatedString(inModDetails.Category);
            stream.WriteNullTerminatedString(inModDetails.Version);
            stream.WriteNullTerminatedString(inModDetails.Description);
            stream.WriteNullTerminatedString(inModDetails.ModPageLink);
            
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

    internal static void Save(string inPath, BaseModResource[] inResources, Block<byte>[] inData,
        FrostyModDetails inModDetails, uint inHead)
    {
        int headerSize = sizeof(ulong) + sizeof(uint) +
                   sizeof(long) + sizeof(int) +
                   ProfilesLibrary.ProfileName.Length + 1 + sizeof(uint) +
                   inModDetails.Title.Length + 1 + inModDetails.Author.Length + 1 +
                   inModDetails.Version.Length + 1 + inModDetails.Description.Length + 1 +
                   inModDetails.Category.Length + 1 + inModDetails.ModPageLink.Length + 1;

        Block<byte> resources;
        using (DataStream stream = new(new MemoryStream()))
        {
            stream.WriteInt32(inResources.Length);

            foreach (BaseModResource resource in inResources)
            {
                resource.Write(stream);
            }

            stream.Position = 0;
            resources = new Block<byte>((int)stream.Length);
            stream.ReadExactly(resources);
        }
        
        Block<byte> data;
        Block<byte> dataHeader = new(inData.Length * (sizeof(long) + sizeof(long)));
        using (BlockStream stream = new(dataHeader, true))
        using (DataStream dataStream = new(new MemoryStream()))
        {
            foreach (Block<byte> subData in inData)
            {
                stream.WriteInt64(dataStream.Position);
                stream.WriteInt32(subData.Size);
                
                dataStream.Write(subData);
            }

            dataStream.Position = 0;
            data = new Block<byte>((int)dataStream.Length);
            dataStream.ReadExactly(data);
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