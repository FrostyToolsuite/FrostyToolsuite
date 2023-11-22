using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Frosty.Sdk.DbObjectElements;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Managers.Infos.FileInfos;

namespace Frosty.Sdk.Managers.Loaders;

public class Dynamic2018AssetLoader : IAssetLoader
{
    private struct BundleHelper
    {
        public string Name;
        public long Offset;
        public long Size;

        public BundleHelper(string inName, long inOffset, long inSize)
        {
            Name = inName;
            Offset = inOffset;
            Size = inSize;
        }
    }

    public void Load()
    {
        foreach (SuperBundleInfo sbInfo in FileSystemManager.EnumerateSuperBundles())
        {
            foreach (SuperBundleInstallChunk sbIc in sbInfo.InstallChunks)
            {
                bool found = false;
                foreach (FileSystemSource source in FileSystemManager.Sources)
                {
                    switch (LoadSuperBundle(source, sbIc))
                    {
                        case Code.Continue:
                            found = true;
                            continue;
                        case Code.NotFound:
                            continue;
                        case Code.Stop:
                            found = true;
                            break;
                    }

                    break;
                }

                if (!found)
                {
                    AssetManager.Logger?.Report("Sdk", $"Couldn't find SuperBundle {sbIc.Name}");
                }
            }
        }
    }

    private Code LoadSuperBundle(FileSystemSource inSource, SuperBundleInstallChunk inSbIc)
    {
        if (!inSource.TryResolvePath($"{inSbIc.Name}.toc", out string? path))
        {
            return Code.NotFound;
        }

        // load the toc
        DbObjectDict? toc = DbObject.Deserialize(path)?.AsDict();

        if (toc is null)
        {
            // just return NotFound, should not happen anyways
            Debug.Assert(false, "We should not be here");
            return Code.NotFound;
        }

        // check for format flags
        bool isCas = toc.AsBoolean("cas");
        bool isDas = toc.AsBoolean("das");

        // path to sb file
        string sbPath = path.Replace(".toc", ".sb");

        // load bundles
        if (toc.ContainsKey("bundles"))
        {
            // stream for loading from sb file
            DataStream sbStream = BlockStream.FromFile(sbPath, false);
            DataStream? baseSbStream = null;
            Dictionary<int, BundleHelper>? baseBundleMapping = null;

            // is its a das superBundle it stores the bundle values in lists
            if (isDas)
            {
                DbObjectDict bundles = toc.AsDict("bundles");

                DbObjectList names = bundles.AsList("names");
                DbObjectList offsets = bundles.AsList("offsets");
                DbObjectList sizes = bundles.AsList("sizes");

                for (int i = 0; i < names.Count; i++)
                {
                    BundleInfo bundle = AssetManager.AddBundle(names[i].AsString(), inSbIc);
                    LoadBundle(sbStream, offsets[i].AsLong(), sizes[i].AsLong(), ref bundle, false);
                }
            }
            else
            {
                DbObjectList bundles = toc.AsList("bundles");

                if (bundles.Count > 0 && ProfilesLibrary.FrostbiteVersion < "2014.4.11" && !inSource.IsDLC() && !FileSystemSource.Base.TryResolvePath($"{inSbIc.Name}.toc", out _))
                {
                    // some superbundles are still in the Update/Patch folder even tho their base bundles which are needed are not there (e.g. languages that are not loaded)
                    return Code.NotFound;
                }

                foreach (DbObject obj in bundles)
                {
                    DbObjectDict bundleObj = obj.AsDict();

                    BundleInfo bundle = AssetManager.AddBundle(bundleObj.AsString("id"), inSbIc);

                    long offset = bundleObj.AsLong("offset");
                    long size = bundleObj.AsLong("size");

                    // legacy flags used until fb 2014.4.11
                    // cas + delta -> casPatchType for bundle members
                    // noncas + delta -> patched bundle and bundle members
                    // (non)cas + base -> load bundle from base sb file
                    bool isDelta = bundleObj.AsBoolean("delta");
                    bool isBase = bundleObj.AsBoolean("base");

                    if (isBase)
                    {
                        baseSbStream ??=
                            BlockStream.FromFile(FileSystemManager.ResolvePath(false, $"{inSbIc.Name}.sb"), false);

                        LoadBundle(baseSbStream, offset, size, ref bundle, !isCas);
                    }
                    else if (!isCas && isDelta)
                    {
                        // for the cas bundle format the delta flag means that the casPatchType is stored for each bundle member
                        // we need to load the base toc to get the corresponding base bundle
                        baseBundleMapping ??= LoadBaseBundles(FileSystemManager.ResolvePath(false, $"{inSbIc.Name}.toc"));

                        if (baseBundleMapping.TryGetValue(Utils.Utils.HashString(bundle.Name, true), out BundleHelper helper))
                        {
                            baseSbStream ??=
                                BlockStream.FromFile(FileSystemManager.ResolvePath(false, $"{inSbIc.Name}.sb"), false);
                        }

                        LoadDeltaBundle(sbStream, offset, size, baseSbStream, helper.Offset, helper.Size, ref bundle);
                    }
                    else
                    {
                        LoadBundle(sbStream, offset, size, ref bundle, !isCas, isDelta);
                    }
                }
            }

            sbStream.Dispose();
            baseSbStream?.Dispose();
        }

        // load chunks
        if (toc.ContainsKey("chunks"))
        {
            HashSet<Guid> patchChunks = new();

            foreach (DbObject obj in toc.AsList("chunks"))
            {
                DbObjectDict chunkObj = obj.AsDict();
                ChunkAssetEntry entry;
                if (isCas || isDas)
                {
                    entry = new ChunkAssetEntry(chunkObj.AsGuid("id"), chunkObj.AsSha1("sha1"), 0, 0, Utils.Utils.HashString(inSbIc.Name, true));

                    IEnumerable<IFileInfo>? fileInfos = ResourceManager.GetFileInfos(entry.Sha1);
                    if (fileInfos is not null)
                    {
                        entry.FileInfos.UnionWith(fileInfos);
                    }
                }
                else
                {
                    entry = new ChunkAssetEntry(chunkObj.AsGuid("id"), Sha1.Zero, 0, 0, Utils.Utils.HashString(inSbIc.Name, true));
                    entry.FileInfos.Add(new NonCasFileInfo(inSbIc.Name, chunkObj.AsUInt("offset"),
                        chunkObj.AsUInt("size")));
                }

                patchChunks.Add(entry.Id);

                AssetManager.AddSuperBundleChunk(entry);

                if (entry.LogicalSize == 0)
                {
                    // TODO: get original size
                    // entry.OriginalSize = entry.FileInfo.GetOriginalSize();
                }
            }

            if (inSource != FileSystemSource.Base)
            {
                string basePath = FileSystemSource.Base.ResolvePath($"{inSbIc.Name}.toc");
                DbObjectDict? baseToc = string.IsNullOrEmpty(basePath) ? null : DbObject.Deserialize(basePath)?.AsDict();

                if (baseToc is not null)
                {
                    foreach (DbObject obj in baseToc.AsList("chunks"))
                    {
                        DbObjectDict chunkObj = obj.AsDict();
                        Guid id = chunkObj.AsGuid("id");
                        if (patchChunks.Contains(id))
                        {
                            continue;
                        }

                        ChunkAssetEntry entry;
                        if (isCas || isDas)
                        {
                            entry = new ChunkAssetEntry(chunkObj.AsGuid("id"), chunkObj.AsSha1("sha1"), 0, 0, Utils.Utils.HashString(inSbIc.Name, true));

                            IEnumerable<IFileInfo>? fileInfos = ResourceManager.GetFileInfos(entry.Sha1);
                            if (fileInfos is not null)
                            {
                                entry.FileInfos.UnionWith(fileInfos);
                            }
                        }
                        else
                        {
                            entry = new ChunkAssetEntry(chunkObj.AsGuid("id"), Sha1.Zero, 0, 0, Utils.Utils.HashString(inSbIc.Name, true));
                        }

                        AssetManager.AddSuperBundleChunk(entry);

                        if (entry.LogicalSize == 0)
                        {
                            // TODO: get original size
                            // entry.OriginalSize = entry.FileInfo.GetOriginalSize();
                        }
                    }
                }
            }

        }

        if (toc.ContainsKey("hasBaseBundles") || toc.ContainsKey("hasBaseChunks"))
        {
            /* these are never actually used, tho the newer games check for them
            if (toc.ContainsKey("removedBundles"))
            {
            }

            if (toc.ContainsKey("removedChunks"))
            {
            }*/

            return Code.Continue;
        }

        return Code.Stop;
    }

    private Dictionary<int, BundleHelper> LoadBaseBundles(string inPath)
    {
        Dictionary<int, BundleHelper> retVal = new();

        if (!File.Exists(inPath))
        {
            return retVal;
        }

        DbObjectDict? toc = DbObject.Deserialize(inPath)?.AsDict();

        if (toc is null || !toc.ContainsKey("bundles"))
        {
            return retVal;
        }

        foreach (DbObject obj in toc.AsList("bundles"))
        {
            string name = obj.AsDict().AsString("id");
            retVal.Add(Utils.Utils.HashString(name, true), new BundleHelper(name, obj.AsDict().AsLong("offset"), obj.AsDict().AsLong("size")));
        }

        return retVal;
    }

    private void LoadDeltaBundle(DataStream deltaStream, long inDeltaOffset, long inDeltaSize, DataStream? baseStream,
        long inBaseOffset, long inBaseSize, ref BundleInfo bundle)
    {
        deltaStream.Position = inDeltaOffset;
        if (inBaseSize != 0)
        {
            baseStream!.Position = inBaseOffset;
        }
        else
        {
            // we need to set it to null here to be sure
            baseStream = null;
        }

        BinaryBundle bundleMeta = DeserializeDeltaBundle(deltaStream, baseStream);

        // TODO: get asset refs from sb file similar to this (https://github.com/GreyDynamics/Frostbite3_Editor/blob/develop/src/tk/greydynamics/Resource/Frostbite3/Cas/NonCasBundle.java)
        // or with a cache like before
        // this is just so u can load those games for now
        foreach (EbxAssetEntry ebx in bundleMeta.EbxList)
        {
            AssetManager.AddEbx(ebx, bundle.Id);
        }

        foreach (ResAssetEntry res in bundleMeta.ResList)
        {
            AssetManager.AddRes(res, bundle.Id);
        }

        foreach (ChunkAssetEntry chunk in bundleMeta.ChunkList)
        {
            AssetManager.AddChunk(chunk, bundle.Id);
        }

        // disable for now since we dont read the data after the bundle
        // Debug.Assert(deltaStream.Position == inDeltaOffset + inDeltaSize, "Didnt read delta bundle correctly.");
        // Debug.Assert((baseStream?.Position ?? 0) == inBaseOffset + inBaseSize, "Didnt read base bundle correctly.");

    }

    private void LoadBundle(DataStream stream, long inOffset, long inSize, ref BundleInfo bundle, bool isNonCas, bool isDelta = false)
    {
        stream.Position = inOffset;

        if (isNonCas)
        {
            LoadNonCasBundle(stream, bundle);
        }
        else
        {
            LoadCasBundle(stream, bundle, isDelta);
        }

        Debug.Assert(stream.Position == inOffset + inSize, "Didnt read bundle correctly.");
    }

    private static void LoadNonCasBundle(DataStream stream, BundleInfo bundle)
    {
        BinaryBundle bundleMeta = BinaryBundle.Deserialize(stream);

        foreach (EbxAssetEntry ebx in bundleMeta.EbxList)
        {
            uint offset = (uint)stream.Position;
            uint size = (uint)Helper.GetSize(stream, ebx.OriginalSize);
            ebx.FileInfos.Add(new NonCasFileInfo(bundle.Parent.Name, offset, size));

            AssetManager.AddEbx(ebx, bundle.Id);
        }

        foreach (ResAssetEntry res in bundleMeta.ResList)
        {
            uint offset = (uint)stream.Position;
            uint size = (uint)Helper.GetSize(stream, res.OriginalSize);
            res.FileInfos.Add(new NonCasFileInfo(bundle.Parent.Name, offset, size));

            AssetManager.AddRes(res, bundle.Id);
        }

        foreach (ChunkAssetEntry chunk in bundleMeta.ChunkList)
        {
            uint offset = (uint)stream.Position;
            // the size of the range is different than the logical size, since the range wont get decreased further once it fits in one block
            uint size = (uint)Helper.GetSize(stream, (chunk.LogicalOffset & 0xFFFF) | chunk.LogicalSize);
            chunk.FileInfos.Add(new NonCasFileInfo(bundle.Parent.Name, offset, size, chunk.LogicalOffset));

            AssetManager.AddChunk(chunk, bundle.Id);
        }
    }

    private static void LoadCasBundle(DataStream stream, BundleInfo bundle, bool isDelta)
    {
        DbObjectDict? bundleObj = DbObject.Deserialize(stream)?.AsDict();
        if (bundleObj is null)
        {
            AssetManager.Logger?.Report("Sdk", $"Invalid bundle {bundle.Name}");
            return;
        }

        DbObjectList? ebxList = bundleObj.AsList("ebx", null);
        DbObjectList? resList = bundleObj.AsList("res", null);
        DbObjectList? chunkList = bundleObj.AsList("chunks", null);

        for (int i = 0; i < ebxList?.Count; i++)
        {
            DbObjectDict ebx = ebxList[i].AsDict();

            EbxAssetEntry entry = new(ebx.AsString("name"), ebx.AsSha1("sha1"),
                ebx.AsLong("originalSize"));

            // old patch storing until fb 2014.4.11
            // casPatchType:
            //   - 0: non patched
            //   - 2: patched base with delta
            if (isDelta && ebx.AsInt("casPatchType") == 2)
            {
                Sha1 baseSha1 = ebx.AsSha1("baseSha1");
                Sha1 deltaSha1 = ebx.AsSha1("deltaSha1");

                IEnumerable<IFileInfo>? fileInfos =
                    ResourceManager.GetPatchFileInfos(entry.Sha1, deltaSha1, baseSha1);

                if (fileInfos is not null)
                {
                    entry.FileInfos.UnionWith(fileInfos);
                }
            }
            else
            {
                IEnumerable<IFileInfo>? fileInfos = ResourceManager.GetFileInfos(entry.Sha1);
                if (fileInfos is not null)
                {
                    entry.FileInfos.UnionWith(fileInfos);
                }
            }

            AssetManager.AddEbx(entry, bundle.Id);
        }

        for (int i = 0; i < resList?.Count; i++)
        {
            DbObjectDict res = resList[i].AsDict();

            ResAssetEntry entry = new(res.AsString("name"), res.AsSha1("sha1"),
                res.AsLong("originalSize"), res.AsULong("resRid"), res.AsUInt("resType"),
                res.AsBlob("resMeta"));

            if (isDelta && res.AsInt("casPatchType") == 2)
            {
                Sha1 baseSha1 = res.AsSha1("baseSha1");
                Sha1 deltaSha1 = res.AsSha1("deltaSha1");

                IEnumerable<IFileInfo>? fileInfos =
                    ResourceManager.GetPatchFileInfos(entry.Sha1, deltaSha1, baseSha1);

                if (fileInfos is not null)
                {
                    entry.FileInfos.UnionWith(fileInfos);
                }
            }
            else
            {
                IEnumerable<IFileInfo>? fileInfos = ResourceManager.GetFileInfos(entry.Sha1);
                if (fileInfos is not null)
                {
                    entry.FileInfos.UnionWith(fileInfos);
                }
            }

            AssetManager.AddRes(entry, bundle.Id);
        }

        for (int i = 0; i < chunkList?.Count; i++)
        {
            DbObjectDict chunk = chunkList[i].AsDict();

            ChunkAssetEntry entry = new(chunk.AsGuid("id"), chunk.AsSha1("sha1"),
                chunk.AsUInt("logicalOffset"), chunk.AsUInt("logicalSize"));

            if (isDelta && chunk.AsInt("casPatchType") == 2)
            {
                Sha1 baseSha1 = chunk.AsSha1("baseSha1");
                Sha1 deltaSha1 = chunk.AsSha1("deltaSha1");

                IEnumerable<IFileInfo>? fileInfos =
                    ResourceManager.GetPatchFileInfos(entry.Sha1, deltaSha1, baseSha1);

                if (fileInfos is not null)
                {
                    entry.FileInfos.UnionWith(fileInfos);
                }
            }
            else
            {
                IEnumerable<IFileInfo>? fileInfos = ResourceManager.GetFileInfos(entry.Sha1);
                if (fileInfos is not null)
                {
                    entry.FileInfos.UnionWith(fileInfos);
                }
            }

            AssetManager.AddChunk(entry, bundle.Id);
        }
    }

    private BinaryBundle DeserializeDeltaBundle(DataStream deltaStream, DataStream? baseStream)
    {
        ulong magic = deltaStream.ReadUInt64();
        if (magic != 0x0000000001000000)
        {
            throw new InvalidDataException();
        }

        uint bundleSize = deltaStream.ReadUInt32(Endian.Big);
        deltaStream.ReadUInt32(Endian.Big); // size of data after binary bundle

        long startOffset = deltaStream.Position;

        int patchedBundleSize = deltaStream.ReadInt32(Endian.Big);
        uint baseBundleSize = baseStream?.ReadUInt32(Endian.Big) ?? 0;
        long baseBundleOffset = baseStream?.Position ?? -1;

        using (BlockStream stream = new(patchedBundleSize + 4))
        {
            stream.WriteInt32(patchedBundleSize, Endian.Big);

            while (deltaStream.Position < bundleSize + startOffset)
            {
                uint packed = deltaStream.ReadUInt32(Endian.Big);
                uint instructionType = (packed & 0xF0000000) >> 28;
                int blockData = (int)(packed & 0x0FFFFFFF);

                switch (instructionType)
                {
                    // read base block
                    case 0:
                        stream.Write(baseStream!.ReadBytes(blockData), 0, blockData);
                        break;
                    // skip base block
                    case 4:
                        baseStream!.Position += blockData;
                        break;
                    // read delta block
                    case 8:
                        stream.Write(deltaStream.ReadBytes(blockData), 0, blockData);
                        break;
                }
            }

            if (baseStream is not null)
            {
                baseStream.Position = baseBundleOffset + baseBundleSize;
            }

            stream.Position = 0;
            return BinaryBundle.Deserialize(stream);
        }
    }
}