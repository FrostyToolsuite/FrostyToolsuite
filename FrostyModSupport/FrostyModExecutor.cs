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

namespace Frosty.ModSupport;

public class FrostyModExecutor
{
    private Dictionary<string, EbxModEntry> m_modifiedEbx = new();
    private Dictionary<string, ResModEntry> m_modifiedRes = new();
    private Dictionary<Guid, ChunkModEntry> m_modifiedChunks = new();

    private Dictionary<Sha1, FrostyMod.ResourceData> m_data = new();

    private Dictionary<string, SuperBundleModInfo> m_superBundleModInfos = new();
    private Dictionary<int, string> m_mapping = new();

    private Dictionary<int, Type> m_handlers = new();
    
    /// <summary>
    /// Generates a directory containing the modded games data.
    /// </summary>
    /// <param name="modPackName">The name of the directory where the data is stored in the games ModData folder.</param>
    /// <param name="modPaths">The full paths of the mods.</param>
    public Errors GenerateMods(string modPackName, params string[] modPaths)
    {
        string modDataPath = Path.Combine(FileSystemManager.BasePath, "ModData", modPackName);
        string patchPath = FileSystemManager.Sources.Count == 1
            ? FileSystemSource.Base.Path
            : FileSystemSource.Patch.Path;

        // check if we need to generate new data
        string modInfosPath = Path.Combine(modDataPath, patchPath, "mods.json");
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

        LoadHandlers();

        // create bundle lookup map
        GenerateBundleLookup();
        
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
            else
            {
                return Errors.InvalidMods;
            }
        }

        foreach (KeyValuePair<string, SuperBundleModInfo> sb in m_superBundleModInfos)
        {
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

        return Errors.Success;
    }

    private void LoadHandlers()
    {
        foreach (string handler in Directory.EnumerateFiles("Handlers"))
        {
            Assembly assembly = Assembly.Load(handler);
            foreach (Type type in assembly.ExportedTypes)
            {
                if (type.IsSubclassOf(typeof(IHandler)))
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
        throw new NotImplementedException();
    }

    private void GenerateBundleLookup()
    {
        foreach (SuperBundleInfo sb in FileSystemManager.EnumerateSuperBundles())
        {
            foreach (SuperBundleInstallChunk sbIc in sb.InstallChunks)
            {
                foreach (KeyValuePair<string, BundleInfo> bundle in sbIc.BundleMapping)
                {
                    m_mapping.Add(bundle.Value.Id, sbIc.Name);
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
                case BundleModResource:
                    break;
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
                            break;
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
                            modEntry = new EbxModEntry(ebx, -1);
                            modEntry.Handler = (IHandler)Activator.CreateInstance(type)!;
                        }
                        
                        modEntry.Handler.Load(container.GetData(resource.ResourceIndex).GetData());
                        break;
                    }

                    EbxAssetEntry? entry = AssetManager.GetEbxAssetEntry(resource.Name);

                    if (resource.IsModified)
                    {
                        // only add asset to bundles, use base games data
                        // TODO: get data from base game
                        modEntry = new EbxModEntry(ebx, -1);
                    }
                    else
                    {
                        FrostyMod.ResourceData data = container.GetData(resource.ResourceIndex);
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
                            modEntry = new ResModEntry(res, -1);
                            // TODO: Get handler for asset
                            // modEntry.Handler = ;
                        }
                        
                        modEntry.Handler.Load(container.GetData(resource.ResourceIndex).GetData());
                        break;
                    }

                    ResAssetEntry? entry = AssetManager.GetResAssetEntry(resource.Name);

                    if (resource.IsModified)
                    {
                        // only add asset to bundles, use base games data
                        // TODO: get data from base game
                        modEntry = new ResModEntry(res, -1);
                    }
                    else
                    {
                        FrostyMod.ResourceData data = container.GetData(resource.ResourceIndex);
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
                            modEntry = new ChunkModEntry(chunk, -1);
                            // TODO: Get handler for asset
                            // modEntry.Handler = ;
                        }
                        
                        modEntry.Handler.Load(container.GetData(resource.ResourceIndex).GetData());
                        break;
                    }

                    ChunkAssetEntry? entry = AssetManager.GetChunkAssetEntry(id);

                    if (resource.IsModified)
                    {
                        // only add asset to bundles, use base games data
                        // TODO: get data from base game
                        modEntry = new ChunkModEntry(chunk, -1);
                    }
                    else
                    {
                        FrostyMod.ResourceData data = container.GetData(resource.ResourceIndex);
                        Debug.Assert(m_data.TryAdd(resource.Sha1, data));
                        modEntry = new ChunkModEntry(chunk, data.Size);
                            
                        if (entry is not null)
                        {
                            // add in existing bundles
                            foreach (int bundle in entry.Bundles)
                            {
                                modifiedBundles.Add(bundle);
                            }   
                        }
                    }
                        
                    m_modifiedChunks.Add(id, modEntry);
                    break;
                }
                case FsFileModResource:
                    break;
            }

            foreach (int addedBundle in resource.AddedBundles)
            {
                string sbName = m_mapping[addedBundle];
                if (!m_superBundleModInfos.TryGetValue(sbName, out SuperBundleModInfo? sb))
                {
                    sb = new SuperBundleModInfo();
                    m_superBundleModInfos.Add(sbName, sb);
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
                string sbName = m_mapping[removedBundle];
                if (!m_superBundleModInfos.TryGetValue(sbName, out SuperBundleModInfo? sb))
                {
                    sb = new SuperBundleModInfo();
                    m_superBundleModInfos.Add(sbName, sb);
                }

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
                string sbName = m_mapping[modifiedBundle];
                if (!m_superBundleModInfos.TryGetValue(sbName, out SuperBundleModInfo? sb))
                {
                    sb = new SuperBundleModInfo();
                    m_superBundleModInfos.Add(sbName, sb);
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
                throw new Exception();
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
}