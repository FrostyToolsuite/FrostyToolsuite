using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Frosty.Sdk.DbObjectElements;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Managers.Infos.FileInfos;

namespace Frosty.Sdk.Managers.Loaders;

public class ManifestAssetLoader : IAssetLoader
{
    public void Load()
    {
        // This format has all SuperBundles stripped
        // all of the bundles and chunks of all SuperBundles are put into the manifest
        // afaik u cant reconstruct the SuperBundles, so this might make things a bit ugly
        // They also have catalog files which entries are not used, but they still make a sanity check for the offsets and indices in the file

        DbObjectDict manifest = FileSystemManager.SuperBundleManifest!;

        CasFileIdentifier file = CasFileIdentifier.FromManifestFileIdentifier(manifest.AsUInt("file"));

        string path = FileSystemManager.GetFilePath(file);

        using (BlockStream stream = BlockStream.FromFile(path, manifest.AsUInt("offset"), manifest.AsInt("size")))
        {
            uint resourceInfoCount = stream.ReadUInt32();
            uint bundleCount = stream.ReadUInt32();
            uint chunkCount = stream.ReadUInt32();

            (CasFileIdentifier, uint, long)[] files = new (CasFileIdentifier, uint, long)[resourceInfoCount];

            // resource infos
            for (int i = 0; i < resourceInfoCount; i++)
            {
                files[i] = (CasFileIdentifier.FromManifestFileIdentifier(stream.ReadUInt32()), stream.ReadUInt32(),
                    (uint)stream.ReadInt64());
            }

            Dictionary<int, HashSet<int>> mapping = new();

            // bundles
            for (int i = 0; i < bundleCount; i++)
            {
                int nameHash = stream.ReadInt32();
                int startIndex = stream.ReadInt32();
                int resourceCount = stream.ReadInt32();

                // unknown, always 0
                stream.Position += sizeof(ulong);

                (CasFileIdentifier, uint, long) resourceInfo = files[startIndex];

                // we use the installChunk of the bundle to get a superBundle and SuperBundleInstallChunk
                InstallChunkInfo ic = FileSystemManager.GetInstallChunkInfo(resourceInfo.Item1.InstallChunkIndex);
                string superbundle = ic.SuperBundles.FirstOrDefault() ?? string.Empty;
                Debug.Assert(!string.IsNullOrEmpty(superbundle), "no super bundle found for install chunk");
                // hack we just assume there are no splitSuperBundles
                SuperBundleInstallChunk sbIc = FileSystemManager.GetSuperBundleInstallChunk(superbundle);

                BinaryBundle bundleMeta;
                using (BlockStream bundleStream = BlockStream.FromFile(
                           FileSystemManager.GetFilePath(resourceInfo.Item1), resourceInfo.Item2,
                           (int)resourceInfo.Item3))
                {
                     bundleMeta = BinaryBundle.Deserialize(bundleStream);
                }

                // get name since they are hashed
                if (!ProfilesLibrary.SharedBundles.TryGetValue(nameHash, out string? name))
                {
                    foreach (EbxAssetEntry ebx in bundleMeta.EbxList)
                    {
                        // blueprint and sublevel bundles always have an ebx with the same name
                        string potentialName = ebx.Name.StartsWith(FileSystemManager.GamePlatform.ToString(), StringComparison.OrdinalIgnoreCase) ? ebx.Name : $"{FileSystemManager.GamePlatform}/{ebx.Name}";
                        int hash = Utils.Utils.HashString(potentialName, true);
                        if (nameHash == hash)
                        {
                            name = potentialName;
                            break;
                        }
                    }
                }

                // if we couldn't get a name just use the nameHash for now when indexing ebx the ui stuff will assign those
                if (string.IsNullOrEmpty(name))
                {
                    name = nameHash.ToString("X8");
                }

                BundleInfo bundle = AssetManager.AddBundle(name, sbIc);

                // load the assets
                // we use the file infos from the catalogs, since its easier even if they are not used by the game
                foreach (EbxAssetEntry ebx in bundleMeta.EbxList)
                {
                    ebx.AddFileInfo(ResourceManager.GetFileInfo(ebx.Sha1));

                    AssetManager.AddEbx(ebx, bundle.Id);
                }

                foreach (ResAssetEntry res in bundleMeta.ResList)
                {
                    res.AddFileInfo(ResourceManager.GetFileInfo(res.Sha1));

                    AssetManager.AddRes(res, bundle.Id);
                }

                foreach (ChunkAssetEntry chunk in bundleMeta.ChunkList)
                {
                    chunk.AddFileInfo(ResourceManager.GetFileInfo(chunk.Sha1));

                    AssetManager.AddChunk(chunk, bundle.Id);
                }
            }

            // chunks
            for (int i = 0; i < chunkCount; i++)
            {
                Guid chunkId = stream.ReadGuid();
                (CasFileIdentifier, uint, long) resourceInfo = files[stream.ReadInt32()];

                InstallChunkInfo ic = FileSystemManager.GetInstallChunkInfo(resourceInfo.Item1.InstallChunkIndex);
                string superbundle = ic.SuperBundles.FirstOrDefault() ?? string.Empty;
                Debug.Assert(!string.IsNullOrEmpty(superbundle), "no super bundle found for install chunk");
                // hack we just assume there are no splitSuperBundles
                SuperBundleInstallChunk sbIc = FileSystemManager.GetSuperBundleInstallChunk(superbundle);

                ChunkAssetEntry entry = new(chunkId, Sha1.Zero, 0, (uint)resourceInfo.Item3, Utils.Utils.HashString(sbIc.Name, true));

                entry.AddFileInfo(
                    new CasFileInfo(resourceInfo.Item1, resourceInfo.Item2, (uint)resourceInfo.Item3, 0));

                AssetManager.AddSuperBundleChunk(entry);
            }
        }
    }
}