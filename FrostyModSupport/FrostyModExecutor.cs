using System.Text.Json;
using Frosty.ModSupport.Interfaces;
using Frosty.ModSupport.Mod;
using Frosty.ModSupport.Mod.Resources;
using Frosty.ModSupport.ModEntries;
using Frosty.ModSupport.ModInfos;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos;

namespace Frosty.ModSupport;

public class FrostyModExecutor
{
    private Dictionary<string, EbxModEntry> m_modifiedEbx = new();
    private Dictionary<string, ResModEntry> m_modifiedRes = new();
    private Dictionary<Guid, ChunkModEntry> m_modifiedChunks = new();

    private Dictionary<string, SuperBundleModInfo> m_superBundleModInfos = new();
    private Dictionary<int, string> m_mapping = new();
    
    /// <summary>
    /// Generates a directory containing the modded games data.
    /// </summary>
    /// <param name="modPackName">The name of the directory where the data is stored in the games ModData folder.</param>
    /// <param name="modPaths">The full paths of the mods.</param>
    public void GenerateMods(string modPackName, params string[] modPaths)
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
                return;
            }
        }

        // make sure the managers are initialized
        ResourceManager.Initialize();
        AssetManager.Initialize();

        // create bundle lookup map
        GenerateBundleLookup();
        
        // process all mods
        foreach (string path in modPaths)
        {
            string extension = Path.GetExtension(path);
            if (extension == ".fbmod")
            {
                FrostyMod mod = FrostyMod.Load(path);
                ProcessModResources(mod);
            }
            else if (extension == ".fbcollection")
            {
                FrostyModCollection modCollection = FrostyModCollection.Load(path);
                ProcessModResources(modCollection);
            }
            else
            {
                throw new Exception();
            }
        }
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
                case EbxModResource:
                {
                    if (resource.IsModified || !m_modifiedEbx.ContainsKey(resource.Name))
                    {
                        if (resource.HasHandler)
                        {
                            
                        }
                        else
                        {
                            if (m_modifiedEbx.TryGetValue(resource.Name, out EbxModEntry? existingEntry))
                            {
                                if (existingEntry.Sha1 == resource.Sha1 /*|| has handler*/)
                                {
                                    break;
                                }

                                m_modifiedEbx.Remove(resource.Name, out _);
                            }

                            // TODO: create EbxModEntry from resource

                            EbxAssetEntry? ebxEntry = AssetManager.GetEbxAssetEntry(resource.Name);

                            if (resource.ResourceIndex == -1)
                            {
                                // only add asset to bundles, use base games data
                            }
                            else if (ebxEntry is not null)
                            {
                                // add in existing bundles
                                foreach (int bundle in ebxEntry.Bundles)
                                {
                                    modifiedBundles.Add(bundle);
                                }
                            }
                        }
                    }
                }
                    break;
                case ResModResource:
                    break;
                case ChunkModResource:
                    break;
                case FsFileModResource:
                    break;
            }

            foreach (int addedBundle in resource.AddedBundles)
            {
                SuperBundleModInfo sb = m_superBundleModInfos[m_mapping[addedBundle]];

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
                SuperBundleModInfo sb = m_superBundleModInfos[m_mapping[removedBundle]];

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
                SuperBundleModInfo sb = m_superBundleModInfos[m_mapping[modifiedBundle]];

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
            FrostyModDetails modDetails;

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