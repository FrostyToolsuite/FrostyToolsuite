using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Frosty.ModSupport.Attributes;
using Frosty.ModSupport.Interfaces;
using Frosty.ModSupport.Mod;
using Frosty.ModSupport.Mod.Resources;
using Frosty.ModSupport.ModEntries;
using Frosty.ModSupport.ModInfos;
using Frosty.Sdk;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Utils;

namespace Frosty.ModSupport;

public partial class FrostyModExecutor
{
    private readonly Dictionary<string, EbxModEntry> m_modifiedEbx = new();
    private readonly Dictionary<string, ResModEntry> m_modifiedRes = new();
    private readonly Dictionary<Guid, ChunkModEntry> m_modifiedChunks = new();

    private readonly List<IModEntry> m_handlerAssets = new();

    private readonly Dictionary<Sha1, ResourceData> m_data = new();
    private readonly Dictionary<Sha1, Block<byte>> m_data2 = new();

    private readonly Dictionary<int, SuperBundleModInfo> m_superBundleModInfos = new();
    private readonly Dictionary<int, int> m_bundleToSuperBundleMapping = new();

    private readonly Dictionary<int, Type> m_handlers = new();
    
    /// <summary>
    /// Generates a directory containing the modded games data.
    /// </summary>
    /// <param name="modPackName">The name of the directory where the data is stored in the games ModData folder.</param>
    /// <param name="modPaths">The full paths of the mods.</param>
    public Errors GenerateMods(string modPackName, params string[] modPaths)
    {
        // define some paths we are going to need
        string patchPath = FileSystemManager.Sources.Count == 1
            ? FileSystemSource.Base.Path
            : FileSystemSource.Patch.Path;
        string modDataPath = Path.Combine(FileSystemManager.BasePath, "ModData", modPackName, patchPath);
        string gamePatchPath = Path.Combine(FileSystemManager.BasePath, patchPath);

        // check if we need to generate new data
        string modInfosPath = Path.Combine(modDataPath, "mods.json");
        List<ModInfo> modInfos = GenerateModInfoList(modPaths);
        if (File.Exists(modInfosPath))
        {
            List<ModInfo>? oldModInfos = JsonSerializer.Deserialize<List<ModInfo>>(File.ReadAllText(modInfosPath));
            if (oldModInfos?.SequenceEqual(modInfos) == true)
            {
                return Errors.NoUpdateNeeded;
            }
        }

        // make sure the managers are initialized
        ResourceManager.Initialize();
        AssetManager.Initialize();

        // load handlers from Handlers directory
        LoadHandlers();
        
        // process all mods
        foreach (string path in modPaths)
        {
            string extension = Path.GetExtension(path);
            if (extension == ".fbmod")
            {
                FrostyMod? mod = FrostyMod.Load(path);
                if (mod is null)
                {
                    return Errors.InvalidMods;
                }
                if (mod.Head != FileSystemManager.Head)
                {
                    // TODO: print warning
                }
                ProcessModResources(mod);
            }
            else if (extension == ".fbcollection")
            {
                FrostyModCollection? modCollection = FrostyModCollection.Load(path);
                if (modCollection is null)
                {
                    return Errors.InvalidMods;
                }

                foreach (FrostyMod mod in modCollection.Mods)
                {
                    if (mod.Head != FileSystemManager.Head)
                    {
                        // TODO: print warning
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
            Debug.Assert(m_data2.TryAdd(entry.Sha1, data));
        }
        
        // clear old generated mod data
        Directory.Delete(modDataPath, true);
        Directory.CreateDirectory(modDataPath);
        
        // modify the superbundles and write them to mod data
        foreach (KeyValuePair<int, SuperBundleModInfo> sb in m_superBundleModInfos)
        {
            SuperBundleInstallChunk sbic = FileSystemManager.GetSuperBundleInstallChunk(sb.Key);
            
            // TODO: we need to write the data to cas files (should we write them per bundle or per superbundle) and store the references, so we can write them into the cat files/directly in the bundle
            switch (FileSystemManager.BundleFormat)
            {
                case BundleFormat.Dynamic2018:
                    break;
                case BundleFormat.Manifest2019:
                    break;
                case BundleFormat.SuperBundleManifest:
                    break;
                case BundleFormat.Kelvin:
                    break;
            }
        }
        
        // create symbolic links for everything that is in gamePatchPath but not in modDataPath
        foreach (string file in Directory.EnumerateFiles(gamePatchPath, string.Empty, SearchOption.AllDirectories))
        {
            string modPath = Path.Combine(modDataPath, Path.GetRelativePath(gamePatchPath, file));
            if (!File.Exists(modPath))
            {
                // TODO: check if we need to create the directory first
                File.CreateSymbolicLink(modPath, file);
            }
        }

        return Errors.Success;
    }

    private void LoadHandlers()
    {
        foreach (string handler in Directory.EnumerateFiles("Handlers"))
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
            HashSet<int> modifiedBundles = new();
            switch (resource)
            {
                case BundleModResource bundle:
                {
                    SuperBundleModInfo sb = GetSuperBundleModInfo(bundle.SuperBundleHash);

                    int bundleHash = Utils.HashString(bundle.Name + FileSystemManager.GetSuperBundleInstallChunk(bundle.SuperBundleHash).Name, true);
                    sb.Added.Bundles.TryAdd(bundleHash, new BundleModInfo());
                    m_bundleToSuperBundleMapping.TryAdd(bundleHash, bundle.SuperBundleHash);
                    break;
                }
                case EbxModResource ebx:
                {
                    bool exists;
                    if ((exists = m_modifiedEbx.ContainsKey(resource.Name)) && !resource.HasHandler)
                    {
                        // asset was already modified by another mod so just skip to the bundle part
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
                            modEntry = m_modifiedEbx[resource.Name];
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
                        Debug.Assert(m_data2.TryAdd(entry.Sha1, data));
                        modEntry = new EbxModEntry(ebx, data.Size);
                    }
                    else
                    {
                        ResourceData data = container.GetData(resource.ResourceIndex);
                        Debug.Assert(m_data.TryAdd(resource.Sha1, data));
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
                    if ((exists = m_modifiedRes.ContainsKey(resource.Name)) && !resource.HasHandler)
                    {
                        // asset was already modified by another mod so just skip to the bundle part
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
                            modEntry = m_modifiedRes[resource.Name];
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
                        Debug.Assert(m_data2.TryAdd(entry.Sha1, data));
                        modEntry = new ResModEntry(res, data.Size);
                    }
                    else
                    {
                        ResourceData data = container.GetData(resource.ResourceIndex);
                        Debug.Assert(m_data.TryAdd(resource.Sha1, data));
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
                    if ((exists = m_modifiedChunks.ContainsKey(id)) && !resource.HasHandler)
                    {
                        // asset was already modified by another mod so just skip to the bundle part
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
                            modEntry = m_modifiedChunks[id];
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
                        Debug.Assert(m_data2.TryAdd(entry.Sha1, data));
                        modEntry = new ChunkModEntry(chunk, data.Size);
                    }
                    else
                    {
                        ResourceData data = container.GetData(resource.ResourceIndex);
                        Debug.Assert(m_data.TryAdd(resource.Sha1, data));
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
                                sb.Modified.Chunks.Add(id);
                            }
                        }
                    }

                    foreach (int superBundle in chunk.AddedSuperBundles)
                    {
                        SuperBundleModInfo sb = GetSuperBundleModInfo(superBundle);
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
            superBundle = Utils.HashString(bundle.Parent.Name);
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

    private Block<byte> GetData(Sha1 sha1)
    {
        if (m_data.TryGetValue(sha1, out ResourceData? data))
        {
            return data.GetData();
        }

        if (m_data2.TryGetValue(sha1, out Block<byte>? block))
        {
            return block;
        }

        throw new Exception();
    }
}