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

    private readonly Func<Sha1, (Block<byte> Block, bool NeedsToDispose)> m_getDataFun;

    private bool m_casSb;

    public Dynamic2018(Dictionary<string, EbxModEntry> inModifiedEbx,
        Dictionary<string, ResModEntry> inModifiedRes, Dictionary<Guid, ChunkModEntry> inModifiedChunks, Func<Sha1, (Block<byte>, bool)> inGetDataFun)
    {
        m_modifiedEbx = inModifiedEbx;
        m_modifiedRes = inModifiedRes;
        m_modifiedChunks = inModifiedChunks;
        m_getDataFun = inGetDataFun;
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

        if (m_casSb)
        {
            SbData = new Block<byte>(modifiedSuperBundle.Size);
            long offset;
            using (BlockStream stream = new(SbData, true))
            {
                int value = modifiedSuperBundle.Size;

                int size = 0;
                if (value == 0)
                {
                    size = 1;
                }
                while (value > 0)
                {
                    size++;
                    value >>= 7;
                }

                // write anonymous dict type
                stream.WriteByte(2 | (1 << 7));
                // type + name + size + data + null + null
                stream.Write7BitEncodedInt64(1 + 8 + size + modifiedSuperBundle.Size + 1 + 1);

                // write list type
                stream.WriteByte(1);
                stream.WriteNullTerminatedString("bundles");
                stream.Write7BitEncodedInt64(modifiedSuperBundle.Size + 1);

                offset = stream.Position;
                stream.Write(modifiedSuperBundle);
                modifiedSuperBundle.Dispose();
                stream.WriteByte(0);
                stream.WriteByte(0);
            }

            foreach (DbObject obj in toc.AsList("bundles"))
            {
                DbObjectDict bundle = obj.AsDict();
                if (bundle.AsBoolean("base"))
                {
                    // if its loaded from base superbundle dont increase the offset
                    continue;
                }
                bundle.Set("offset", obj.AsDict().AsLong("offset") + offset);
            }
        }
        else
        {
            SbData = modifiedSuperBundle;
        }

        TocData = new Block<byte>(0);
        using (BlockStream stream = new(TocData, true))
        {
            DbObject.Serialize(stream, toc);
        }
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

        BlockStream? deltaSbStream = null;
        BlockStream? baseSbStream = null;

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

                        // load and write unmodified bundle
                        newBundleSize = size;
                        if (!isBase)
                        {
                            newOffset = inModifiedStream.Position;
                            deltaSbStream ??= BlockStream.FromFile(inPath.Replace(".toc", ".sb"), false);
                            deltaSbStream.Position = offset;
                            deltaSbStream.CopyTo(inModifiedStream, (int)size);
                        }
                        else
                        {
                            needsOldBaseFlag = true;
                            newOffset = offset;
                        }
                    }
                    else
                    {
                        // load, modify and write bundle
                        newOffset = inModifiedStream.Position;
                        if (isBase)
                        {
                            baseSbStream ??= BlockStream.FromFile(FileSystemManager.ResolvePath(false, $"{inSbIc.Name}.sb"), false);
                            sbStream = baseSbStream;
                        }
                        else
                        {
                            deltaSbStream ??= BlockStream.FromFile(inPath.Replace(".toc", ".sb"), false);
                            sbStream = deltaSbStream;
                        }

                        if (isCas)
                        {
                            ModifyAndWriteCasBundle(sbStream, offset, inModifiedStream, inModInfo, bundleModInfo);
                        }
                        else
                        {
                            ModifyAndWriteNonCasBundle(sbStream, offset, inModifiedStream, isDelta, bundleModInfo);
                        }

                        newBundleSize = inModifiedStream.Position - newOffset;

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

                    if (isDelta || (!needsOldBaseFlag && isBase))
                    {
                        newBundle.Set("delta", true);
                    }

                    inToc.AsList("bundles").Add(newBundle);
                }
            }
        }

        foreach (BundleModInfo bundleModInfo in inModInfo.Added.Bundles.Values)
        {
            FrostyLogger.Logger?.LogError("Adding bundles not yet implemented.");
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

                // create new bundle and add it to toc
                DbObjectDict newChunk;

                if (!inModInfo.Modified.Chunks.Contains(id))
                {
                    if (inOnlyUseModified)
                    {
                        // we create a new patch to a base superbundle, so we only need modified chunks
                        continue;
                    }

                    if (isCas)
                    {
                        // use the original info
                        newChunk = chunkObj;
                    }
                    else
                    {
                        long offset = chunkObj.AsLong("offset");
                        long size = chunkObj.AsLong("size");
                        newChunk = DbObject.CreateDict(3);
                        newChunk.Set("id", id);
                        newChunk.Set("offset", inModifiedStream.Position);
                        newChunk.Set("size", size);
                        deltaSbStream ??= BlockStream.FromFile(inPath.Replace(".toc", ".sb"), false);
                        deltaSbStream.Position = offset;
                        deltaSbStream.CopyTo(inModifiedStream, (int)size);
                    }
                }
                else
                {
                    // modify chunk
                    ChunkModEntry modEntry = m_modifiedChunks[id];
                    newChunk = DbObject.CreateDict(3);

                    if (isCas)
                    {
                        newChunk.Set("id", id);
                        newChunk.Set("sha1", modEntry.Sha1);

                        // add sha1 to write cas files later
                        inModInfo.Data.Add(modEntry.Sha1);
                    }
                    else
                    {
                        newChunk.Set("id", id);
                        newChunk.Set("offset", inModifiedStream.Position);
                        newChunk.Set("size", modEntry.Size);

                        (Block<byte> Block, bool NeedsToDispose) data = m_getDataFun(modEntry.Sha1);
                        inModifiedStream.Write(data.Block);

                        if (data.NeedsToDispose)
                        {
                            data.Block.Dispose();
                        }
                    }

                    // remove chunk so we can check if the base superbundle needs to be loaded to modify a base chunk
                    inModInfo.Modified.Chunks.Remove(id);
                }

                inToc.AsList("chunks").Add(newChunk);
            }
        }

        foreach (Guid id in inModInfo.Added.Chunks)
        {
            // add chunk
            ChunkModEntry modEntry = m_modifiedChunks[id];
            DbObjectDict newChunk = DbObject.CreateDict(3);

            if (isCas)
            {
                newChunk.Set("id", id);
                newChunk.Set("sha1", modEntry.Sha1);

                // add sha1 to write cas files later
                inModInfo.Data.Add(modEntry.Sha1);
            }
            else
            {
                newChunk.Set("id", id);
                newChunk.Set("offset", inModifiedStream.Position);
                newChunk.Set("size", modEntry.Size);

                (Block<byte> Block, bool NeedsToDispose) data = m_getDataFun(modEntry.Sha1);
                inModifiedStream.Write(data.Block);

                if (data.NeedsToDispose)
                {
                    data.Block.Dispose();
                }
            }

            inToc.AsList("chunks").Add(newChunk);
        }

        if (isCas)
        {
            inToc.Set("cas", true);
            m_casSb = true;
        }

        deltaSbStream?.Dispose();
        baseSbStream?.Dispose();
    }

    private void ModifyAndWriteCasBundle(BlockStream inStream, long inOffset, BlockStream inModifiedStream, SuperBundleModInfo inModInfo, BundleModInfo inBundleModInfo)
    {
        inStream.Position = inOffset;
        DbObjectDict? bundle = DbObject.Deserialize(inStream)?.AsDict();

        if (bundle is null)
        {
            FrostyLogger.Logger?.LogError("Trying to mod bundle that is not valid.");
            return;
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

            if (!inBundleModInfo.Modified.Ebx.Contains(name))
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

            if (ProfilesLibrary.FrostbiteVersion <= "2014.4.11")
            {
                ebx.Set("casPatchType", 1);
            }

            if (modEntry.Size < byte.MaxValue)
            {
                (Block<byte> Block, bool NeedsToDispose) data = m_getDataFun(modEntry.Sha1);
                ebx.Set("idata", data.Block.ToArray());
                if (data.NeedsToDispose)
                {
                    data.Block.Dispose();
                }
            }

            // add sha1 to write cas files later
            inModInfo.Data.Add(modEntry.Sha1);

            ebxList[i] = ebx;
        }

        foreach (string name in inBundleModInfo.Added.Ebx)
        {
            EbxModEntry modEntry = m_modifiedEbx[name];

            DbObjectDict ebx = DbObject.CreateDict(4);
            ebx.Set("name", name);
            ebx.Set("sha1", modEntry.Sha1);
            ebx.Set("size", modEntry.Size);
            ebx.Set("originalSize", modEntry.OriginalSize);
            ebxBundleSize += modEntry.Size;

            if (ProfilesLibrary.FrostbiteVersion <= "2014.4.11")
            {
                ebx.Set("casPatchType", 1);
            }

            if (modEntry.Size < byte.MaxValue)
            {
                (Block<byte> Block, bool NeedsToDispose) data = m_getDataFun(modEntry.Sha1);
                ebx.Set("idata", data.Block.ToArray());
                if (data.NeedsToDispose)
                {
                    data.Block.Dispose();
                }
            }

            // add sha1 to write cas files later
            inModInfo.Data.Add(modEntry.Sha1);

            ebxList ??= DbObject.CreateList("ebx", inBundleModInfo.Added.Ebx.Count);
            ebxList.Add(ebx);
        }

        for (int i = 0; i < resList?.Count; i++)
        {
            DbObjectDict res = resList[i].AsDict();

            string name = res.AsString("name");

            if (!inBundleModInfo.Modified.Res.Contains(name))
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

            if (ProfilesLibrary.FrostbiteVersion <= "2014.4.11")
            {
                res.Set("casPatchType", 1);
            }

            if (modEntry.Size < byte.MaxValue)
            {
                (Block<byte> Block, bool NeedsToDispose) data = m_getDataFun(modEntry.Sha1);
                res.Set("idata", data.Block.ToArray());
                if (data.NeedsToDispose)
                {
                    data.Block.Dispose();
                }
            }

            // add sha1 to write cas files later
            inModInfo.Data.Add(modEntry.Sha1);

            resList[i] = res;
        }

        foreach (string name in inBundleModInfo.Added.Res)
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

            if (ProfilesLibrary.FrostbiteVersion <= "2014.4.11")
            {
                res.Set("casPatchType", 1);
            }

            if (modEntry.Size < byte.MaxValue)
            {
                (Block<byte> Block, bool NeedsToDispose) data = m_getDataFun(modEntry.Sha1);
                res.Set("idata", data.Block.ToArray());
                if (data.NeedsToDispose)
                {
                    data.Block.Dispose();
                }
            }

            // add sha1 to write cas files later
            inModInfo.Data.Add(modEntry.Sha1);

            resList ??= DbObject.CreateList("res", inBundleModInfo.Added.Res.Count);
            resList.Add(res);
        }

        Dictionary<int, List<DbObjectDict>>? metaDict = null;
        for (int i = 0; i < chunkList?.Count; i++)
        {
            DbObjectDict chunk = chunkList[i].AsDict();

            Guid chunkId = chunk.AsGuid("id");

            if (!inBundleModInfo.Modified.Chunks.Contains(chunkId))
            {
                chunkBundleSize += chunk.AsLong("size") - chunk.AsUInt("rangeStart");
                continue;
            }

            ChunkModEntry modEntry = m_modifiedChunks[chunkId];

            chunk = DbObject.CreateDict(9);
            chunk.Set("id", chunkId);
            chunk.Set("sha1", modEntry.Sha1);
            chunk.Set("size", modEntry.Size);
            chunk.Set("logicalOffset", modEntry.LogicalOffset);
            chunk.Set("logicalSize", modEntry.LogicalSize);
            uint bundledSize = (uint)(modEntry.Size - modEntry.RangeStart);
            chunkBundleSize += bundledSize;
            if (ProfilesLibrary.FrostbiteVersion >= "2015")
            {
                chunk.Set("bundledSize", bundledSize);
            }

            if (ProfilesLibrary.FrostbiteVersion <= "2014.4.11")
            {
                chunk.Set("casPatchType", 1);
            }

            if (modEntry.Size < byte.MaxValue)
            {
                (Block<byte> Block, bool NeedsToDispose) data = m_getDataFun(modEntry.Sha1);
                chunk.Set("idata", data.Block.ToArray());
                if (data.NeedsToDispose)
                {
                    data.Block.Dispose();
                }
            }

            // add sha1 to write cas files later
            inModInfo.Data.Add(modEntry.Sha1);

            // i dont think the chunkMeta is sorted in any way for these games
            if (metaDict is null)
            {
                metaDict = new Dictionary<int, List<DbObjectDict>>(chunkMetaList!.Count);
                foreach (DbObject metaObj in chunkMetaList)
                {
                    // some chunks have the same
                    int h32 = metaObj.AsDict().AsInt("h32");
                    metaDict.TryAdd(h32, new List<DbObjectDict>());
                    metaDict[h32].Add(metaObj.AsDict());
                }
            }

            // if the h32 didnt change just get it to change the firstMip if necessary
            // else we just add a new meta with the new h32
            // since the game only looks up the h32 afaik
            DbObjectDict? chunkMeta;

            if (modEntry.H32 == 0)
            {
                // old mod format didnt store h32, so we just dont update the meta
                chunkMeta = null;
            }
            else if (!metaDict.TryGetValue(modEntry.H32, out List<DbObjectDict>? list))
            {
                chunkMeta = DbObject.CreateDict(2);
                chunkMeta.Set("h32", modEntry.H32);
                chunkMeta.Set("meta", DbObject.CreateDict(1));
                chunkMetaList!.Add(chunkMeta);
            }
            else
            {
                if (list.Count != 1 && modEntry.FirstMip != -1)
                {
                    FrostyLogger.Logger?.LogWarning($"More than 1 chunk for texture with h32 {modEntry.H32}");
                }
                chunkMeta = list[0];
            }

            DbObjectDict? meta = chunkMeta?.AsDict("meta");

            if (modEntry.FirstMip != -1)
            {
                // set rangeStart/End in chunk
                chunk.Set("rangeStart", modEntry.RangeStart);
                chunk.Set("rangeEnd", modEntry.RangeEnd);

                // set firstMip in meta
                meta?.Set("firstMip", modEntry.FirstMip);
            }
            else
            {
                // remove firstMip from meta if it exists
                if (meta?.Remove("firstMip") == true)
                {
                    // non 2d texture chunk, since frosty v1 didn't modify it correctly, log a warning
                    FrostyLogger.Logger?.LogWarning($"Mod does not correctly modify a texture chunk ({chunkId}).");
                }
            }

            chunkList[i] = chunk;
        }

        foreach (Guid chunkId in inBundleModInfo.Added.Chunks)
        {
            ChunkModEntry modEntry = m_modifiedChunks[chunkId];

            DbObjectDict chunk = DbObject.CreateDict(9);
            chunk.Set("id", chunkId);
            chunk.Set("sha1", modEntry.Sha1);
            chunk.Set("size", modEntry.Size);
            chunk.Set("logicalOffset", modEntry.LogicalOffset);
            chunk.Set("logicalSize", modEntry.LogicalSize);

            uint bundledSize = (uint)(modEntry.Size - modEntry.RangeStart);
            chunkBundleSize += bundledSize;
            if (ProfilesLibrary.FrostbiteVersion >= "2015")
            {
                chunk.Set("bundledSize", bundledSize);
            }

            if (ProfilesLibrary.FrostbiteVersion <= "2014.4.11")
            {
                chunk.Set("casPatchType", 1);
            }

            if (modEntry.Size < byte.MaxValue)
            {
                (Block<byte> Block, bool NeedsToDispose) data = m_getDataFun(modEntry.Sha1);
                chunk.Set("idata", data.Block.ToArray());
                if (data.NeedsToDispose)
                {
                    data.Block.Dispose();
                }
            }

            // add sha1 to write cas files later
            inModInfo.Data.Add(modEntry.Sha1);

            if (metaDict is null)
            {
                chunkMetaList ??= DbObject.CreateList("chunkMeta", inBundleModInfo.Added.Chunks.Count);
                metaDict = new Dictionary<int, List<DbObjectDict>>(chunkMetaList.Count);
                foreach (DbObject metaObj in chunkMetaList)
                {
                    // some chunks have the same
                    int h32 = metaObj.AsDict().AsInt("h32");
                    metaDict.TryAdd(h32, new List<DbObjectDict>());
                    metaDict[h32].Add(metaObj.AsDict());
                }
            }

            DbObjectDict? chunkMeta;
            if (!metaDict.TryGetValue(modEntry.H32, out List<DbObjectDict>? list))
            {
                chunkMeta = DbObject.CreateDict(2);
                chunkMeta.Set("h32", modEntry.H32);
                chunkMeta.Set("meta", DbObject.CreateDict(1));
                chunkMetaList ??= DbObject.CreateList("chunkMeta", inBundleModInfo.Added.Chunks.Count);
                chunkMetaList.Add(chunkMeta);
            }
            else
            {
                if (list.Count != 1 && modEntry.FirstMip != -1)
                {
                    FrostyLogger.Logger?.LogWarning($"More than 1 chunk for texture with h32 {modEntry.H32}");
                }
                chunkMeta = list[0];
            }

            DbObjectDict? meta = chunkMeta?.AsDict("meta");

            if (modEntry.FirstMip != -1)
            {
                // set rangeStart/End in chunk
                chunk.Set("rangeStart", modEntry.RangeStart);
                chunk.Set("rangeEnd", modEntry.RangeEnd);

                // set firstMip in meta
                meta?.Set("firstMip", modEntry.FirstMip);
            }
            else
            {
                // remove firstMip from meta if it exists
                if (meta?.Remove("firstMip") == true)
                {
                    // non 2d texture chunk, since frosty v1 didn't modify it correctly, log a warning
                    FrostyLogger.Logger?.LogWarning($"Mod does not correctly modify a texture chunk ({chunkId}).");
                }
            }

            chunkList ??= DbObject.CreateList("chunks", inBundleModInfo.Added.Chunks.Count);
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

        if (ProfilesLibrary.FrostbiteVersion > "2014.4.11")
        {
            bundle.Set("ebxBundleSize", ebxBundleSize);
            bundle.Set("resBundleSize", resBundleSize);
            bundle.Set("chunkBundleSize", chunkBundleSize);
        }
        bundle.Set("totalSize", ebxBundleSize + resBundleSize + chunkBundleSize);

        // write bundle to sb file
        DbObject.Serialize(inModifiedStream, bundle);
    }

    private void ModifyAndWriteNonCasBundle(BlockStream inStream, long inOffset, BlockStream inModifiedStream, bool isDelta, BundleModInfo inModInfo)
    {
        inStream.Position = inOffset;
        FrostyLogger.Logger?.LogWarning("Non cas superbundle, there might be some issues.");

        uint baseBundleSize;
        Block<byte> bundleMeta;
        Block<byte> data = new(0);
        BlockStream dataStream = new(data, true);
        if (isDelta)
        {
            throw new NotImplementedException();
        }
        else
        {
            baseBundleSize = (uint)inStream.Position;
            bundleMeta = BinaryBundle.Modify(inStream, inModInfo, m_modifiedEbx, m_modifiedRes, m_modifiedChunks,
                (entry, i, isAdded, isModified, originalSize) =>
                {
                    if (!isModified)
                    {
                        uint blockCount = (originalSize + (uint)ProfilesLibrary.MaxBufferSize - 1) /
                                          (uint)ProfilesLibrary.MaxBufferSize;
                        dataStream.WriteUInt32(blockCount, Endian.Big);
                    }
                    else
                    {
                        uint blockCount = (originalSize & (uint)ProfilesLibrary.MaxBufferSize - 1) /
                                          (uint)ProfilesLibrary.MaxBufferSize;
                        dataStream.WriteUInt32(blockCount, Endian.Big);
                        blockCount = ((uint)entry.OriginalSize + (uint)ProfilesLibrary.MaxBufferSize - 1) /
                                          (uint)ProfilesLibrary.MaxBufferSize;
                        dataStream.WriteUInt32(blockCount | 0x30000000, Endian.Big);

                        // write data
                        (Block<byte> Block, bool NeedsToDispose) data = m_getDataFun(entry.Sha1);

                        if (entry is ChunkModEntry chunk && chunk.FirstMip > 0)
                        {
                            data.Block.Shift((int)chunk.RangeStart);
                        }

                        dataStream.Write(data.Block);

                        if (data.NeedsToDispose)
                        {
                            data.Block.Dispose();
                        }
                    }
                });
            baseBundleSize = (uint)inStream.Position - baseBundleSize - 4;
        }

        dataStream.Dispose();

        // write patched bundle
        inModifiedStream.WriteUInt32(1, Endian.Big);
        inModifiedStream.WriteUInt32(0, Endian.Big);
        inModifiedStream.WriteInt32(bundleMeta.Size + 8, Endian.Big); // size of patched bundleMeta and uint to skip base bundleMeta
        inModifiedStream.WriteInt32(data.Size, Endian.Big); // size of data after the meta

        inModifiedStream.WriteInt32(bundleMeta.Size - 4, Endian.Big); // size of the final patched bundleMeta without the storing stuff
        inModifiedStream.WriteUInt32(baseBundleSize | 0x40000000u, Endian.Big); // skip baseBundle
        inModifiedStream.WriteUInt32(bundleMeta.ToBlock<uint>()[0] | 0x80); // this is big endian, since the ptr is giving us the thing in le we write it in le
        bundleMeta.Shift(4); // skip size
        inModifiedStream.Write(bundleMeta);
        bundleMeta.Dispose();
        inModifiedStream.Write(data);
        data.Dispose();
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

        using (Dynamic2018 action = new(m_modifiedEbx, m_modifiedRes, m_modifiedChunks, GetData))
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