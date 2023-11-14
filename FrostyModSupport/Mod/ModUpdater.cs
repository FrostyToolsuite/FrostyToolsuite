using System.Diagnostics;
using System.Xml;
using Frosty.ModSupport.Mod.Resources;
using Frosty.Sdk;
using Frosty.Sdk.DbObjectElements;
using Frosty.Sdk.IO;
using Frosty.Sdk.IO.Compression;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos;
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

    public static Errors UpdateMod(string inPath, string inNewPath)
    {
        (BaseModResource[], Block<byte>[], FrostyModDetails, uint)? mod;

        string extension = Path.GetExtension(inPath);

        bool isDaiMod = false;
        if (extension == ".daimod")
        {
            isDaiMod = true;
        }
        else if (extension != ".fbmod")
        {
            return Errors.InvalidMods;
        }

        foreach (BundleInfo bundle in AssetManager.EnumerateBundleInfos())
        {
            int hash = Utils.HashString(bundle.Name, true);
            s_bundleMapping.TryAdd(hash, new HashSet<int>());
            s_bundleMapping[hash].Add(bundle.Id);
        }

        using (BlockStream stream = BlockStream.FromFile(inPath, false))
        {
            if (isDaiMod)
            {
                mod = ConvertDaiMod(stream);
            }
            else if (FrostyMod.Magic != stream.ReadUInt64())
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

        FrostyMod.Save(inNewPath, mod.Value.Item1, mod.Value.Item2, mod.Value.Item3, mod.Value.Item4);

        foreach (Block<byte> block in mod.Value.Item2)
        {
            block.Dispose();
        }

        return Errors.Success;
    }

    private static (BaseModResource[], Block<byte>[], FrostyModDetails, uint)? ConvertDaiMod(BlockStream inStream)
    {
        if (inStream.ReadFixedSizedString(8) != "DAIMODV2")
        {
            return null;
        }

        int unk = inStream.ReadInt32();
        string name = inStream.ReadNullTerminatedString();
        string xml = inStream.ReadNullTerminatedString();
        string code = inStream.ReadNullTerminatedString();

        XmlDocument doc = new();
        doc.Load(xml);

        XmlElement? detailsElem = doc["daimod"]?["details"];
        FrostyModDetails details = new(detailsElem?["name"]?.InnerText ?? string.Empty,
            detailsElem?["author"]?.InnerText ?? string.Empty, "DAI Mods", detailsElem?["version"]?.InnerText ?? string.Empty,
            "Converted from DAI Mod\n" + detailsElem?["description"]?.InnerText, string.Empty);

        int dataCount = inStream.ReadInt32();
        List<Block<byte>> data = new(dataCount);

        return (default, data.ToArray(), details, 0);
    }

    private static (BaseModResource[], Block<byte>[], FrostyModDetails, uint)? UpdateNewFormat(DataStream inStream)
    {
        uint version = inStream.ReadUInt32();

        long dataOffset = inStream.ReadInt64();
        int dataCount = inStream.ReadInt32();

        if (!ProfilesLibrary.ProfileName.Equals(inStream.ReadSizedString(), StringComparison.OrdinalIgnoreCase))
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
            (int ResourceIndex, string Name, Sha1 Sha1, long OriginalSize, int HandlerHash, string UserData, IEnumerable<int> BundlesToAdd) b;
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

                    // we now store ebx names the same way they are stored in the bundle, so all lowercase
                    resources[i] = new EbxModResource(b.ResourceIndex, b.Name.ToLower(), b.Sha1, b.OriginalSize, flags, b.HandlerHash, b.UserData, b.BundlesToAdd, Enumerable.Empty<int>());
                    break;
                case ModResourceType.Res:
                    b = ReadBaseModResource(inStream, version);
                    flags = AssetManager.GetResAssetEntry(b.Item2) is not null ? BaseModResource.ResourceFlags.IsAdded : 0;

                    resources[i] = new ResModResource(b.ResourceIndex, b.Name, b.Sha1, b.OriginalSize, flags, b.HandlerHash, b.UserData, b.BundlesToAdd,
                        Enumerable.Empty<int>(), inStream.ReadUInt32(), inStream.ReadUInt64(),
                        inStream.ReadBytes(inStream.ReadInt32()));
                    break;
                case ModResourceType.Chunk:
                    b = ReadBaseModResource(inStream, version);

                    IEnumerable<int> superBundlesToAdd;
                    ChunkAssetEntry? entry;
                    if ((entry = AssetManager.GetChunkAssetEntry(Guid.Parse(b.Name))) is null)
                    {
                        flags = BaseModResource.ResourceFlags.IsAdded;
                        // TODO: add to some superbundles
                        superBundlesToAdd = Enumerable.Empty<int>();
                    }
                    else
                    {
                        flags = 0;
                        superBundlesToAdd = Enumerable.Empty<int>();
                    }

                    uint rangeStart = inStream.ReadUInt32();
                    uint rangeEnd = inStream.ReadUInt32();
                    uint logicalOffset = inStream.ReadUInt32();
                    uint logicalSize = inStream.ReadUInt32();
                    int h32 = inStream.ReadInt32();
                    int firstMip = inStream.ReadInt32();

                    if (b.ResourceIndex == -1 && entry is not null)
                    {
                        logicalOffset = entry.LogicalOffset;
                        logicalSize = entry.LogicalSize;

                        if (firstMip != -1 && rangeEnd == 0)
                        {
                            // we need to calculate the range, since it was not calculated
                            using (BlockStream stream = new(AssetManager.GetAsset(entry)))
                            {
                                long uncompressedSize = logicalOffset + logicalSize;
                                long uncompressedBundledSize = (logicalOffset & 0xFFFF) | logicalSize;
                                long sizeLeft = uncompressedSize - uncompressedBundledSize;
                                uint size = 0;

                                while (true)
                                {
                                    ulong packed = inStream.ReadUInt64(Endian.Big);

                                    int decompressedSize = (int)((packed >> 32) & 0x00FFFFFF);
                                    CompressionType compressionType = (CompressionType)(packed >> 24);
                                    Debug.Assert(((packed >> 20) & 0xF) == 7, "Invalid cas data");
                                    int bufferSize = (int)(packed & 0x000FFFFF);

                                    sizeLeft -= decompressedSize;
                                    if (sizeLeft < 0)
                                    {
                                        break;
                                    }

                                    if ((compressionType & ~CompressionType.Obfuscated) == CompressionType.None)
                                    {
                                        bufferSize = decompressedSize;
                                    }

                                    size += (uint)(bufferSize + 8);
                                    stream.Position += bufferSize;
                                }

                                rangeStart = size;
                                rangeEnd = (uint)stream.Length;
                            }
                        }
                    }

                    resources[i] = new ChunkModResource(b.ResourceIndex, b.Name, b.Sha1, b.OriginalSize, flags, b.HandlerHash, b.UserData,
                        b.BundlesToAdd, Enumerable.Empty<int>(), rangeStart, rangeEnd, logicalOffset, logicalSize, h32,
                        firstMip, superBundlesToAdd, Enumerable.Empty<int>());
                    break;
                default:
                    throw new Exception("Unexpected mod resource type");
            }
        }

        retVal.Item2 = new Block<byte>[dataCount];
        inStream.Position = dataOffset;
        for (int i = 0; i < dataCount; i++)
        {
            long offset = inStream.ReadInt64();
            int size = (int)inStream.ReadInt64();

            long curPos = inStream.Position;
            Block<byte> block = new(size);
            inStream.Position = dataOffset + dataCount * 16 + offset;
            inStream.ReadExactly(block);
            retVal.Item2[i] = block;
            inStream.Position = curPos;
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

        if (!ProfilesLibrary.ProfileName.Equals(mod.AsString("gameProfile"), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int version = int.Parse(mod.AsString("magic").Remove(0, 6));

        if (version > 2)
        {
            // we just ignore the converted daimods from v1.0.6.2, user should import the original daimod
            return null;
        }

        FrostyModDetails modDetails = new(mod.AsString("title"), mod.AsString("author"), mod.AsString("category"),
            mod.AsString("version"), mod.AsString("description"), string.Empty);

        Dictionary<int, (HashSet<int>, HashSet<int>)> bundles = new();
        foreach (DbObject actionObj in mod.AsList("actions"))
        {
            DbObjectDict action = actionObj.AsDict();
            int bundleHash = Utils.HashString(action.AsString("bundle"), true);
            string type = action.AsString("type");
            int resourceId = action.AsInt("resourceId");

            switch (type)
            {
                case "modify":
                    // we dont really need to do anything since we get the modified bundles through the AssetManager when applying mods
                    break;
                case "add":
                    bundles.TryAdd(resourceId, (new HashSet<int>(), new HashSet<int>()));
                    bundles[resourceId].Item1.UnionWith(s_bundleMapping[bundleHash]);
                    break;
                case "remove":
                    bundles.TryAdd(resourceId, (new HashSet<int>(), new HashSet<int>()));
                    bundles[resourceId].Item2.UnionWith(s_bundleMapping[bundleHash]);
                    break;
            }
        }

        int id = 0, resourceIndex = -1;

        DbObjectList resourcesList = mod.AsList("resources");
        List<BaseModResource> resources = new(resourcesList.Count);
        List<Block<byte>> data = new(resourcesList.Count);
        Dictionary<int, BlockStream> archiveStreams = new();

        // create mandatory resources for icon and 4 screenshots
        int index = mod.AsInt("icon", -1);
        if (index != -1)
        {
            DbObjectDict icon = resourcesList[index].AsDict();
            resourceIndex = ReadArchive(data, archiveStreams, icon, modName);
        }

        resources.Add(new EmbeddedModResource(resourceIndex, "Icon"));

        resourceIndex = -1;
        DbObjectList screenshots = mod.AsList("screenshots");
        for (int j = 0; j < 4; j++)
        {
            if (j < screenshots.Count)
            {
                index = screenshots[j].AsInt();
                if (index != -1)
                {
                    resourceIndex = ReadArchive(data, archiveStreams, resourcesList[index].AsDict(), modName);
                }
                else
                {
                    resourceIndex = -1;
                }
            }

            resources.Add(new EmbeddedModResource(resourceIndex, $"Screenshot{j}"));
        }

        resourceIndex = -1;
        foreach (DbObject resourceObj in resourcesList)
        {
            DbObjectDict resource = resourceObj.AsDict();
            string type = resource.AsString("type");
            string name = resource.AsString("name");
            Sha1 sha1 = resource.AsSha1("sha1");

            IEnumerable<int> bundlesToAdd;
            IEnumerable<int> bundlesToRemove;
            if (bundles.TryGetValue(id, out (HashSet<int>, HashSet<int>) b))
            {
                bundlesToAdd = b.Item1;
                bundlesToRemove = b.Item2;
            }
            else
            {
                bundlesToAdd = Enumerable.Empty<int>();
                bundlesToRemove = Enumerable.Empty<int>();
            }

            if (type != "embedded")
            {
                resourceIndex = ReadArchive(data, archiveStreams, resource, modName);
            }

            switch (type)
            {
                case "superbundle":
                    // not supported yet
                    break;
                case "bundle":
                    throw new NotImplementedException();
                case "ebx":
                {
                    BaseModResource.ResourceFlags flags = AssetManager.GetEbxAssetEntry(name) is null
                        ? BaseModResource.ResourceFlags.IsAdded
                        : 0;
                    resources.Add(new EbxModResource(resourceIndex, name, sha1, resource.AsLong("uncompressedSize"),
                        flags, 0, string.Empty, bundlesToAdd, bundlesToRemove));
                    break;
                }
                case "res":
                {
                    BaseModResource.ResourceFlags flags = AssetManager.GetResAssetEntry(name) is null
                        ? BaseModResource.ResourceFlags.IsAdded
                        : 0;
                    resources.Add(new ResModResource(resourceIndex, name, sha1, resource.AsLong("uncompressedSize"),
                        flags, 0, string.Empty, bundlesToAdd, bundlesToRemove, resource.AsUInt("resType"),
                        resource.AsULong("resRid"), resource.AsBlob("resMeta")));
                    break;
                }
                case "chunk":
                {
                    Guid chunkId = Guid.Parse(name);

                    BaseModResource.ResourceFlags flags;
                    IEnumerable<int> superBundlesToAdd;
                    ChunkAssetEntry? entry;
                    if ((entry = AssetManager.GetChunkAssetEntry(chunkId)) is null)
                    {
                        flags = BaseModResource.ResourceFlags.IsAdded;
                        // TODO: add to some superbundles

                        HashSet<int> temp = new();
                        foreach (SuperBundleInfo superBundleInfo in FileSystemManager.EnumerateSuperBundles())
                        {
                            switch (FileSystemManager.BundleFormat)
                            {
                                case BundleFormat.Dynamic2018:
                                case BundleFormat.Manifest2019:
                                    if (!superBundleInfo.Name.Equals(FileSystemManager.GamePlatform + "/chunks0",
                                            StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    foreach (SuperBundleInstallChunk sbIc in superBundleInfo.InstallChunks)
                                    {
                                        temp.Add(Utils.HashString(sbIc.Name, true));
                                    }
                                    break;
                                case BundleFormat.Kelvin:
                                    if (!superBundleInfo.Name.Equals(FileSystemManager.GamePlatform + "/globals",
                                            StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    foreach (SuperBundleInstallChunk sbIc in superBundleInfo.InstallChunks)
                                    {
                                        temp.Add(Utils.HashString(sbIc.Name, true));
                                    }
                                    break;
                                case BundleFormat.SuperBundleManifest:
                                    continue;
                            }

                            break;
                        }

                        superBundlesToAdd = temp;
                    }
                    else
                    {
                        flags = 0;
                        superBundlesToAdd = Enumerable.Empty<int>();
                    }

                    uint rangeStart = resource.AsUInt("rangeStart");
                    uint rangeEnd = resource.AsUInt("rangeEnd");
                    uint logicalOffset = resource.AsUInt("logicalOffset");
                    uint logicalSize = resource.AsUInt("logicalSize");
                    int h32 = resource.AsInt("h32");
                    int firstMip = resource.AsInt("firstMip", -1);

                    if (firstMip == -1 && rangeEnd != 0)
                    {
                        // set the firstmip to 0 and hope not too many issues arise
                        firstMip = 0;
                    }

                    if (resourceIndex == -1 && entry is not null)
                    {
                        logicalOffset = entry.LogicalOffset;
                        logicalSize = entry.LogicalSize;

                        if (firstMip != -1 && rangeEnd == 0)
                        {
                            // we need to calculate the range in case the old mod didnt have it calculated for assets only added to bundles
                            using (BlockStream stream = new(AssetManager.GetAsset(entry)))
                            {
                                long uncompressedSize = entry.LogicalOffset + entry.LogicalSize;
                                long uncompressedBundledSize = (entry.LogicalOffset & 0xFFFF) | entry.LogicalSize;
                                long sizeLeft = uncompressedSize - uncompressedBundledSize;
                                uint size = 0;

                                while (true)
                                {
                                    ulong packed = inStream.ReadUInt64(Endian.Big);

                                    int decompressedSize = (int)((packed >> 32) & 0x00FFFFFF);
                                    CompressionType compressionType = (CompressionType)(packed >> 24);
                                    Debug.Assert(((packed >> 20) & 0xF) == 7, "Invalid cas data");
                                    int bufferSize = (int)(packed & 0x000FFFFF);

                                    sizeLeft -= decompressedSize;
                                    if (sizeLeft < 0)
                                    {
                                        break;
                                    }

                                    if ((compressionType & ~CompressionType.Obfuscated) == CompressionType.None)
                                    {
                                        bufferSize = decompressedSize;
                                    }

                                    size += (uint)(bufferSize + 8);
                                    stream.Position += bufferSize;
                                }

                                rangeStart = size;
                                rangeEnd = (uint)stream.Length;
                            }
                        }
                    }

                    if (version < 2)
                    {
                        // previous mod format versions had no action listed for toc chunk changes
                        // so now have to manually add an action for it.


                        // new code requires first mip to be set to modify range values, however
                        // old mods didnt modify this. So lets force it, hopefully not too many
                        // issues result from this.
                        firstMip = 0;
                    }

                    resources.Add(new ChunkModResource(resourceIndex, name, sha1, resource.AsLong("uncompressedSize"),
                        flags, 0, string.Empty, bundlesToAdd, bundlesToRemove, rangeStart, rangeEnd, logicalOffset,
                        logicalSize, h32, firstMip, superBundlesToAdd, Enumerable.Empty<int>()));
                    break;
                }
            }

            id++;
        }

        foreach (BlockStream stream in archiveStreams.Values)
        {
            stream.Dispose();
        }

        return (resources.ToArray(), data.ToArray(), modDetails, mod.AsUInt("gameVersion"));
    }

    private static int ReadArchive(List<Block<byte>> data, Dictionary<int, BlockStream> archiveStreams,
        DbObjectDict inDict, string inModName)
    {
        int archiveIndex = inDict.AsInt("archiveIndex", -1);
        if (archiveIndex == -1)
        {
            return -1;
        }

        if (!archiveStreams.TryGetValue(archiveIndex, out BlockStream? stream))
        {
            string path = $"{inModName}_{archiveIndex:D2}.archive";
            if (!File.Exists(path))
            {
                return -1;
            }
            stream = BlockStream.FromFile(path, false);
        }

        uint archiveOffset = inDict.AsUInt("archiveOffset");
        stream.Position = archiveOffset;
        Block<byte> block = new(inDict.AsInt("compressedSize"));
        stream.ReadExactly(block);

        data.Add(block);

        return data.Count - 1;
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