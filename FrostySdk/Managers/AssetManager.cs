using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Managers.Loaders;
using Frosty.Sdk.Managers.Patch;
using Frosty.Sdk.Resources;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.Managers;

/// <summary>
/// Manages everything related to Assets from the game.
/// </summary>
public static class AssetManager
{
    public static bool IsInitialized { get; private set; }

    public static ILogger? Logger { get; set; }

    private static readonly Dictionary<int, BundleInfo> s_bundleMapping = new();

    private static readonly Dictionary<string, EbxAssetEntry> s_ebxNameMapping = new();
    private static readonly Dictionary<Guid, EbxAssetEntry> s_ebxGuidMapping = new();

    private static readonly Dictionary<string, ResAssetEntry> s_resNameMapping = new();
    private static readonly Dictionary<ulong, ResAssetEntry> s_resRidMapping = new();

    private static readonly Dictionary<Guid, ChunkAssetEntry> s_chunkGuidMapping = new();

    /// <summary>
    /// Cache Versions:
    /// <para>1 - Initial Version</para>
    /// <para>2 - Nothing changed in the format just bumped up that the cache gets regenerated, bc bundled chunks did not always had their logical offset/size stored</para>
    /// <para>3 - Completely changed what needs to be stored</para>
    /// </summary>
    private const uint c_cacheVersion = 3;
    private const ulong c_cacheMagic = 0x02005954534F5246;

    /// <summary>
    /// Parses the games SuperBundles and creates lookups for all Assets, Bundles and SuperBundles.
    /// </summary>
    /// <param name="patchResult">The <see cref="PatchResult"/> that all the changes will get added to, if it is not null and a previous cache exists.</param>
    /// <returns>False if the initialization failed.</returns>
    public static bool Initialize(PatchResult? patchResult = null)
    {
        if (IsInitialized)
        {
            return true;
        }

        if (!FileSystemManager.IsInitialized || !ResourceManager.IsInitialized)
        {
            return false;
        }

        if (!ReadCache(out List<EbxAssetEntry> prePatchEbx, out List<ResAssetEntry> prePatchRes,
                out List<ChunkAssetEntry> prePatchChunks))
        {
            Stopwatch timer = new();

            if (FileSystemManager.BundleFormat == BundleFormat.Dynamic2018 || FileSystemManager.BundleFormat == BundleFormat.SuperBundleManifest)
            {
                Logger?.Report("Sdk", "Loading FileInfos from catalogs");

                timer.Start();
                ResourceManager.LoadInstallChunks();
                timer.Stop();

                Logger?.Report("Sdk", $"Loaded FileInfos from catalogs in {timer.Elapsed.TotalSeconds} seconds");
            }

            IAssetLoader assetLoader = GetAssetLoader();

            Logger?.Report("Sdk", "Loading Assets from SuperBundles");

            timer.Restart();
            assetLoader.Load();
            timer.Stop();

            Logger?.Report("Sdk", $"Loaded Assets from SuperBundles in {timer.Elapsed.TotalSeconds} seconds");

            ResourceManager.CLearInstallChunks();

            Logger?.Report("Sdk", "Indexing Ebx");

            timer.Restart();
            DoEbxIndexing();
            timer.Stop();

            Logger?.Report("Sdk", $"Indexed ebx in {timer.Elapsed}");

            WriteCache();

            if (prePatchEbx.Count > 0 || prePatchRes.Count > 0 || prePatchChunks.Count > 0)
            {
                if (patchResult != null)
                {
                    // modified/added ebx
                    foreach (EbxAssetEntry ebxAssetEntry in s_ebxNameMapping.Values)
                    {
                        EbxAssetEntry? prePatch = prePatchEbx.Find(e =>
                            e.Name.Equals(ebxAssetEntry.Name, StringComparison.OrdinalIgnoreCase));
                        if (prePatch is not null)
                        {
                            if (prePatch.Sha1 != ebxAssetEntry.Sha1)
                            {
                                patchResult.Modified.Ebx.Add(ebxAssetEntry.Name);
                                prePatchEbx.Remove(prePatch);
                            }
                        }
                        else
                        {
                            patchResult.Added.Ebx.Add(ebxAssetEntry.Name);
                        }
                    }

                    // modified/added res
                    foreach (ResAssetEntry resAssetEntry in s_resNameMapping.Values)
                    {
                        ResAssetEntry? prePatch = prePatchRes.Find(e =>
                            e.Name.Equals(resAssetEntry.Name, StringComparison.OrdinalIgnoreCase));
                        if (prePatch is not null)
                        {
                            if (prePatch.Sha1 != resAssetEntry.Sha1)
                            {
                                patchResult.Modified.Res.Add(resAssetEntry.Name);
                                prePatchRes.Remove(prePatch);
                            }
                        }
                        else
                        {
                            patchResult.Added.Res.Add(resAssetEntry.Name);
                        }
                    }

                    // modified/added chunks
                    foreach (ChunkAssetEntry chunkAssetEntry in s_chunkGuidMapping.Values)
                    {
                        ChunkAssetEntry? prePatch = prePatchChunks.Find(e =>
                            e.Id.Equals(chunkAssetEntry.Id));
                        if (prePatch is not null)
                        {
                            if (prePatch.Sha1 != chunkAssetEntry.Sha1)
                            {
                                patchResult.Modified.Chunks.Add(chunkAssetEntry.Id);
                                prePatchChunks.Remove(prePatch);
                            }
                        }
                        else
                        {
                            patchResult.Added.Chunks.Add(chunkAssetEntry.Id);
                        }
                    }

                    // removed ebx
                    foreach (EbxAssetEntry ebxAssetEntry in prePatchEbx)
                    {
                        patchResult.Removed.Ebx.Add(ebxAssetEntry.Name);
                    }

                    // removed res
                    foreach (ResAssetEntry resAssetEntry in prePatchRes)
                    {
                        patchResult.Removed.Res.Add(resAssetEntry.Name);
                    }

                    // removed chunks
                    foreach (ChunkAssetEntry chunkAssetEntry in prePatchChunks)
                    {
                        patchResult.Removed.Chunks.Add(chunkAssetEntry.Id);
                    }
                }

                prePatchEbx.Clear();
                prePatchRes.Clear();
                prePatchChunks.Clear();
            }
        }

        Logger?.Report("Sdk", "Finished initializing");

        IsInitialized = true;
        return true;
    }

    #region -- GetEntry --

    #region -- Bundle --

    /// <summary>
    /// Gets the <see cref="BundleInfo"/> by hash.
    /// </summary>
    /// <param name="inHash">The hash of the Bundle.</param>
    /// <returns>The <see cref="BundleInfo"/> or null if it doesn't exist.</returns>
    public static BundleInfo? GetBundleInfo(int inHash)
    {
        return s_bundleMapping.GetValueOrDefault(inHash);
    }

    #endregion

    #region -- AssetEntry --

    #region -- Ebx --

    /// <summary>
    /// Gets the <see cref="EbxAssetEntry"/> by name.
    /// </summary>
    /// <param name="name">The name of the Ebx.</param>
    /// <returns>The <see cref="EbxAssetEntry"/> or null if it doesn't exist.</returns>
    public static EbxAssetEntry? GetEbxAssetEntry(string name)
    {
        return s_ebxNameMapping.GetValueOrDefault(name.ToLower());
    }

    /// <summary>
    /// Gets the <see cref="EbxAssetEntry"/> by <see cref="Guid"/>.
    /// </summary>
    /// <param name="guid">The <see cref="Guid"/> of the Ebx.</param>
    /// <returns>The <see cref="EbxAssetEntry"/> or null if it doesn't exist.</returns>
    public static EbxAssetEntry? GetEbxAssetEntry(Guid guid)
    {
        return s_ebxGuidMapping.GetValueOrDefault(guid);
    }

    #endregion

    #region -- Res --

    /// <summary>
    /// Gets the <see cref="ResAssetEntry"/> by name.
    /// </summary>
    /// <param name="name">The name of the Res.</param>
    /// <returns>The <see cref="ResAssetEntry"/> or null if it doesn't exist.</returns>
    public static ResAssetEntry? GetResAssetEntry(string name)
    {
        return s_resNameMapping.GetValueOrDefault(name.ToLower());
    }

    /// <summary>
    /// Gets the <see cref="ResAssetEntry"/> by Rid.
    /// </summary>
    /// <param name="resRid">The Rid of the Res.</param>
    /// <returns>The <see cref="ResAssetEntry"/> or null if it doesn't exist.</returns>
    public static ResAssetEntry? GetResAssetEntry(ulong resRid)
    {
        return s_resRidMapping.GetValueOrDefault(resRid);
    }

    #endregion

    #region -- Chunk --

    /// <summary>
    /// Gets the <see cref="ChunkAssetEntry"/> by Id.
    /// </summary>
    /// <param name="chunkId">The Id of the Res.</param>
    /// <returns>The <see cref="ChunkAssetEntry"/> or null if it doesn't exist.</returns>
    public static ChunkAssetEntry? GetChunkAssetEntry(Guid chunkId)
    {
        return s_chunkGuidMapping.GetValueOrDefault(chunkId);
    }

    #endregion

    #endregion

    #endregion

    #region -- GetAsset --

    public static EbxAsset GetEbx(EbxAssetEntry entry)
    {
        using (BlockStream stream = new(GetAsset(entry)))
        {
            return EbxAsset.Deserialize(stream);
        }
    }

    public static T GetResAs<T>(ResAssetEntry entry)
        where T : Resource, new()
    {
        using (BlockStream stream = new(GetAsset(entry)))
        {
            T retVal = new();
            retVal.Set(entry);
            retVal.Deserialize(stream);

            return retVal;
        }
    }

    public static Block<byte> GetAsset(AssetEntry entry)
    {
        return entry.FileInfo!.GetData((int)entry.OriginalSize);
    }

    public static Block<byte> GetRawAsset(AssetEntry entry)
    {
        return entry.FileInfo!.GetRawData();
    }

    #endregion

    public static IEnumerable<BundleInfo> EnumerateBundleInfos()
    {
        foreach (BundleInfo bundle in s_bundleMapping.Values)
        {
            yield return bundle;
        }
    }

    public static IEnumerable<EbxAssetEntry> EnumerateEbxAssetEntries()
    {
        foreach (EbxAssetEntry entry in s_ebxNameMapping.Values)
        {
            yield return entry;
        }
    }

    public static IEnumerable<ResAssetEntry> EnumerateResAssetEntries()
    {
        foreach (ResAssetEntry entry in s_resNameMapping.Values)
        {
            yield return entry;
        }
    }

    public static IEnumerable<ChunkAssetEntry> EnumerateChunkAssetEntries()
    {
        foreach (ChunkAssetEntry entry in s_chunkGuidMapping.Values)
        {
            yield return entry;
        }
    }

    internal static BundleInfo AddBundle(string name, SuperBundleInstallChunk sbIc)
    {
        BundleInfo bundle = new(name, sbIc);
        Debug.Assert(s_bundleMapping.TryAdd(bundle.Id, bundle), "fuck");
        return bundle;
    }

    private static void UpdateBundle(string inName, BundleInfo inBundleInfo)
    {
        BundleInfo bundle = new(inName, inBundleInfo.Parent);
        s_bundleMapping.Remove(inBundleInfo.Id);
        Debug.Assert(s_bundleMapping.TryAdd(bundle.Id, bundle), "fuck");
    }

    private static IAssetLoader GetAssetLoader()
    {
        switch (FileSystemManager.BundleFormat)
        {
            case BundleFormat.Dynamic2018:
                return new Dynamic2018AssetLoader();
            case BundleFormat.Manifest2019:
                return new Manifest2019AssetLoader();
            case BundleFormat.Kelvin:
                return new KelvinAssetLoader();
            case BundleFormat.SuperBundleManifest:
                return new ManifestAssetLoader();
            default:
                throw new ArgumentException("Not valid AssetLoader.");
        }
    }

    #region -- AddingAssets --

    internal static void AddEbx(EbxAssetEntry entry, int bundleId)
    {
        if (s_ebxNameMapping.TryGetValue(entry.Name, out EbxAssetEntry? existing))
        {
            if (entry.FileInfo is not null)
            {
                existing.AddFileInfo(entry.FileInfo);
            }

            existing.Bundles.Add(bundleId);
        }
        else
        {
            entry.Bundles.Add(bundleId);
            s_ebxNameMapping.Add(entry.Name, entry);
        }
    }

    internal static void AddRes(ResAssetEntry entry, int bundleId)
    {
        if (s_resNameMapping.TryGetValue(entry.Name, out ResAssetEntry? existing))
        {
            if (entry.FileInfo is not null)
            {
                existing.AddFileInfo(entry.FileInfo);
            }

            existing.Bundles.Add(bundleId);
        }
        else
        {
            entry.Bundles.Add(bundleId);
            if (entry.ResRid != 0)
            {
                s_resRidMapping.Add(entry.ResRid, entry);
            }
            s_resNameMapping.Add(entry.Name, entry);
        }
    }

    internal static void AddChunk(ChunkAssetEntry entry, int bundleId)
    {
        if (s_chunkGuidMapping.TryGetValue(entry.Id, out ChunkAssetEntry? existing))
        {
            if (entry.FileInfo is not null)
            {
                existing.AddFileInfo(entry.FileInfo);
            }

            existing.Bundles.Add(bundleId);

            if (existing.LogicalSize == 0)
            {
                // this chunk was first added as a superbundle chunk, so add logical offset/size and sha1
                existing.Sha1 = existing.Sha1;
                existing.LogicalOffset = existing.LogicalOffset;
                existing.LogicalSize = existing.LogicalSize;
                existing.OriginalSize = entry.OriginalSize;
            }
        }
        else
        {
            entry.Bundles.Add(bundleId);
            s_chunkGuidMapping.Add(entry.Id, entry);
        }
    }

    /// <summary>
    /// Adds Chunk contained in the toc of a SuperBundle to the AssetManager.
    /// This will override any location of where an already processed chunk was stored,
    /// so that TextureChunks which are stored in Bundles have the correct data.
    /// </summary>
    /// <param name="entry">The <see cref="ChunkAssetEntry"/> of the Chunk.</param>
    internal static void AddSuperBundleChunk(ChunkAssetEntry entry)
    {
        if (s_chunkGuidMapping.TryGetValue(entry.Id, out ChunkAssetEntry? existing))
        {
            // add existing Bundles
            entry.Bundles.UnionWith(existing.Bundles);

            if (existing.FileInfo is not null)
            {
                entry.AddFileInfo(existing.FileInfo);
            }

            // add logicalOffset/Size, since those are only stored in bundles
            entry.LogicalOffset = existing.LogicalOffset;
            entry.LogicalSize = existing.LogicalSize;
            entry.OriginalSize = existing.OriginalSize;

            // merge SuperBundleInstallChunks
            entry.SuperBundleInstallChunks.UnionWith(existing.SuperBundleInstallChunks);

            s_chunkGuidMapping[entry.Id] = entry;
        }
        else
        {
            s_chunkGuidMapping.Add(entry.Id, entry);
        }
    }

    #endregion

    #region -- Cache --

    private static void DoEbxIndexing()
    {
        if (s_ebxGuidMapping.Count > 0)
        {
            return;
        }

        foreach (EbxAssetEntry entry in s_ebxNameMapping.Values)
        {
            using (BlockStream stream = new(GetAsset(entry)))
            {
                BaseEbxReader reader = BaseEbxReader.CreateReader(stream);
                entry.Type = reader.GetRootType();
                entry.Guid = reader.GetPartitionGuid();

                foreach (Guid dependency in reader.GetDependencies())
                {
                    entry.DependentAssets.Add(dependency);
                }

                Debug.Assert(!s_ebxGuidMapping.ContainsKey(entry.Guid));

                s_ebxGuidMapping.Add(entry.Guid, entry);
            }

            // Manifest AssetLoader has stripped the bundle names, so we need to figure out the ui bundles, since the ebx are not in the bundle with the same name
            if (FileSystemManager.BundleFormat == BundleFormat.SuperBundleManifest &&
                (TypeLibrary.IsSubClassOf(entry.Type, "UIItemDescriptionAsset") ||
                 TypeLibrary.IsSubClassOf(entry.Type, "UIMetaDataAsset")))
            {
                string name = $"{FileSystemManager.GamePlatform}/{entry.Name}_bundle";
                string hash = Utils.Utils.HashString(name, true).ToString("X8");

                BundleInfo? bundle = s_bundleMapping.Values.FirstOrDefault(b => b.Name == hash);
                if (bundle is not null)
                {
                    UpdateBundle(name, bundle);
                }
            }
        }
    }

    private static bool ReadCache(out List<EbxAssetEntry> prePatchEbx, out List<ResAssetEntry> prePatchRes, out List<ChunkAssetEntry> prePatchChunks)
    {
        prePatchEbx = new List<EbxAssetEntry>();
        prePatchRes = new List<ResAssetEntry>();
        prePatchChunks = new List<ChunkAssetEntry>();

        if (!File.Exists($"{FileSystemManager.CacheName}.cache"))
        {
            return false;
        }

        bool isPatched = false;

        using (DataStream stream = new(new FileStream($"{FileSystemManager.CacheName}.cache", FileMode.Open, FileAccess.Read)))
        {
            ulong magic = stream.ReadUInt64();
            if (magic != c_cacheMagic)
            {
                return false;
            }

            uint version = stream.ReadUInt32();
            if (version != c_cacheVersion)
            {
                return false;
            }

            int profileNameHash = stream.ReadInt32();
            if (profileNameHash != Utils.Utils.HashString(ProfilesLibrary.ProfileName, true))
            {
                return false;
            }

            uint head = stream.ReadUInt32();
            if (head != FileSystemManager.Head)
            {
                isPatched = true;
            }

            int bundleCount = stream.ReadInt32();
            for (int i = 0; i < bundleCount; i++)
            {
                string name = stream.ReadNullTerminatedString();
                string sbIcName = stream.ReadNullTerminatedString();
                if (!isPatched)
                {
                    SuperBundleInstallChunk sbIc = FileSystemManager.GetSuperBundleInstallChunk(sbIcName);
                    AddBundle(name, sbIc);
                }
            }

            Logger?.Report("Sdk", "Loading ebx from cache");
            int ebxCount = stream.ReadInt32();
            for (int i = 0; i < ebxCount; i++)
            {
                Logger?.Report(i / (double)ebxCount);
                string name = stream.ReadNullTerminatedString();

                EbxAssetEntry entry = new(name, stream.ReadSha1(), stream.ReadInt64())
                {
                    Guid = stream.ReadGuid(),
                    Type = stream.ReadNullTerminatedString()
                };

                entry.AddFileInfo(IFileInfo.Deserialize(stream));

                int numBundles = stream.ReadInt32();
                for (int j = 0; j < numBundles; j++)
                {
                    entry.Bundles.Add(stream.ReadInt32());
                }

                if (isPatched)
                {
                    prePatchEbx.Add(entry);
                }
                else
                {
                    s_ebxGuidMapping.Add(entry.Guid, entry);
                    s_ebxNameMapping.Add(entry.Name, entry);
                }
            }

            Logger?.Report("Sdk", "Loading res from cache");
            int resCount = stream.ReadInt32();
            for (int i = 0; i < resCount; i++)
            {
                Logger?.Report(i / (double)resCount);
                string name = stream.ReadNullTerminatedString();

                ResAssetEntry entry = new(name, stream.ReadSha1(), stream.ReadInt64(),
                    stream.ReadUInt64(), stream.ReadUInt32(), stream.ReadBytes(stream.ReadInt32()));

                entry.AddFileInfo(IFileInfo.Deserialize(stream));

                int numBundles = stream.ReadInt32();
                for (int j = 0; j < numBundles; j++)
                {
                    entry.Bundles.Add(stream.ReadInt32());
                }

                if (isPatched)
                {
                    prePatchRes.Add(entry);
                }
                else
                {
                    if (entry.ResRid != 0)
                    {
                        s_resRidMapping.Add(entry.ResRid, entry);
                    }
                    s_resNameMapping.Add(name, entry);
                }
            }

            Logger?.Report("Sdk", "Loading chunks from cache");
            int chunkCount = stream.ReadInt32();
            for (int i = 0; i < chunkCount; i++)
            {
                Logger?.Report(i / (double)chunkCount);
                ChunkAssetEntry entry = new(stream.ReadGuid(), stream.ReadSha1(),
                    stream.ReadUInt32(), stream.ReadUInt32());

                entry.AddFileInfo(IFileInfo.Deserialize(stream));

                int numSuperBundles = stream.ReadInt32();
                for (int j = 0; j < numSuperBundles; j++)
                {
                    entry.SuperBundleInstallChunks.Add(stream.ReadInt32());
                }

                int numBundles = stream.ReadInt32();
                for (int j = 0; j < numBundles; j++)
                {
                    entry.Bundles.Add(stream.ReadInt32());
                }

                if (isPatched)
                {
                    prePatchChunks.Add(entry);
                }
                else
                {
                    s_chunkGuidMapping.Add(entry.Id, entry);
                }
            }
        }

        return !isPatched;
    }

    private static void WriteCache()
    {
        FileInfo fi = new($"{FileSystemManager.CacheName}.cache");
        Directory.CreateDirectory(fi.DirectoryName!);

        using (DataStream stream = new(new FileStream(fi.FullName, FileMode.Create, FileAccess.Write)))
        {
            stream.WriteUInt64(c_cacheMagic);
            stream.WriteUInt32(c_cacheVersion);

            stream.WriteInt32(Utils.Utils.HashString(ProfilesLibrary.ProfileName, true));
            stream.WriteUInt32(FileSystemManager.Head);

            stream.WriteInt32(s_bundleMapping.Count);
            foreach (BundleInfo bundle in s_bundleMapping.Values)
            {
                stream.WriteNullTerminatedString(bundle.Name);
                stream.WriteNullTerminatedString(bundle.Parent.Name);
            }

            stream.WriteInt32(s_ebxNameMapping.Count);
            foreach (EbxAssetEntry entry in s_ebxNameMapping.Values)
            {
                stream.WriteNullTerminatedString(entry.Name);

                stream.WriteSha1(entry.Sha1);
                stream.WriteInt64(entry.OriginalSize);

                stream.WriteGuid(entry.Guid);
                stream.WriteNullTerminatedString(entry.Type);

                IFileInfo.Serialize(stream, entry.FileInfo!);

                stream.WriteInt32(entry.Bundles.Count);
                foreach (int bundleId in entry.Bundles)
                {
                    stream.WriteInt32(bundleId);
                }
            }

            stream.WriteInt32(s_resNameMapping.Count);
            foreach (ResAssetEntry entry in s_resNameMapping.Values)
            {
                stream.WriteNullTerminatedString(entry.Name);

                stream.WriteSha1(entry.Sha1);
                stream.WriteInt64(entry.OriginalSize);

                stream.WriteUInt64(entry.ResRid);
                stream.WriteUInt32(entry.ResType);
                stream.WriteInt32(entry.ResMeta.Length);
                stream.Write(entry.ResMeta, 0, entry.ResMeta.Length);

                IFileInfo.Serialize(stream, entry.FileInfo!);

                stream.WriteInt32(entry.Bundles.Count);
                foreach (int bundleId in entry.Bundles)
                {
                    stream.WriteInt32(bundleId);
                }
            }

            stream.WriteInt32(s_chunkGuidMapping.Count);
            foreach (ChunkAssetEntry entry in s_chunkGuidMapping.Values)
            {
                stream.WriteGuid(entry.Id);

                stream.WriteSha1(entry.Sha1);

                stream.WriteUInt32(entry.LogicalOffset);
                stream.WriteUInt32(entry.LogicalSize);

                IFileInfo.Serialize(stream, entry.FileInfo!);

                stream.WriteInt32(entry.SuperBundleInstallChunks.Count);
                foreach (int superBundleId in entry.SuperBundleInstallChunks)
                {
                    stream.WriteInt32(superBundleId);
                }

                stream.WriteInt32(entry.Bundles.Count);
                foreach (int bundleId in entry.Bundles)
                {
                    stream.WriteInt32(bundleId);
                }
            }
        }
    }

    #endregion
}