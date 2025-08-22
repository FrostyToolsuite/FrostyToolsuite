using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Frosty.ModSupport.Archive;
using Frosty.ModSupport.Attributes;
using Frosty.ModSupport.Interfaces;
using Frosty.ModSupport.Mod;
using Frosty.ModSupport.Mod.Resources;
using Frosty.ModSupport.ModEntries;
using Frosty.ModSupport.ModInfos;
using Frosty.Sdk;
using Frosty.Sdk.DbObjectElements;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Utils;
using Microsoft.Extensions.Logging;

namespace Frosty.ModSupport;

public partial class FrostyModExecutor
{
    private readonly Dictionary<string, EbxModEntry> m_modifiedEbx = new();
    private readonly Dictionary<string, ResModEntry> m_modifiedRes = new();
    private readonly Dictionary<Guid, ChunkModEntry> m_modifiedChunks = new();

    private readonly List<IModEntry> m_handlerAssets = new();

    private readonly Dictionary<Sha1, ResourceData> m_data = new();
    private readonly Dictionary<Sha1, Block<byte>> m_memoryData = new();

    private readonly Dictionary<int, SuperBundleModInfo> m_superBundleModInfos = new();
    private readonly Dictionary<int, int> m_bundleToSuperBundleMapping = new();

    private readonly Dictionary<int, Type> m_handlers = new();

    private readonly Dictionary<Guid, InstallChunkWriter> m_installChunkWriters = new();

    private string m_patchPath = string.Empty;
    private string m_modDataPath = string.Empty;
    private string m_gamePatchPath = string.Empty;

    /// <summary>
    /// Generates a directory containing the modded games data.
    /// </summary>
    /// <param name="inModPackPath">The full path of the directory where the modified data is going to be stored.</param>
    /// <param name="inModPaths">The full paths of the mods.</param>
    public Errors GenerateMods(string inModPackPath, IEnumerable<string> inModPaths)
    {
        if (!FileSystemManager.Initialize(inModPackPath))
        {
            return Errors.FailedToInitialize;
        }

        // define some paths we are going to need
        m_patchPath = FileSystemManager.Sources.Count == 1
            ? FileSystemSource.Base.Path
            : FileSystemSource.Patch.Path;
        m_modDataPath = Path.Combine(inModPackPath, m_patchPath);
        m_gamePatchPath = Path.Combine(FileSystemManager.BasePath, m_patchPath);

        // check if we need to generate new data
        string modInfosPath = Path.Combine(inModPackPath, "mods.json");
        List<ModInfo> modInfos = GenerateModInfoList(inModPaths);

        string headPath = Path.Combine(inModPackPath, "head.txt");
        if (File.Exists(modInfosPath) && File.Exists(headPath))
        {
            List<ModInfo>? oldModInfos = JsonSerializer.Deserialize<List<ModInfo>>(File.ReadAllText(modInfosPath));
            string head = File.ReadAllText(headPath);
            if (oldModInfos?.SequenceEqual(modInfos) == true && FileSystemManager.Head == uint.Parse(head))
            {
                return Errors.NoUpdateNeeded;
            }
        }

        // make sure the managers are initialized
        if (!ResourceManager.Initialize() || !AssetManager.Initialize())
        {
            return Errors.FailedToInitialize;
        }

        // load handlers from Handlers directory
        LoadHandlers();

        // process all mods
        foreach (ModInfo modInfo in modInfos)
        {
            string extension = Path.GetExtension(modInfo.Path);
            if (extension == ".fbmod")
            {
                FrostyMod? mod = FrostyMod.Load(modInfo.Path);
                if (mod is null)
                {
                    return Errors.InvalidMods;
                }
                if (mod.Head != FileSystemManager.Head)
                {
                    FrostyLogger.Logger?.LogWarning($"Mod {mod.ModDetails.Title} was made for a different version of the game, it might or might not work");
                }
                ProcessModResources(mod);
            }
            else if (extension == ".fbcollection")
            {
                FrostyModCollection? modCollection = FrostyModCollection.Load(modInfo.Path);
                if (modCollection is null)
                {
                    return Errors.InvalidMods;
                }

                foreach (FrostyMod mod in modCollection.Mods)
                {
                    if (mod.Head != FileSystemManager.Head)
                    {
                        FrostyLogger.Logger?.LogWarning($"Mod {mod.ModDetails.Title} was made for a different version of the game, it might or might not work");
                    }
                    ProcessModResources(mod);
                }
            }
        }

        // apply handlers
        foreach (IModEntry entry in m_handlerAssets)
        {
            // entry.Handler will never be null, since the assets added to m_handlerAssets always have a handler set
            entry.Handler!.Modify(entry, out Block<byte> data);
            m_memoryData.TryAdd(entry.Sha1, data);
        }

        // clear old generated mod data
        if (Directory.Exists(inModPackPath))
        {
            Directory.Delete(inModPackPath, true);
        }
        Directory.CreateDirectory(m_modDataPath);

        // write head to file so we know for which game version this data was generated
        File.WriteAllText(modInfosPath, JsonSerializer.Serialize(modInfos));
        File.WriteAllText(headPath, FileSystemManager.Head.ToString());

        // modify the superbundles and write them to mod data
        foreach (KeyValuePair<int, SuperBundleModInfo> sb in m_superBundleModInfos)
        {
            SuperBundleInstallChunk sbIc = FileSystemManager.GetSuperBundleInstallChunk(sb.Key);

            InstallChunkWriter installChunkWriter = GetInstallChunkWriter(sbIc);

            switch (FileSystemManager.BundleFormat)
            {
                case BundleFormat.Dynamic2018:
                    // clear Data so we can add only the ones we need to write to cas
                    sb.Value.Data.Clear();
                    ModDynamic2018(sbIc, sb.Value, installChunkWriter);
                    break;
                case BundleFormat.Manifest2019:
                    // write all cas files before the action, since we need the offset before writing
                    WriteCasArchives(sb.Value, installChunkWriter);
                    ModManifest2019(sbIc, sb.Value, installChunkWriter);
                    break;
                case BundleFormat.SuperBundleManifest:
                    throw new NotImplementedException();
                    break;
                case BundleFormat.Kelvin:
                    throw new NotImplementedException();
                    break;
            }
        }

        // we need to write the cas files at the end bc of non cas format
        if (FileSystemManager.BundleFormat == BundleFormat.Dynamic2018 || FileSystemManager.BundleFormat == BundleFormat.SuperBundleManifest)
        {
            foreach (KeyValuePair<int, SuperBundleModInfo> sb in m_superBundleModInfos)
            {
                SuperBundleInstallChunk sbIc = FileSystemManager.GetSuperBundleInstallChunk(sb.Key);

                InstallChunkWriter installChunkWriter = GetInstallChunkWriter(sbIc);

                WriteCasArchives(sb.Value, installChunkWriter);
            }

            foreach (InstallChunkWriter writer in m_installChunkWriters.Values)
            {
                writer.WriteCatalog();
            }
        }

        if (FileSystemManager.BundleFormat == BundleFormat.Manifest2019)
        {
            DbObjectDict layout = DbObject.Deserialize(Path.Combine(m_gamePatchPath, "layout.toc"))!.AsDict();
            byte[]? layeredInstallChunkFiles = layout.AsBlob("layeredInstallChunkFiles", null);
            if (layeredInstallChunkFiles is not null && m_installChunkWriters.Count > 0)
            {
                List<CasFileIdentifier> final;
                using (DataStream stream = new(new MemoryStream(layeredInstallChunkFiles)))
                {
                    final = new List<CasFileIdentifier>((int)(stream.Length / 8));
                    for (int i = 0; i < stream.Length / 8; i++)
                    {
                        final.Add(CasFileIdentifier.FromFileIdentifier(stream.ReadUInt64()));
                    }
                }

                foreach (InstallChunkWriter writer in m_installChunkWriters.Values)
                {
                    foreach (CasFileIdentifier file in writer.GetFiles())
                    {
                        final.Add(file);
                    }
                }

                final.Sort();

                using Block<byte> data = new(final.Count * 8);
                using (BlockStream stream = new(data, true))
                {
                    foreach (CasFileIdentifier file in final)
                    {
                        stream.WriteUInt64(CasFileIdentifier.ToFileIdentifierLong(file));
                    }
                }

                layout.Set("layeredInstallChunkFiles", data.ToArray());

                using (DataStream stream = new(File.Create(Path.Combine(m_modDataPath, "layout.toc"))))
                {
                    ObfuscationHeader.Write(stream);
                    DbObject.Serialize(stream, layout);
                }
            }
        }

        foreach (Block<byte> data in m_memoryData.Values)
        {
            data.Dispose();
        }
        foreach (InstallChunkWriter installChunkWriter in m_installChunkWriters.Values)
        {
            installChunkWriter.Dispose();
        }

        // create symbolic links for everything that is in gamePatchPath but not in modDataPath
        foreach (string file in Directory.EnumerateFiles(m_gamePatchPath, string.Empty, SearchOption.AllDirectories))
        {
            string modPath = Path.Combine(m_modDataPath, Path.GetRelativePath(m_gamePatchPath, file));
            if (!File.Exists(modPath))
            {
                Directory.CreateDirectory(Directory.GetParent(modPath)!.FullName);
                File.CreateSymbolicLink(modPath, file);
            }
        }

        // symlink shader_cache, else dx12 games will perform not as good
        string shaderCache = Path.Combine(FileSystemManager.BasePath, "shader_cache");
        if (Directory.Exists(shaderCache))
        {
            Directory.CreateSymbolicLink(Path.Combine(inModPackPath, "shader_cache"), shaderCache);
        }

        if (FileSystemManager.Sources.Count > 1)
        {
            // symlink all other sources
            foreach (FileSystemSource source in FileSystemManager.Sources)
            {
                if (source.Path != m_patchPath)
                {
                    string destPath = Path.Combine(inModPackPath, source.Path);
                    Directory.CreateDirectory(Directory.GetParent(destPath)!.FullName);
                    Directory.CreateSymbolicLink(destPath,
                        Path.Combine(FileSystemManager.BasePath, source.Path));
                }

                if (source.Path != FileSystemSource.Base.Path && source.Path.Contains("Data"))
                {
                    File.CreateSymbolicLink(
                        Path.Combine(inModPackPath, source.Path.Replace("/Data", string.Empty),
                            "package.mft"),
                        Path.Combine(FileSystemManager.BasePath, source.Path.Replace("/Data", string.Empty),
                            "package.mft"));
                }
            }
        }

        return Errors.Success;
    }

    private InstallChunkWriter GetInstallChunkWriter(SuperBundleInstallChunk inSbIc)
    {
        if (!m_installChunkWriters.TryGetValue(inSbIc.InstallChunk.Id, out InstallChunkWriter? installChunkWriter))
        {
            m_installChunkWriters.Add(inSbIc.InstallChunk.Id, installChunkWriter = new InstallChunkWriter(inSbIc.InstallChunk, m_gamePatchPath, m_modDataPath, m_patchPath == FileSystemSource.Patch.Path));
        }

        return installChunkWriter;
    }

    private void WriteCasArchives(SuperBundleModInfo inModInfo, InstallChunkWriter inInstallChunkWriter)
    {
        foreach (Sha1 sha1 in inModInfo.Data)
        {
            (Block<byte> Block, bool NeedsToDispose) data = GetData(sha1);
            inInstallChunkWriter.WriteData(sha1, data.Block);
            if (data.NeedsToDispose)
            {
                data.Block.Dispose();
            }
        }
    }

    private void LoadHandlers()
    {
        string handlersDir = Path.Combine(Frosty.Sdk.Utils.Utils.BaseDirectory, "Handlers");
        Directory.CreateDirectory(handlersDir);

        foreach (string handler in Directory.EnumerateFiles(handlersDir))
        {
            Assembly assembly = Assembly.Load(handler);
            foreach (Type type in assembly.ExportedTypes)
            {
                if (typeof(IHandler).IsAssignableFrom(type))
                {
                    HandlerAttribute? attribute = type.GetCustomAttribute<HandlerAttribute>();
                    if (attribute is null)
                    {
                        continue;
                    }
                    m_handlers.TryAdd(attribute.Hash, type);
                }
            }
        }
    }

    private void ProcessModResources(IResourceContainer container)
    {
        foreach (BaseModResource resource in container.Resources)
        {
            Sha1 sha1 = resource.Sha1;
            HashSet<int> modifiedBundles = new();
            switch (resource)
            {
                case BundleModResource bundle:
                {
                    SuperBundleModInfo sb = GetSuperBundleModInfo(bundle.SuperBundleHash);

                    sb.Added.Bundles.TryAdd(bundle.BundleHash, new BundleModInfo());
                    m_bundleToSuperBundleMapping.TryAdd(bundle.BundleHash, bundle.SuperBundleHash);
                    break;
                }
                case EbxModResource ebx:
                {
                    bool exists;
                    if ((exists = m_modifiedEbx.TryGetValue(resource.Name, out EbxModEntry? existing)) && !resource.HasHandler)
                    {
                        // asset was already modified by another mod so just skip to the bundle part
                        sha1 = existing!.Sha1;
                        break;
                    }

                    EbxModEntry modEntry;

                    if (resource.HasHandler)
                    {
                        if (!m_handlers.TryGetValue(resource.HandlerHash, out Type? type))
                        {
                            continue;
                        }

                        if (exists)
                        {
                            modEntry = existing!;
                            if (modEntry.Handler is null)
                            {
                                break;
                            }
                        }
                        else
                        {
                            modEntry = new EbxModEntry(ebx, -1)
                            {
                                Handler = (IHandler)Activator.CreateInstance(type)!
                            };
                            m_modifiedEbx.Add(resource.Name, modEntry);
                            m_handlerAssets.Add(modEntry);
                        }

                        modEntry.Handler.Load(container.GetData(resource.ResourceIndex).GetData());
                        break;
                    }

                    EbxAssetEntry? entry = AssetManager.GetEbxAssetEntry(resource.Name);

                    if (!resource.IsModified)
                    {
                        // asset needs to exist if it is not modified by the game
                        if (entry is null)
                        {
                            // we skip the bundle part here
                            continue;
                        }

                        // only add asset to bundles, use base games data
                        Block<byte> data = AssetManager.GetRawAsset(entry);
                        m_memoryData.TryAdd(entry.Sha1, data);
                        modEntry = new EbxModEntry(ebx, data.Size);
                        resource.Sha1 = entry.Sha1;
                    }
                    else
                    {
                        ResourceData data = container.GetData(resource.ResourceIndex);
                        m_data.TryAdd(resource.Sha1, data);
                        modEntry = new EbxModEntry(ebx, data.Size);

                        if (entry is not null)
                        {
                            // add in existing bundles
                            foreach (int bundle in entry.Bundles)
                            {
                                modifiedBundles.Add(bundle);
                            }
                        }
                    }
                    m_modifiedEbx.Add(resource.Name, modEntry);
                    break;
                }
                case ResModResource res:
                {
                    bool exists;
                    if ((exists = m_modifiedRes.TryGetValue(resource.Name, out ResModEntry? existing)) && !resource.HasHandler)
                    {
                        // asset was already modified by another mod so just skip to the bundle part
                        sha1 = existing!.Sha1;
                        break;
                    }

                    ResModEntry modEntry;

                    if (resource.HasHandler)
                    {
                        if (!m_handlers.TryGetValue(resource.HandlerHash, out Type? type))
                        {
                            continue;
                        }

                        if (exists)
                        {
                            modEntry = existing!;
                            if (modEntry.Handler is null)
                            {
                                break;
                            }
                        }
                        else
                        {
                            modEntry = new ResModEntry(res, -1)
                            {
                                Handler = (IHandler)Activator.CreateInstance(type)!
                            };
                            m_modifiedRes.Add(resource.Name, modEntry);
                            m_handlerAssets.Add(modEntry);
                        }

                        modEntry.Handler.Load(container.GetData(resource.ResourceIndex).GetData());
                        break;
                    }

                    ResAssetEntry? entry = AssetManager.GetResAssetEntry(resource.Name);

                    if (!resource.IsModified)
                    {
                        // asset needs to exist if it is not modified by the game
                        if (entry is null)
                        {
                            // we skip the bundle part here
                            continue;
                        }

                        // only add asset to bundles, use base games data
                        Block<byte> data = AssetManager.GetRawAsset(entry);
                        m_memoryData.TryAdd(entry.Sha1, data);
                        modEntry = new ResModEntry(res, data.Size);
                        resource.Sha1 = entry.Sha1;
                    }
                    else
                    {
                        ResourceData data = container.GetData(resource.ResourceIndex);
                        m_data.TryAdd(resource.Sha1, data);
                        modEntry = new ResModEntry(res, data.Size);

                        if (entry is not null)
                        {
                            // add in existing bundles
                            foreach (int bundle in entry.Bundles)
                            {
                                modifiedBundles.Add(bundle);
                            }
                        }
                    }
                    m_modifiedRes.Add(resource.Name, modEntry);
                    break;
                }
                case ChunkModResource chunk:
                {
                    Guid id = Guid.Parse(resource.Name);
                    bool exists;
                    if ((exists = m_modifiedChunks.TryGetValue(id, out ChunkModEntry? existing)) && !resource.HasHandler)
                    {
                        // asset was already modified by another mod so just skip to the bundle part
                        sha1 = existing!.Sha1;
                        break;
                    }

                    ChunkModEntry modEntry;

                    if (resource.HasHandler)
                    {
                        if (!m_handlers.TryGetValue(resource.HandlerHash, out Type? type))
                        {
                            continue;
                        }

                        if (exists)
                        {
                            modEntry = existing!;
                            if (modEntry.Handler is null)
                            {
                                break;
                            }
                        }
                        else
                        {
                            modEntry = new ChunkModEntry(chunk, -1)
                            {
                                Handler = (IHandler)Activator.CreateInstance(type)!
                            };
                            m_modifiedChunks.Add(id, modEntry);
                            m_handlerAssets.Add(modEntry);
                        }

                        modEntry.Handler.Load(container.GetData(resource.ResourceIndex).GetData());
                        break;
                    }

                    ChunkAssetEntry? entry = AssetManager.GetChunkAssetEntry(id);

                    if (!resource.IsModified)
                    {
                        // asset needs to exist if it is not modified by the game
                        if (entry is null)
                        {
                            // we skip the bundle part here
                            continue;
                        }

                        // only add asset to bundles, use base games data
                        Block<byte> data = AssetManager.GetRawAsset(entry);
                        m_memoryData.TryAdd(entry.Sha1, data);
                        modEntry = new ChunkModEntry(chunk, data.Size);
                        resource.Sha1 = entry.Sha1;
                    }
                    else
                    {
                        ResourceData data = container.GetData(resource.ResourceIndex);
                        m_data.TryAdd(resource.Sha1, data);
                        modEntry = new ChunkModEntry(chunk, data.Size);

                        if (entry is not null)
                        {
                            // add in existing bundles
                            foreach (int bundle in entry.Bundles)
                            {
                                modifiedBundles.Add(bundle);
                            }

                            foreach (int superBundle in entry.SuperBundleInstallChunks)
                            {
                                SuperBundleModInfo sb = GetSuperBundleModInfo(superBundle);
                                sb.Data.Add(resource.Sha1);
                                sb.Modified.Chunks.Add(id);
                            }
                        }
                    }

                    foreach (int superBundle in chunk.AddedSuperBundles)
                    {
                        SuperBundleModInfo sb = GetSuperBundleModInfo(superBundle);
                        sb.Data.Add(resource.Sha1);
                        sb.Added.Chunks.Add(id);
                    }

                    foreach (int superBundle in chunk.RemovedSuperBundles)
                    {
                        SuperBundleModInfo sb = GetSuperBundleModInfo(superBundle);
                        sb.Removed.Chunks.Add(id);
                    }
                    m_modifiedChunks.Add(id, modEntry);
                    break;
                }
                case FsFileModResource:
                {
                    // TODO:
                    break;
                }
                default:
                    continue;
            }

            foreach (int addedBundle in resource.AddedBundles)
            {
                SuperBundleModInfo sb = GetSuperBundleModInfoFromBundle(addedBundle);

                if (sha1 != Sha1.Zero)
                {
                    sb.Data.Add(sha1);
                }

                if (!sb.Modified.Bundles.TryGetValue(addedBundle, out BundleModInfo? modInfo))
                {
                    modInfo = new BundleModInfo();
                    sb.Modified.Bundles.Add(addedBundle, modInfo);
                }

                switch (resource.Type)
                {
                    case ModResourceType.Ebx:
                        modInfo.Added.Ebx.Add(resource.Name);
                        break;
                    case ModResourceType.Res:
                        modInfo.Added.Res.Add(resource.Name);
                        break;
                    case ModResourceType.Chunk:
                        modInfo.Added.Chunks.Add(Guid.Parse(resource.Name));
                        break;
                }
            }

            foreach (int removedBundle in resource.RemovedBundles)
            {
                SuperBundleModInfo sb = GetSuperBundleModInfoFromBundle(removedBundle);

                if (!sb.Modified.Bundles.TryGetValue(removedBundle, out BundleModInfo? modInfo))
                {
                    modInfo = new BundleModInfo();
                    sb.Modified.Bundles.Add(removedBundle, modInfo);
                }

                switch (resource.Type)
                {
                    case ModResourceType.Ebx:
                        modInfo.Removed.Ebx.Add(resource.Name);
                        break;
                    case ModResourceType.Res:
                        modInfo.Removed.Res.Add(resource.Name);
                        break;
                    case ModResourceType.Chunk:
                        modInfo.Removed.Chunks.Add(Guid.Parse(resource.Name));
                        break;
                }
            }

            foreach (int modifiedBundle in modifiedBundles)
            {
                SuperBundleModInfo sb = GetSuperBundleModInfoFromBundle(modifiedBundle);

                if (resource.Sha1 != Sha1.Zero)
                {
                    sb.Data.Add(resource.Sha1);
                }

                if (!sb.Modified.Bundles.TryGetValue(modifiedBundle, out BundleModInfo? modInfo))
                {
                    modInfo = new BundleModInfo();
                    sb.Modified.Bundles.Add(modifiedBundle, modInfo);
                }

                switch (resource.Type)
                {
                    case ModResourceType.Ebx:
                        modInfo.Modified.Ebx.Add(resource.Name);
                        break;
                    case ModResourceType.Res:
                        modInfo.Modified.Res.Add(resource.Name);
                        break;
                    case ModResourceType.Chunk:
                        modInfo.Modified.Chunks.Add(Guid.Parse(resource.Name));
                        break;
                }
            }
        }
    }

    private SuperBundleModInfo GetSuperBundleModInfoFromBundle(int inBundle)
    {
        BundleInfo? bundle = AssetManager.GetBundleInfo(inBundle);
        int superBundle;
        if (bundle is null)
        {
            if (!m_bundleToSuperBundleMapping.TryGetValue(inBundle, out superBundle))
            {
                // change this to a Error at some point
                throw new Exception("Asset was added to Bundle, which doesnt exist.");
            }
        }
        else
        {
            superBundle = bundle.Parent.Id;
        }

        return GetSuperBundleModInfo(superBundle);
    }

    private SuperBundleModInfo GetSuperBundleModInfo(int superBundle)
    {
        if (!m_superBundleModInfos.TryGetValue(superBundle, out SuperBundleModInfo? sb))
        {
            sb = new SuperBundleModInfo();
            m_superBundleModInfos.Add(superBundle, sb);
        }

        return sb;
    }

    private static List<ModInfo> GenerateModInfoList(IEnumerable<string> modPaths)
    {
        List<ModInfo> modInfoList = new();

        foreach (string path in modPaths)
        {
            FrostyModDetails? modDetails;

            string extension = Path.GetExtension(path);
            if (extension == ".fbmod")
            {
                modDetails = FrostyMod.GetModDetails(path);
            }
            else if (extension == ".fbcollection")
            {
                modDetails = FrostyModCollection.GetModDetails(path);
            }
            else
            {
                continue;
            }

            if (modDetails is null)
            {
                return modInfoList;
            }

            ModInfo modInfo = new()
            {
                Path = path,
                Name = modDetails.Title,
                Version = modDetails.Version,
                Category = modDetails.Category,
                Link = modDetails.ModPageLink,
                FileName = path
            };

            modInfoList.Add(modInfo);
        }
        return modInfoList;
    }

    private (Block<byte> Block, bool NeedsToDispose) GetData(Sha1 sha1)
    {
        if (m_data.TryGetValue(sha1, out ResourceData? data))
        {
            return (data.GetData(), true);
        }

        if (m_memoryData.TryGetValue(sha1, out Block<byte>? block))
        {
            return (block, false);
        }

        throw new Exception();
    }
}