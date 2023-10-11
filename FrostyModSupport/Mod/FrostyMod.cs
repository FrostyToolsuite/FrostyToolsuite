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
    
    public FrostyModDetails ModDetails { get; }
    public IEnumerable<BaseModResource> Resources { get; }

    private FrostyMod(DataStream inStream)
    {
        
    }
    
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

            if (FileSystemManager.Head != stream.ReadUInt32())
            {
                // made for a different version of the game, may or may not work
            }

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
            
        }
        
        return default;
    }

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
}