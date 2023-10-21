using Frosty.ModSupport.Mod.Resources;
using Frosty.Sdk;
using Frosty.Sdk.DbObjectElements;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Utils;

namespace Frosty.ModSupport.Mod;

public class ModUpdater
{
    public enum Errors
    {
        Success = 0,
        InvalidMods = -1,
    }
    
    private static Dictionary<int, HashSet<int>> s_bundleMapping = new();
    
    public static Errors UpdateMod(string inPath)
    {
        (BaseModResource[], Block<byte>[], FrostyModDetails, uint)? mod;

        string extension = Path.GetExtension(inPath);

        if (extension == ".daimod")
        {
            // TODO: daimod convert
        }
        else if (extension != ".fbmod")
        {
            return Errors.InvalidMods;
        }
        
        using (BlockStream stream = BlockStream.FromFile(inPath, false))
        {
            // read header
            if (FrostyMod.Magic != stream.ReadUInt64())
            {
                stream.Position = 0;
                mod = UpdateLegacyFormat(stream, inPath.Replace(".fbmod", string.Empty));
            }
            else
            {
                mod = UpdateNewFormat(stream);
            }
        }

        if (mod is null)
        {
            return Errors.InvalidMods;
        }
        
        FrostyMod.Save(inPath, mod.Value.Item1, mod.Value.Item2, mod.Value.Item3, mod.Value.Item4);

        foreach (Block<byte> block in mod.Value.Item2)
        {
            block.Dispose();
        }
        
        return Errors.Success;
    }

    private static (BaseModResource[], Block<byte>[], FrostyModDetails, uint)? UpdateNewFormat(DataStream inStream)
    {
        uint version = inStream.ReadUInt32();
        
        long dataOffset = inStream.ReadInt64();
        int dataCount = inStream.ReadInt32();

        if (ProfilesLibrary.ProfileName.Equals(inStream.ReadSizedString(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        
        (BaseModResource[], Block<byte>[], FrostyModDetails, uint) retVal = default;
        retVal.Item4 = inStream.ReadUInt32(); 
        
        retVal.Item3 = new FrostyModDetails(inStream.ReadNullTerminatedString(), inStream.ReadNullTerminatedString(),
            inStream.ReadNullTerminatedString(), inStream.ReadNullTerminatedString(), inStream.ReadNullTerminatedString(),
            version > 4 ? inStream.ReadNullTerminatedString() : string.Empty);
        
        int resourceCount = inStream.ReadInt32();
        BaseModResource[] resources = new BaseModResource[resourceCount];
        for (int i = 0; i < resourceCount; i++)
        {
            ModResourceType type = (ModResourceType)inStream.ReadByte();
            (int, string, Sha1, long, int, string, IEnumerable<int>) b;
            BaseModResource.ResourceFlags flags;
            switch (type)
            {
                case ModResourceType.Embedded:
                    b = ReadBaseModResource(inStream, version);
                    resources[i] = new EmbeddedModResource(b.Item1, b.Item2);
                    break;
                case ModResourceType.Bundle:
                    b = ReadBaseModResource(inStream, version);
                    inStream.ReadNullTerminatedString();
                    int superBundleHash = inStream.ReadInt32();
                    // TODO: update hash and add bundle hash to s_bundleMapping
                    
                    resources[i] = new BundleModResource(b.Item2, superBundleHash);
                    break;
                case ModResourceType.Ebx:
                    b = ReadBaseModResource(inStream, version);
                    flags = AssetManager.GetEbxAssetEntry(b.Item2) is not null ? BaseModResource.ResourceFlags.IsAdded : 0;
                    
                    resources[i] = new EbxModResource(b.Item1, b.Item2, b.Item3, b.Item4, flags, b.Item5, b.Item6, b.Item7, Enumerable.Empty<int>());
                    break;
                case ModResourceType.Res:
                    b = ReadBaseModResource(inStream, version);
                    flags = AssetManager.GetResAssetEntry(b.Item2) is not null ? BaseModResource.ResourceFlags.IsAdded : 0;
                    
                    resources[i] = new ResModResource(b.Item1, b.Item2, b.Item3, b.Item4, flags, b.Item5, b.Item6, b.Item7,
                        Enumerable.Empty<int>(), inStream.ReadUInt32(), inStream.ReadUInt64(),
                        inStream.ReadBytes(inStream.ReadInt32()));
                    break;
                case ModResourceType.Chunk:
                    b = ReadBaseModResource(inStream, version);
                    flags = AssetManager.GetChunkAssetEntry(Guid.Parse(b.Item2)) is not null ? BaseModResource.ResourceFlags.IsAdded : 0;

                    uint rangeStart = inStream.ReadUInt32();
                    uint rangeEnd = inStream.ReadUInt32();
                    uint logicalOffset = inStream.ReadUInt32();
                    uint logicalSize = inStream.ReadUInt32();
                    int h32 = inStream.ReadInt32();
                    int firstMip = inStream.ReadInt32();
                    
                    IEnumerable<int> superBundlesToAdd = Enumerable.Empty<int>();
                    if (flags.HasFlag(BaseModResource.ResourceFlags.IsAdded))
                    {
                        // TODO: add to superbundle
                    }

                    resources[i] = new ChunkModResource(b.Item1, b.Item2, b.Item3, b.Item4, flags, b.Item5, b.Item6,
                        b.Item7, Enumerable.Empty<int>(), rangeStart, rangeEnd, logicalOffset, logicalSize, h32,
                        firstMip, superBundlesToAdd, Enumerable.Empty<int>());
                    break;
                default:
                    throw new Exception("Unexpected mod resource type");
            }
        }
        
        return retVal;
    }

    private static (BaseModResource[], Block<byte>[], FrostyModDetails, uint)? UpdateLegacyFormat(DataStream inStream,
        string modName)
    {
        DbObjectDict? mod = DbObject.Deserialize(inStream)?.AsDict();
        if (mod is null)
        {
            return null;
        }

        return default;
    }

    private static (int, string, Sha1, long, int, string, IEnumerable<int>) ReadBaseModResource(DataStream inStream, uint version)
    {
        (int, string, Sha1, long, int, string, IEnumerable<int>) retVal = default;

        retVal.Item1 = inStream.ReadInt32();

        retVal.Item2 = (version < 4 && retVal.Item1 != -1) || version > 3 ? inStream.ReadNullTerminatedString() : string.Empty;

        if (retVal.Item1 != -1)
        {
            retVal.Item3 = inStream.ReadSha1();
            retVal.Item4 = inStream.ReadInt64();
            inStream.Position += 1; // we discard the flags and just check with the AssetManager if the asset was added
            retVal.Item5 = inStream.ReadInt32();
            retVal.Item6 = version > 2 ? inStream.ReadNullTerminatedString() : string.Empty;
        }
        
        // prior to version 4, mods stored bundles the asset already existed in for modification
        // so must read and ignore this list
        if (version < 4 && retVal.Item1 != -1)
        {
            int count = inStream.ReadInt32();
            inStream.Position += count * sizeof(int);

            count = inStream.ReadInt32();
            HashSet<int> bundles = new(count);
            for (int i = 0; i < count; i++)
            {
                int hash = inStream.ReadInt32();
                bundles.UnionWith(s_bundleMapping[hash]);
            }
            retVal.Item7 = bundles;
        }

        // as of version 4, only bundles the asset will be added to are stored, existing bundles
        // are extracted from the asset manager during the apply process
        else if (version > 3)
        {
            int count = inStream.ReadInt32();
            HashSet<int> bundles = new(count);
            for (int i = 0; i < count; i++)
            {
                int hash = inStream.ReadInt32();
                bundles.UnionWith(s_bundleMapping[hash]);
            }
            retVal.Item7 = bundles;
        }
        
        return retVal;
    }
}