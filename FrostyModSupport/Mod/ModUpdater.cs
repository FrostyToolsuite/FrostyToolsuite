using System.Diagnostics;
using System.Globalization;
using System.Xml;
using Frosty.ModSupport.Mod.Resources;
using Frosty.Sdk;
using Frosty.Sdk.DbObjectElements;
using Frosty.Sdk.IO;
using Frosty.Sdk.IO.Compression;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Profiles;
using Frosty.Sdk.Utils;

namespace Frosty.ModSupport.Mod;

public class ModUpdater
{
    private static readonly Dictionary<int, HashSet<int>> s_bundleMapping = new();
    private static readonly Dictionary<int, string> s_superBundleMapping = new();

    public static bool UpdateMod(string inPath, string inNewPath)
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
            FrostyLogger.Logger?.LogError("Mod needs to be a .fbmod or .daimod");
            return false;
        }

        foreach (BundleInfo bundle in AssetManager.EnumerateBundleInfos())
        {
            int hash = Utils.HashString(bundle.Name, true);
            s_bundleMapping.TryAdd(hash, new HashSet<int>());
            s_bundleMapping[hash].Add(bundle.Id);
        }

        // v1 used a chunks bundle to add chunks to the superbundle we dont need that, so just add an empty collection
        s_bundleMapping.Add(Utils.HashString("chunks"), new HashSet<int>());

        if (FileSystemManager.BundleFormat == BundleFormat.SuperBundleManifest)
        {
            s_superBundleMapping.Add(Utils.HashStringA("<none>"),
                FileSystemManager.GetSuperBundle(FileSystemManager.DefaultInstallChunk!.SuperBundles.First())
                    .InstallChunks[0].Name);
        }
        else
        {
            foreach (SuperBundleInfo superBundle in FileSystemManager.EnumerateSuperBundles())
            {
                int hash = Utils.HashStringA(superBundle.Name, true);
                foreach (SuperBundleInstallChunk sbIc in superBundle.InstallChunks)
                {
                    s_superBundleMapping.Add(hash, sbIc.Name);
                    break;
                }
            }
        }

        using (BlockStream stream = BlockStream.FromFile(inPath, false))
        {
            if (isDaiMod)
            {
                // ancient daimods from the daimodmaker
                mod = ConvertDaiMod(stream);
            }
            else if (FrostyMod.Magic != stream.ReadUInt64())
            {
                // legacy frosty mod format, it used a DbObject
                stream.Position = 0;
                mod = UpdateLegacyFormat(stream, inPath.Replace(".fbmod", string.Empty));
            }
            else
            {
                // binary frosty mod format
                mod = UpdateNewFormat(stream);
            }
        }

        if (mod is null)
        {
            return false;
        }

        FrostyMod.Save(inNewPath, mod.Value.Item1, mod.Value.Item2, mod.Value.Item3, mod.Value.Item4);

        foreach (Block<byte> block in mod.Value.Item2)
        {
            block.Dispose();
        }

        FrostyLogger.Logger?.LogInfo("Successfully updated mod to newest format version");
        return true;
    }

    private static (BaseModResource[], Block<byte>[], FrostyModDetails, uint)? UpdateNewFormat(DataStream inStream)
    {
        uint version = inStream.ReadUInt32();

        long dataOffset = inStream.ReadInt64();
        int dataCount = inStream.ReadInt32();

        if (!ProfilesLibrary.ProfileName.Equals(inStream.ReadSizedString(), StringComparison.OrdinalIgnoreCase))
        {
            FrostyLogger.Logger?.LogError("Mod was not made for this profile");
            return null;
        }

        uint head = inStream.ReadUInt32();

        FrostyModDetails modDetails = new(inStream.ReadNullTerminatedString(), inStream.ReadNullTerminatedString(),
            inStream.ReadNullTerminatedString(), inStream.ReadNullTerminatedString(),
            inStream.ReadNullTerminatedString(), version > 4 ? inStream.ReadNullTerminatedString() : string.Empty);

        FrostyLogger.Logger?.LogInfo(
            $"Converting mod \"{modDetails.Title}\" from version {version} to {FrostyMod.Version}");

        int resourceCount = inStream.ReadInt32();
        BaseModResource[] resources = new BaseModResource[resourceCount];
        for (int i = 0; i < resourceCount; i++)
        {
            ModResourceType type = (ModResourceType)inStream.ReadByte();
            (int ResourceIndex, string Name, Sha1 Sha1, long OriginalSize, int HandlerHash, string UserData,
                IEnumerable<int> BundlesToAdd, bool HasBundleToAdd) baseResource;
            BaseModResource.ResourceFlags flags;
            switch (type)
            {
                case ModResourceType.Embedded:
                    baseResource = ReadBaseModResource(inStream, version);
                    resources[i] = new EmbeddedModResource(baseResource.ResourceIndex, baseResource.Name);
                    break;
                case ModResourceType.Bundle:
                    baseResource = ReadBaseModResource(inStream, version);
                    inStream.ReadNullTerminatedString();
                    int superBundleHash = inStream.ReadInt32();
                    string sbIcName = s_superBundleMapping[superBundleHash];
                    s_bundleMapping.Add(Utils.HashString(baseResource.Name, true), new HashSet<int>
                    {
                        Utils.HashString(baseResource.Name + sbIcName, true)
                    });

                    resources[i] = new BundleModResource(baseResource.Name, superBundleHash);
                    break;
                case ModResourceType.Ebx:
                    baseResource = ReadBaseModResource(inStream, version);

                    // we now store ebx names the same way they are stored in the bundle, so all lowercase
                    baseResource.Name = baseResource.Name.ToLower();

                    flags = AssetManager.GetEbxAssetEntry(baseResource.Name) is null
                        ? BaseModResource.ResourceFlags.IsAdded
                        : 0;

                    resources[i] = new EbxModResource(baseResource.ResourceIndex, baseResource.Name, baseResource.Sha1,
                        baseResource.OriginalSize, flags, baseResource.HandlerHash, baseResource.UserData,
                        baseResource.BundlesToAdd, Enumerable.Empty<int>());
                    break;
                case ModResourceType.Res:
                    baseResource = ReadBaseModResource(inStream, version);
                    flags = AssetManager.GetResAssetEntry(baseResource.Name) is null
                        ? BaseModResource.ResourceFlags.IsAdded
                        : 0;

                    resources[i] = new ResModResource(baseResource.ResourceIndex, baseResource.Name, baseResource.Sha1,
                        baseResource.OriginalSize, flags, baseResource.HandlerHash, baseResource.UserData,
                        baseResource.BundlesToAdd, Enumerable.Empty<int>(), inStream.ReadUInt32(),
                        inStream.ReadUInt64(), inStream.ReadBytes(inStream.ReadInt32()));
                    break;
                case ModResourceType.Chunk:
                    baseResource = ReadBaseModResource(inStream, version);
                    uint rangeStart = inStream.ReadUInt32();
                    uint rangeEnd = inStream.ReadUInt32();
                    uint logicalOffset = inStream.ReadUInt32();
                    uint logicalSize = inStream.ReadUInt32();
                    int h32 = inStream.ReadInt32();
                    int firstMip = inStream.ReadInt32();

                    flags = FixChunk(baseResource.ResourceIndex, baseResource.HasBundleToAdd, Guid.Parse(baseResource.Name),
                        ref logicalOffset, ref logicalSize, ref rangeStart, ref rangeEnd, ref firstMip,
                        out IEnumerable<int> superBundlesToAdd);

                    resources[i] = new ChunkModResource(baseResource.ResourceIndex, baseResource.Name,
                        baseResource.Sha1, baseResource.OriginalSize, flags, baseResource.HandlerHash,
                        baseResource.UserData, baseResource.BundlesToAdd, Enumerable.Empty<int>(), rangeStart, rangeEnd,
                        logicalOffset, logicalSize, h32, firstMip, superBundlesToAdd, Enumerable.Empty<int>());
                    break;
                default:
                    throw new Exception("Unexpected mod resource type");
            }
        }

        Block<byte>[] data = new Block<byte>[dataCount];
        inStream.Position = dataOffset;
        for (int i = 0; i < dataCount; i++)
        {
            long offset = inStream.ReadInt64();
            int size = (int)inStream.ReadInt64();

            long curPos = inStream.Position;
            Block<byte> block = new(size);
            inStream.Position = dataOffset + dataCount * 16 + offset;
            inStream.ReadExactly(block);
            data[i] = block;
            inStream.Position = curPos;
        }

        return (resources, data, modDetails, head);
    }

    private static (BaseModResource[], Block<byte>[], FrostyModDetails, uint)? UpdateLegacyFormat(DataStream inStream,
        string modName)
    {
        DbObjectDict? mod = DbObject.Deserialize(inStream)?.AsDict();
        if (mod is null)
        {
            FrostyLogger.Logger?.LogError("Not a valid DbObject format fbmod");
            return null;
        }

        if (!ProfilesLibrary.ProfileName.Equals(mod.AsString("gameProfile"), StringComparison.OrdinalIgnoreCase))
        {
            FrostyLogger.Logger?.LogError("Mod was not made for this profile");
            return null;
        }

        int version = int.Parse(mod.AsString("magic").Remove(0, 6));

        if (version > 2)
        {
            // we just ignore the converted daimods from v1.0.6.2, user should import the original daimod
            FrostyLogger.Logger?.LogError(
                "This mod was converted from a daimod in an older version of frosty, please update the original daimod instead");
            return null;
        }

        FrostyModDetails modDetails = new(mod.AsString("title"), mod.AsString("author"), mod.AsString("category"),
            mod.AsString("version"), mod.AsString("description"), string.Empty);

        FrostyLogger.Logger?.LogInfo(
            $"Converting legacy mod \"{modDetails.Title}\" with version {version} to new binary format with version {FrostyMod.Version}");

        if (modDetails.Description.Contains("(Converted from .daimod)"))
        {
            FrostyLogger.Logger?.LogError(
                "This mod was converted from a daimod in an older version of frosty, please update the original daimod instead");
            return null;
        }

        Dictionary<int, (HashSet<int>, HashSet<int>)> bundles = new();
        foreach (DbObject actionObj in mod.AsList("actions"))
        {
            DbObjectDict action = actionObj.AsDict();
            string bundleName = action.AsString("bundle");

            if (bundleName == "chunks")
            {
                // not sure if the old format actually used the chunks bundle, but just to be sure we skip
                continue;
            }

            int bundleHash = Utils.HashString(bundleName, true);
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
            bool hasBundlesToAdd = false;
            if (bundles.TryGetValue(id, out (HashSet<int>, HashSet<int>) b))
            {
                bundlesToAdd = b.Item1;
                bundlesToRemove = b.Item2;
                hasBundlesToAdd = b.Item1.Count > 0;
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
                {
                    int superBundleHash = Utils.HashString(resource.AsString("sb"), true);
                    string sbIcName = s_superBundleMapping[superBundleHash];
                    s_bundleMapping.Add(Utils.HashString(name, true), new HashSet<int>
                    {
                        Utils.HashString(name + sbIcName, true)
                    });

                    resources.Add(new BundleModResource(name, superBundleHash));
                    break;
                }
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

                    uint rangeStart = resource.AsUInt("rangeStart");
                    uint rangeEnd = resource.AsUInt("rangeEnd");
                    uint logicalOffset = resource.AsUInt("logicalOffset");
                    uint logicalSize = resource.AsUInt("logicalSize");
                    int h32 = resource.AsInt("h32");
                    int firstMip = resource.AsInt("firstMip", -1);

                    if (version < 2)
                    {
                        // previous mod format versions had no action listed for toc chunk changes
                        // so now have to manually add an action for it.


                        // new code requires first mip to be set to modify range values, however
                        // old mods didnt modify this. So lets force it, hopefully not too many
                        // issues result from this.
                        firstMip = 0;
                    }

                    BaseModResource.ResourceFlags flags = FixChunk(resourceIndex, hasBundlesToAdd, chunkId,
                        ref logicalOffset, ref logicalSize, ref rangeStart, ref rangeEnd, ref firstMip,
                        out IEnumerable<int> superBundlesToAdd);

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

    private static (BaseModResource[], Block<byte>[], FrostyModDetails, uint)? ConvertDaiMod(BlockStream inStream)
    {
        if (inStream.ReadFixedSizedString(8) != "DAIMODV2")
        {
            FrostyLogger.Logger?.LogError("Not a valid daimod");
            return null;
        }

        if (!ProfilesLibrary.IsLoaded(ProfileVersion.DragonAgeInquisition))
        {
            FrostyLogger.Logger?.LogError("Mod was not made for this profile");
            return null;
        }

        inStream.ReadInt32(); // unk (version idk)
        inStream.ReadNullTerminatedString(); // name
        string xml = inStream.ReadNullTerminatedString();
        inStream.ReadNullTerminatedString(); // code

        XmlDocument doc = new();
        doc.LoadXml(xml);

        XmlElement? mod = doc["daimod"];
        if (mod is null)
        {
            FrostyLogger.Logger?.LogError("Not a valid daimod");
            return null;
        }

        // create mod details
        XmlElement? detailsElem = mod["details"];
        FrostyModDetails details = new(detailsElem?["name"]?.InnerText ?? string.Empty,
            detailsElem?["author"]?.InnerText ?? string.Empty, "DAI Mods",
            detailsElem?["version"]?.InnerText ?? string.Empty,
            "Converted from DAI Mod\n" + detailsElem?["description"]?.InnerText, string.Empty);

        FrostyLogger.Logger?.LogInfo(
            $"Converting daimod \"{details.Title}\" to new binary fbmod format with version {FrostyMod.Version}");

        // get bundle actions
        Dictionary<int, (HashSet<int>, HashSet<int>)> bundles = new();
        XmlElement? bundlesElem = mod["bundles"];
        if (bundlesElem is not null)
        {
            foreach (XmlElement bundle in bundlesElem)
            {
                string bundleName = bundle.GetAttribute("name");
                int bundleHash = Utils.HashString(bundleName, true);
                string action = bundle.GetAttribute("action");

                XmlElement? entries = bundle["entries"];
                if (entries is null)
                {
                    continue;
                }

                foreach (XmlElement entry in entries)
                {
                    if (!int.TryParse(entry.GetAttribute("id"), out int resourceId))
                    {
                        continue;
                    }
                    switch (action)
                    {
                        case "modify":
                            bundles.TryAdd(resourceId, (new HashSet<int>(), new HashSet<int>()));
                            bundles[resourceId].Item1.UnionWith(s_bundleMapping[bundleHash]);
                            break;
                    }
                }
            }
        }

        XmlElement? resourcesElem = mod["resources"];
        List<BaseModResource> resources =
            new(5 + resourcesElem?.ChildNodes.Count ?? 0) { new EmbeddedModResource(-1, "Icon") };

        for (int i = 1; i < 5; i++)
        {
            resources.Add(new EmbeddedModResource(-1, $"Screenshot{i}"));
        }

        Span<byte> guidBytes = stackalloc byte[0x10];
        if (resourcesElem is not null)
        {
            foreach (XmlElement resource in resourcesElem)
            {
                string resourceName = resource.GetAttribute("name");
                string type = resource.GetAttribute("type");
                string action = resource.GetAttribute("action");
                if (!int.TryParse(resource.GetAttribute("resourceId"), out int resourceId))
                {
                    resourceId = -1;
                }
                Sha1 sha1 = resource.HasAttribute("sha1") ? new Sha1(resource.GetAttribute("sha1")) : Sha1.Zero;

                if (action == "remove")
                {
                    // we dont really need to remove the chunks
                    continue;
                }

                bool hasBundlesToAdd = false;
                IEnumerable<int> bundlesToAdd;
                IEnumerable<int> bundlesToRemove;
                if (bundles.TryGetValue(resourceId, out (HashSet<int>, HashSet<int>) b))
                {
                    bundlesToAdd = b.Item1;
                    bundlesToRemove = b.Item2;
                    hasBundlesToAdd = b.Item1.Count > 0;
                }
                else
                {
                    bundlesToAdd = Enumerable.Empty<int>();
                    bundlesToRemove = Enumerable.Empty<int>();
                }

                long.TryParse(resource.GetAttribute("originalSize"), out long originalSize);

                switch (type)
                {
                    case "ebx":
                    {
                        BaseModResource.ResourceFlags flags = AssetManager.GetEbxAssetEntry(resourceName) is null
                            ? BaseModResource.ResourceFlags.IsAdded
                            : 0;

                        Debug.Assert(flags.HasFlag(BaseModResource.ResourceFlags.IsAdded) == (action == "add"));

                        resources.Add(new EbxModResource(resourceId, resourceName, sha1, originalSize, flags, 0,
                            string.Empty, bundlesToAdd, bundlesToRemove));
                        break;
                    }
                    case "res":
                    {
                        BaseModResource.ResourceFlags flags = AssetManager.GetResAssetEntry(resourceName) is null
                            ? BaseModResource.ResourceFlags.IsAdded
                            : 0;

                        Debug.Assert(flags.HasFlag(BaseModResource.ResourceFlags.IsAdded) == (action == "add"));

                        ReadOnlySpan<char> resMetaString = resource.GetAttribute("meta");
                        byte[] resMeta = new byte[0x10];
                        for (int i = 0; i < resMeta.Length; i++)
                        {
                            resMeta[i] = byte.Parse(resMetaString.Slice(i * 2, 2), NumberStyles.HexNumber);
                        }

                        resources.Add(new ResModResource(resourceId, resourceName, sha1, originalSize, flags, 0,
                            string.Empty, bundlesToAdd, bundlesToRemove,
                            (uint)int.Parse(resource.GetAttribute("resType")),
                            (ulong)long.Parse(resource.GetAttribute("resRid")), resMeta));
                        break;
                    }
                    case "chunk":
                    {
                        ReadOnlySpan<char> idString = resourceName;
                        for (int j = 0; j < guidBytes.Length; j++)
                        {
                            guidBytes[j] = byte.Parse(idString.Slice(j * 2, 2), NumberStyles.HexNumber);
                        }

                        Guid id = new(guidBytes);

                        uint rangeStart = uint.Parse(resource.GetAttribute("rangeStart"));
                        uint rangeEnd = uint.Parse(resource.GetAttribute("rangeEnd"));
                        uint logicalOffset = uint.Parse(resource.GetAttribute("logicalOffset"));
                        uint logicalSize = uint.Parse(resource.GetAttribute("logicalSize"));
                        int h32 = int.Parse(resource.GetAttribute("chunkH32"));

                        // they store the firstMip as DbObject so we need to parse it
                        ReadOnlySpan<char> meta = resource.GetAttribute("meta");
                        Block<byte> metaBytes = new(meta.Length / 2);
                        for (int i = 0; i < metaBytes.Size; i++)
                        {
                            metaBytes[i] = byte.Parse(meta.Slice(i * 2, 2), NumberStyles.HexNumber);
                        }

                        int firstMip;
                        using (BlockStream stream = new(metaBytes))
                        {
                            firstMip = DbObject.Deserialize(stream)?.AsInt() ?? -1;
                        }

                        BaseModResource.ResourceFlags flags = FixChunk(resourceId, hasBundlesToAdd, id,
                            ref logicalOffset, ref logicalSize, ref rangeStart, ref rangeEnd, ref firstMip,
                            out IEnumerable<int> superBundlesToAdd);

                        Debug.Assert(flags.HasFlag(BaseModResource.ResourceFlags.IsAdded) == (action == "add"));

                        resources.Add(new ChunkModResource(resourceId, id.ToString(), sha1, originalSize, flags, 0,
                            string.Empty, bundlesToAdd, bundlesToRemove, rangeStart, rangeEnd, logicalOffset,
                            logicalSize, h32, firstMip, superBundlesToAdd, Enumerable.Empty<int>()));
                        break;
                    }
                }
            }
        }

        int dataCount = inStream.ReadInt32();
        Block<byte>[] data = new Block<byte>[dataCount];
        for (int i = 0; i < dataCount; i++)
        {
            Block<byte> buffer = new(inStream.ReadInt32());
            inStream.ReadExactly(buffer);
            data[i] = buffer;
        }

        return (resources.ToArray(), data, details, 0);
    }

    private static (int, string, Sha1, long, int, string, IEnumerable<int>, bool) ReadBaseModResource(DataStream inStream, uint version)
    {
        (int, string, Sha1, long, int, string, IEnumerable<int>, bool) retVal = default;

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
            retVal.Item8 = bundles.Count > 0;
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
            retVal.Item8 = bundles.Count > 0;
        }

        return retVal;
    }

    private static BaseModResource.ResourceFlags FixChunk(int inResourceIndex, bool inHasBundlesToAdd, Guid inId,
        ref uint logicalOffset, ref uint logicalSize, ref uint rangeStart, ref uint rangeEnd, ref int firstMip,
        out IEnumerable<int> superBundlesToAdd)
    {
        if (firstMip == -1 && rangeEnd != 0)
        {
            // set the firstMip to 0 and hope not too many issues arise
            firstMip = 0;
        }

        ChunkAssetEntry? entry;
        BaseModResource.ResourceFlags flags = 0;
        superBundlesToAdd = Enumerable.Empty<int>();
        if ((entry = AssetManager.GetChunkAssetEntry(inId)) is null)
        {
            flags = BaseModResource.ResourceFlags.IsAdded;

            if (!inHasBundlesToAdd || firstMip != -1)
            {
                HashSet<int> temp = new();
                SuperBundleInfo? superBundleInfo = null;
                foreach (SuperBundleInfo sb in FileSystemManager.EnumerateSuperBundles())
                {
                    if (sb.Name.Contains("chunks", StringComparison.OrdinalIgnoreCase))
                    {
                        superBundleInfo = sb;
                        break;
                    }
                }

                superBundleInfo ??= FileSystemManager.GetSuperBundle($"{FileSystemManager.GamePlatform}/globals");

                foreach (SuperBundleInstallChunk sbIc in superBundleInfo.InstallChunks)
                {
                    temp.Add(Utils.HashString(sbIc.Name, true));
                }

                Debug.Assert(temp.Count > 0);
                superBundlesToAdd = temp;
            }
        }
        else if (inResourceIndex == -1)
        {
            logicalOffset = entry.LogicalOffset;
            logicalSize = entry.LogicalSize;

            if (firstMip != -1 && rangeEnd == 0)
            {
                // we need to calculate the range in case the old mod didnt have it calculated for assets only added to bundles
                using (BlockStream stream = new(AssetManager.GetRawAsset(entry)))
                {
                    long uncompressedSize = entry.LogicalOffset + entry.LogicalSize;
                    long uncompressedBundledSize = (entry.LogicalOffset & 0xFFFF) | entry.LogicalSize;
                    long sizeLeft = uncompressedSize - uncompressedBundledSize;
                    uint size = 0;

                    while (true)
                    {
                        ulong packed = stream.ReadUInt64(Endian.Big);

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

        return flags;
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
}