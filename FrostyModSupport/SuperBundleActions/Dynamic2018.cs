using System.Diagnostics;
using Frosty.ModSupport.Archive;
using Frosty.ModSupport.ModEntries;
using Frosty.ModSupport.ModInfos;
using Frosty.Sdk;
using Frosty.Sdk.DbObjectElements;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Utils;

namespace Frosty.ModSupport;

internal class Dynamic2018 : IDisposable
{
    public Block<byte>? TocData { get; private set; }
    public Block<byte>? SbData { get; private set; }

    private readonly Dictionary<string, EbxModEntry> m_modifiedEbx;
    private readonly Dictionary<string, ResModEntry> m_modifiedRes;
    private readonly Dictionary<Guid, ChunkModEntry> m_modifiedChunks;

    public Dynamic2018(Dictionary<string, EbxModEntry> inModifiedEbx,
        Dictionary<string, ResModEntry> inModifiedRes, Dictionary<Guid, ChunkModEntry> inModifiedChunks)
    {
        m_modifiedEbx = inModifiedEbx;
        m_modifiedRes = inModifiedRes;
        m_modifiedChunks = inModifiedChunks;
    }

    public void ModSuperBundle(string inPath, bool inCreateNewPatch, SuperBundleInstallChunk inSbIc,
        SuperBundleModInfo inModInfo, InstallChunkWriter inInstallChunkWriter)
    {
        DbObjectDict toc = DbObject.CreateDict(5);

        Block<byte> modifiedSuperBundle = new(0);
        using (BlockStream modifiedStream = new(modifiedSuperBundle, true))
        {
            ProcessBundles(inPath, inCreateNewPatch, inSbIc, inModInfo, inInstallChunkWriter, modifiedStream, toc);

            // if we modify some bundles that are not in the patch we need to parse the base superbundle as well
            if (inModInfo.Modified.Bundles.Count > 0 || inModInfo.Modified.Chunks.Count > 0)
            {
                string basePath = FileSystemManager.ResolvePath(false, $"{inSbIc.Name}.toc");
                ProcessBundles(basePath, true, inSbIc, inModInfo, inInstallChunkWriter, modifiedStream, toc);
            }

            Debug.Assert(inModInfo.Modified.Bundles.Count == 0 && inModInfo.Modified.Chunks.Count == 0);
        }

        // TODO: how to get the offset in the .sb file correct since we have the 7bit encoded int
        SbData = new(modifiedSuperBundle.Size + 4);
        using (BlockStream stream = new(SbData, true))
        {
            // write anonymous dict type
            stream.WriteByte(2 | (1 << 7));
            stream.Write7BitEncodedInt64(modifiedSuperBundle.Size);
            long offset = stream.Position;
            stream.Write(modifiedSuperBundle);
            modifiedSuperBundle.Dispose();
            stream.WriteByte(0);
        }

        TocData = new Block<byte>(0);

    }

    private void ProcessBundles(string inPath, bool inOnlyUseModified, SuperBundleInstallChunk inSbIc,
        SuperBundleModInfo inModInfo, InstallChunkWriter inInstallChunkWriter, BlockStream inModifiedStream, DbObjectDict inToc)
    {
        DbObjectDict? toc = DbObject.Deserialize(inPath)?.AsDict();
        if (toc is null)
        {
            Debug.Assert(false, "We should not be here");
            return;
        }

        // check for format flags
        bool isCas = toc.AsBoolean("cas");
        bool isDas = toc.AsBoolean("das");

        if (isCas)
        {
            inToc.Set("cas", true);
        }

        // load bundles
        if (toc.ContainsKey("bundles"))
        {
            if (isDas)
            {
                throw new NotImplementedException();
            }
            else
            {
                DbObjectList bundles = toc.AsList("bundles");
                if (!inToc.ContainsKey("bundles"))
                {
                    inToc.Set("bundles",
                        DbObject.CreateList("bundles",
                            inOnlyUseModified
                                ? inModInfo.Modified.Bundles.Count + inModInfo.Added.Bundles.Count
                                : bundles.Count + inModInfo.Added.Bundles.Count));
                }
                BlockStream? sbStream = null;
                foreach (DbObject obj in bundles)
                {
                    DbObjectDict bundleObj = obj.AsDict();

                    string bundleName = bundleObj.AsString("id");
                    int id = Frosty.Sdk.Utils.Utils.HashString(bundleName + inSbIc.Name, true);

                    long offset = bundleObj.AsLong("offset");
                    long size = bundleObj.AsLong("size");

                    bool isDelta = bundleObj.AsBoolean("delta");
                    bool isBase = bundleObj.AsBoolean("base");

                    long newOffset;
                    long newBundleSize;
                    bool needsOldBaseFlag = false;

                    if (!inModInfo.Modified.Bundles.TryGetValue(id, out BundleModInfo? bundleModInfo))
                    {
                        if (inOnlyUseModified)
                        {
                            // we create a new patch to a base superbundle, so we only need modified bundles
                            continue;
                        }

                        if (!isCas)
                        {
                            throw new NotImplementedException();
                        }

                        // load and write unmodified bundle
                        newOffset = inModifiedStream.Position;
                        newBundleSize = size;
                        if (!isBase)
                        {
                            sbStream ??= BlockStream.FromFile(inPath.Replace(".toc", ".sb"), false);
                            sbStream.Position = offset;
                            sbStream.CopyTo(inModifiedStream, (int)size);
                        }
                        else
                        {
                            needsOldBaseFlag = true;
                        }
                    }
                    else
                    {
                        // load, modify and write bundle
                        newOffset = inModifiedStream.Position;
                        sbStream ??= BlockStream.FromFile(inPath.Replace(".toc", ".sb"), false);
                        sbStream.Position = offset;
                        if (isCas)
                        {
                            DbObjectDict? bundle = DbObject.Deserialize(sbStream)?.AsDict();

                            if (bundle is null)
                            {
                                FrostyLogger.Logger?.LogError("Trying to mod bundle that is not valid.");
                                continue;
                            }

                            // modify bundle assets
                            DbObjectList? ebxList = bundle.AsList("ebx", null);
                            DbObjectList? resList = bundle.AsList("res", null);
                            DbObjectList? chunkList = bundle.AsList("chunks", null);
                            DbObjectList? chunkMetaList = bundle.AsList("chunkMeta", null);

                            long ebxBundleSize = 0, resBundleSize = 0, chunkBundleSize = 0;

                            for (int i = 0; i < ebxList?.Count; i++)
                            {
                                DbObjectDict ebx = ebxList[i].AsDict();

                                string name = ebx.AsString("name");

                                if (!bundleModInfo.Modified.Ebx.Contains(name))
                                {
                                    ebxBundleSize += ebx.AsLong("size");
                                    continue;
                                }

                                EbxModEntry modEntry = m_modifiedEbx[name];

                                ebx = DbObject.CreateDict(4);
                                ebx.Set("name", name);
                                ebx.Set("sha1", modEntry.Sha1);
                                ebx.Set("size", modEntry.Size);
                                ebx.Set("originalSize", modEntry.OriginalSize);
                                ebxBundleSize += modEntry.Size;

                                ebxList[i] = ebx;
                            }

                            foreach (string name in bundleModInfo.Added.Ebx)
                            {
                                EbxModEntry modEntry = m_modifiedEbx[name];

                                DbObjectDict ebx = DbObject.CreateDict(4);
                                ebx.Set("name", name);
                                ebx.Set("sha1", modEntry.Sha1);
                                ebx.Set("size", modEntry.Size);
                                ebx.Set("originalSize", modEntry.OriginalSize);
                                ebxBundleSize += modEntry.Size;

                                ebxList ??= DbObject.CreateList("ebx", bundleModInfo.Added.Ebx.Count);
                                ebxList.Add(ebx);
                            }

                            for (int i = 0; i < resList?.Count; i++)
                            {
                                DbObjectDict res = resList[i].AsDict();

                                string name = res.AsString("name");

                                if (!bundleModInfo.Modified.Res.Contains(name))
                                {
                                    resBundleSize += res.AsLong("size");
                                    continue;
                                }

                                ResModEntry modEntry = m_modifiedRes[name];

                                res = DbObject.CreateDict(7);
                                res.Set("name", name);
                                res.Set("sha1", modEntry.Sha1);
                                res.Set("size", modEntry.Size);
                                res.Set("originalSize", modEntry.OriginalSize);
                                res.Set("resType", modEntry.ResType);
                                res.Set("resMeta", modEntry.ResMeta);
                                res.Set("resRid", modEntry.ResRid);
                                resBundleSize += modEntry.Size;

                                resList[i] = res;
                            }

                            foreach (string name in bundleModInfo.Added.Res)
                            {
                                ResModEntry modEntry = m_modifiedRes[name];

                                DbObjectDict res = DbObject.CreateDict(7);
                                res.Set("name", name);
                                res.Set("sha1", modEntry.Sha1);
                                res.Set("size", modEntry.Size);
                                res.Set("originalSize", modEntry.OriginalSize);
                                res.Set("resType", modEntry.ResType);
                                res.Set("resMeta", modEntry.ResMeta);
                                res.Set("resRid", modEntry.ResRid);
                                resBundleSize += modEntry.Size;

                                resList ??= DbObject.CreateList("res", bundleModInfo.Added.Res.Count);
                                resList.Add(res);
                            }

                            Dictionary<int, DbObjectDict>? metaDict = null;
                            for (int i = 0; i < chunkList?.Count; i++)
                            {
                                DbObjectDict chunk = chunkList[i].AsDict();

                                Guid chunkId = chunk.AsGuid("id");

                                if (!bundleModInfo.Modified.Chunks.Contains(chunkId))
                                {
                                    chunkBundleSize += chunk.AsLong("size") - chunk.AsUInt("rangeStart");
                                    continue;
                                }

                                ChunkModEntry modEntry = m_modifiedChunks[chunkId];

                                chunk = DbObject.CreateDict(9);
                                chunk.Set("name", chunkId);
                                chunk.Set("sha1", modEntry.Sha1);
                                chunk.Set("size", modEntry.Size);
                                chunk.Set("logicalOffset", modEntry.LogicalOffset);
                                chunk.Set("logicalSize", modEntry.LogicalSize);
                                chunkBundleSize += modEntry.Size - modEntry.RangeStart;

                                // i dont think the chunkMeta is sorted in any way for these games
                                if (metaDict is null)
                                {
                                    metaDict = new Dictionary<int, DbObjectDict>(chunkMetaList!.Count);
                                    foreach (DbObject metaObj in chunkMetaList)
                                    {
                                        metaDict.Add(metaObj.AsDict().AsInt("h32"), metaObj.AsDict());
                                    }
                                }

                                // if the h32 didnt change just get it to change the firstMip if necessary
                                // else we just add a new meta with the new h32
                                // since the game only looks up the h32 afaik
                                if (!metaDict.TryGetValue(modEntry.H32, out DbObjectDict? chunkMeta))
                                {
                                    chunkMeta = DbObject.CreateDict(2);
                                    chunkMeta.Set("h32", modEntry.H32);
                                    chunkMeta.Set("meta", DbObject.CreateDict(1));
                                    chunkMetaList!.Add(chunkMeta);
                                }

                                DbObjectDict meta = chunkMeta.AsDict("meta");

                                if (modEntry.FirstMip != -1)
                                {
                                    // set rangeStart/End in chunk
                                    chunk.Set("rangeStart", modEntry.RangeStart);
                                    chunk.Set("rangeEnd", modEntry.RangeEnd);

                                    // set firstMip in meta
                                    meta.Set("firstMip", modEntry.FirstMip);
                                }
                                else
                                {
                                    // remove firstMip from meta if it exists
                                    meta.Remove("firstMip");
                                }

                                chunkList[i] = chunk;
                            }

                            foreach (Guid chunkId in bundleModInfo.Added.Chunks)
                            {
                                ChunkModEntry modEntry = m_modifiedChunks[chunkId];

                                DbObjectDict chunk = DbObject.CreateDict(9);
                                chunk.Set("name", chunkId);
                                chunk.Set("sha1", modEntry.Sha1);
                                chunk.Set("size", modEntry.Size);

                                // create meta
                                DbObjectDict chunkMeta = DbObject.CreateDict(2);
                                chunkMeta.Set("h32", modEntry.H32);
                                DbObjectDict meta = DbObject.CreateDict(1);
                                chunkMeta.Set("meta", meta);

                                if (modEntry.FirstMip != -1)
                                {
                                    // set rangeStart/End on chunk
                                    chunk.Set("rangeStart", modEntry.RangeStart);
                                    chunk.Set("rangeEnd", modEntry.RangeEnd);

                                    // set firstMip on meta
                                    meta.Set("firstMip", modEntry.FirstMip);
                                }

                                uint bundledSize = (uint)(modEntry.Size - modEntry.RangeStart);
                                chunkBundleSize += bundledSize;
                                if (ProfilesLibrary.FrostbiteVersion >= "2015")
                                {
                                    chunk.Set("bundledSize", bundledSize);
                                }

                                chunk.Set("logicalOffset", modEntry.LogicalOffset);
                                chunk.Set("logicalSize", modEntry.LogicalSize);
                                chunkMetaList ??= DbObject.CreateList("chunkMeta", bundleModInfo.Added.Chunks.Count);
                                chunkMetaList.Add(chunkMeta);

                                chunkList ??= DbObject.CreateList("chunks", bundleModInfo.Added.Chunks.Count);
                                chunkList.Add(chunk);
                            }

                            // ebx and res are always there, even if there are no ebx or res
                            bundle.Set("ebx", ebxList ?? DbObject.CreateList("ebx"));
                            bundle.Set("res", resList ?? DbObject.CreateList("res"));
                            if (chunkList?.Count > 0)
                            {
                                bundle.Set("chunks", chunkList);
                                // if there are chunks there is also chunkMeta
                                bundle.Set("chunkMeta", chunkMetaList!);
                            }

                            bundle.Set("ebxBundleSize", ebxBundleSize);
                            bundle.Set("resBundleSize", resBundleSize);
                            bundle.Set("chunkBundleSize", chunkBundleSize);
                            bundle.Set("totalSize", ebxBundleSize + resBundleSize + chunkBundleSize);

                            // write bundle to sb file
                            DbObject.Serialize(inModifiedStream, bundle);
                            newBundleSize = inModifiedStream.Position - newOffset;
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }


                        // remove bundle so we can check if the base superbundle needs to be loaded to modify a base bundle
                        inModInfo.Modified.Bundles.Remove(id);
                    }

                    // create new bundle and add it to toc
                    DbObjectDict newBundle = DbObject.CreateDict(3);
                    newBundle.Set("id", bundleName);
                    newBundle.Set("offset", newOffset);
                    newBundle.Set("size", newBundleSize);

                    // older versions of fb use these flags, so set them if they exist
                    if (needsOldBaseFlag)
                    {
                        newBundle.Set("base", true);
                    }

                    if (ProfilesLibrary.FrostbiteVersion <= "2014.4.11")
                    {
                        newBundle.Set("delta", true);
                    }

                    inToc.AsList("bundles").Add(newBundle);
                }
            }
        }

        if (toc.ContainsKey("chunks"))
        {
            DbObjectList chunks = toc.AsList("chunks");
            if (!inToc.ContainsKey("chunks"))
            {
                inToc.Set("chunks",
                    DbObject.CreateList("chunks",
                        inOnlyUseModified
                            ? inModInfo.Modified.Chunks.Count + inModInfo.Added.Chunks.Count
                            : chunks.Count + inModInfo.Added.Chunks.Count));
            }

            foreach (DbObject obj in chunks)
            {
                DbObjectDict chunkObj = obj.AsDict();

                Guid id = chunkObj.AsGuid("id");

                if (isCas)
                {
                }
                else
                {

                }

                // create new bundle and add it to toc
                DbObjectDict newChunk;

                if (!inModInfo.Modified.Chunks.Contains(id))
                {
                    if (inOnlyUseModified)
                    {
                        // we create a new patch to a base superbundle, so we only need modified chunks
                        continue;
                    }

                    // use the original info
                    newChunk = chunkObj;
                }
                else
                {
                    // modify chunk
                    newChunk = DbObject.CreateDict(3);

                    if (isCas)
                    {
                        ChunkModEntry modEntry = m_modifiedChunks[id];

                        newChunk.Set("id", id);
                        newChunk.Set("sha1", modEntry.Sha1);
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }

                    // remove chunk so we can check if the base superbundle needs to be loaded to modify a base chunk
                    inModInfo.Modified.Chunks.Remove(id);
                }

                inToc.AsList("chunks").Add(newChunk);
            }
        }
    }

    public void Dispose()
    {
        TocData?.Dispose();
        SbData?.Dispose();
    }
}

public partial class FrostyModExecutor
{
    private void ModDynamic2018(SuperBundleInstallChunk inSbIc, SuperBundleModInfo inModInfo, InstallChunkWriter inInstallChunkWriter)
    {
        bool createNewPatch = false;
        string? tocPath = Path.Combine(m_gamePatchPath, $"{inSbIc.Name}.toc");
        if (!File.Exists(tocPath))
        {
            if (!FileSystemManager.TryResolvePath(false, $"{inSbIc.Name}.toc", out tocPath))
            {
                FrostyLogger.Logger?.LogError("Trying to mod SuperBundle that doesnt exist");
                return;
            }

            createNewPatch = true;
        }

        using (Dynamic2018 action = new(m_modifiedEbx, m_modifiedRes, m_modifiedChunks))
        {
            action.ModSuperBundle(tocPath, createNewPatch, inSbIc, inModInfo, inInstallChunkWriter);

            FileInfo modifiedToc = new(Path.Combine(m_modDataPath, $"{inSbIc.Name}.toc"));
            Directory.CreateDirectory(modifiedToc.DirectoryName!);

            using (FileStream stream = new(modifiedToc.FullName, FileMode.Create, FileAccess.Write))
            {
                ObfuscationHeader.Write(stream);
                stream.Write(action.TocData!);
                action.TocData!.Dispose();
            }

            if (action.SbData is not null)
            {
                using (FileStream stream = new(modifiedToc.FullName.Replace(".toc", ".sb"), FileMode.Create, FileAccess.Write))
                {
                    stream.Write(action.SbData);
                    action.SbData.Dispose();
                }
            }
            else
            {
                // if the sb exists, but we just didnt modify it, create a symbolic link for it
                string sbPath = tocPath.Replace(".toc", ".sb");
                if (File.Exists(sbPath))
                {
                    File.CreateSymbolicLink(modifiedToc.FullName.Replace(".toc", ".sb"), sbPath);
                }
            }
        }
    }
}