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

        bool isPatch = inSource.Path != FileSystemSource.Base.Path;

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
            string? baseSbPath = null;
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
                        baseSbPath ??= FileSystemSource.Base.ResolvePath($"{inSbIc.Name}.sb");
                        baseSbStream ??=
                            BlockStream.FromFile(baseSbPath, false);

                        LoadBundle(baseSbStream, offset, size, ref bundle, !isCas, inIsPatch: false);
                    }
                    else if (!isCas && isDelta)
                    {
                        // for the cas bundle format the delta flag means that the casPatchType is stored for each bundle member
                        // we need to load the base toc to get the corresponding base bundle
                        baseBundleMapping ??= LoadBaseBundles(FileSystemSource.Base.ResolvePath($"{inSbIc.Name}.toc"));

                        if (baseBundleMapping.TryGetValue(Utils.Utils.HashString(bundle.Name, true), out BundleHelper helper))
                        {
                            baseSbPath ??= FileSystemSource.Base.ResolvePath($"{inSbIc.Name}.sb");
                            baseSbStream ??=
                                BlockStream.FromFile(baseSbPath, false);
                        }

                        LoadDeltaBundle(sbStream, offset, size, baseSbStream, helper.Offset, helper.Size, ref bundle);
                    }
                    else
                    {
                        LoadBundle(sbStream, offset, size, ref bundle, !isCas, isDelta, isPatch);
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

                    IFileInfo? fileInfo = ResourceManager.GetFileInfo(entry.Sha1);
                    if (fileInfo is not null)
                    {
                        entry.AddFileInfo(fileInfo);
                    }
                }
                else
                {
                    entry = new ChunkAssetEntry(chunkObj.AsGuid("id"), Sha1.Zero, 0, 0, Utils.Utils.HashString(inSbIc.Name, true));
                    entry.AddFileInfo(new NonCasFileInfo(inSbIc.Name, isPatch, chunkObj.AsUInt("offset"),
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

            if (isPatch)
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
                            entry = new ChunkAssetEntry(chunkObj.AsGuid("id"), chunkObj.AsSha1("sha1"), 0, 0,
                                Utils.Utils.HashString(inSbIc.Name, true));

                            IFileInfo? fileInfo = ResourceManager.GetFileInfo(entry.Sha1);
                            if (fileInfo is not null)
                            {
                                entry.AddFileInfo(fileInfo);
                            }
                        }
                        else
                        {
                            entry = new ChunkAssetEntry(chunkObj.AsGuid("id"), Sha1.Zero, 0, 0,
                                Utils.Utils.HashString(inSbIc.Name, true));
                            entry.AddFileInfo(new NonCasFileInfo(inSbIc.Name, false, chunkObj.AsUInt("offset"),
                                chunkObj.AsUInt("size")));
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

    private void LoadDeltaBundle(DataStream inDeltaStream, long inDeltaOffset, long inDeltaSize, DataStream? inBaseStream,
        long inBaseOffset, long inBaseSize, ref BundleInfo bundle)
    {
        inDeltaStream.Position = inDeltaOffset;
        if (inBaseSize != 0)
        {
            inBaseStream!.Position = inBaseOffset;
        }
        else
        {
            // we need to set it to null here to be sure
            inBaseStream = null;
        }
        BinaryBundle bundleMeta = DeserializeDeltaBundle(inDeltaStream, inBaseStream);

        int index = 0;

        LoadDeltaStoredAssets(inDeltaStream, inDeltaOffset, inDeltaSize, inBaseStream, inBaseOffset, inBaseSize,
            () =>
            {
                if (index < bundleMeta.EbxList.Length)
                {
                    return bundleMeta.EbxList[index++];
                }

                if (index < bundleMeta.EbxList.Length + bundleMeta.ResList.Length)
                {
                    return bundleMeta.ResList[index++ - bundleMeta.EbxList.Length];
                }

                if (index < bundleMeta.EbxList.Length + bundleMeta.ResList.Length + bundleMeta.ChunkList.Length)
                {
                    return bundleMeta.ChunkList[index++ - bundleMeta.EbxList.Length - bundleMeta.ResList.Length];
                }

                return null;
            }, bundle);

        Debug.Assert(inDeltaStream.Position == inDeltaOffset + inDeltaSize, "Didnt read delta bundle correctly.");
        Debug.Assert((inBaseStream?.Position ?? 0) == inBaseOffset + inBaseSize, "Didnt read base bundle correctly.");
    }



    private void LoadDeltaStoredAssets(DataStream inDeltaStream, long inDeltaOffset, long inDeltaSize,
        DataStream? inBaseStream, long inBaseOffset, long inBaseSize, Func<AssetEntry?> getNextAsset,
        BundleInfo inBundle)
    {
        AssetEntry? entry = getNextAsset();
        long originalSize = entry is not null ? GetOriginalSize(entry) : -1;
        long sizeLeft = originalSize;

        uint deltaOffset = 0, baseOffset = 0;
        int midInstructionSize = -1;

        while (inDeltaStream.Position - inDeltaOffset < inDeltaSize)
        {
            if (entry is not null && sizeLeft == originalSize)
            {
                deltaOffset = (uint)inDeltaStream.Position;
                baseOffset = (uint)(inBaseStream?.Position ?? 0);
                midInstructionSize = -1;
            }

            if (entry?.Name == "d657b459-2780-bcdd-7c3d-73982b1cbfd9")
            {

            }

            // read patched storing
            uint packed = inDeltaStream.ReadUInt32(Endian.Big);
            int instructionType = (int)(packed & 0xF0000000) >> 28;
            int instructionSize = (int)(packed & 0x0FFFFFFF);

            switch (instructionType)
            {
                case 0:
                {
                    // read base blocks
                    while (instructionSize-- > 0)
                    {
                        Debug.Assert(inBaseStream is not null);
                        sizeLeft -= Cas.GetUncompressedSize(inBaseStream);

                        if (sizeLeft <= 0)
                        {
                            Debug.Assert(sizeLeft == 0);

                            AddAsset(inDeltaStream, inBaseStream, inBundle, entry, deltaOffset, baseOffset, midInstructionSize);

                            entry = getNextAsset();
                            if (entry is not null)
                            {
                                originalSize = GetOriginalSize(entry);
                                sizeLeft = originalSize;
                                if (instructionSize != 0)
                                {
                                    midInstructionSize = instructionSize;
                                }
                            }
                        }
                    }
                    break;
                }
                case 1:
                {
                    // make large fixes in base block
                    Debug.Assert(inBaseStream is not null);

                    long baseBlockSize = Cas.GetUncompressedSize(inBaseStream);
                    long currentOffset = 0;

                    while (instructionSize-- > 0)
                    {
                        long size = 0;

                        ushort offset = inDeltaStream.ReadUInt16(Endian.Big);
                        ushort skipCount = inDeltaStream.ReadUInt16(Endian.Big);

                        // use base
                        size += (offset - currentOffset);
                        currentOffset = offset;

                        // use delta
                        long deltaBlockSize = Cas.GetUncompressedSize(inDeltaStream);
                        size += deltaBlockSize;

                        sizeLeft -= size;

                        if (sizeLeft <= 0)
                        {
                            Debug.Assert(sizeLeft == 0);

                            AddAsset(inDeltaStream, inBaseStream, inBundle, entry, deltaOffset, baseOffset, midInstructionSize);

                            entry = getNextAsset();
                            if (entry is not null)
                            {
                                originalSize = GetOriginalSize(entry);
                                sizeLeft = originalSize;
                                if (instructionSize != 0)
                                {
                                    midInstructionSize = instructionSize;
                                }
                            }
                        }

                        currentOffset += skipCount;
                    }

                    // fill rest with base block
                    if (baseBlockSize - currentOffset > 0)
                    {
                        sizeLeft -= baseBlockSize - currentOffset;

                        if (sizeLeft <= 0)
                        {
                            Debug.Assert(sizeLeft == 0);

                            AddAsset(inDeltaStream, inBaseStream, inBundle, entry, deltaOffset, baseOffset, midInstructionSize);

                            entry = getNextAsset();
                            if (entry is not null)
                            {
                                originalSize = GetOriginalSize(entry);
                                sizeLeft = originalSize;
                            }
                        }
                    }

                    break;
                }
                case 2:
                {
                    // make small fixes in base block
                    Debug.Assert(inBaseStream is not null);

                    // size of final decompressed data
                    int newBlockSize = inDeltaStream.ReadUInt16(Endian.Big) + 1;

                    // skip base block, since we already know the final decompressed size
                    Cas.GetUncompressedSize(inBaseStream);

                    inDeltaStream.Position += instructionSize;

                    sizeLeft -= newBlockSize;

                    if (sizeLeft <= 0)
                    {
                        Debug.Assert(sizeLeft == 0);

                        AddAsset(inDeltaStream, inBaseStream, inBundle, entry, deltaOffset, baseOffset, midInstructionSize);

                        entry = getNextAsset();
                        if (entry is not null)
                        {
                            originalSize = GetOriginalSize(entry);
                            sizeLeft = originalSize;
                        }
                    }

                    break;
                }
                case 3:
                {
                    // read delta blocks
                    while (instructionSize-- > 0)
                    {
                        sizeLeft -= Cas.GetUncompressedSize(inDeltaStream);

                        if (sizeLeft <= 0)
                        {
                            Debug.Assert(sizeLeft == 0);

                            AddAsset(inDeltaStream, inBaseStream, inBundle, entry, deltaOffset, baseOffset, midInstructionSize);

                            entry = getNextAsset();
                            if (entry is not null)
                            {
                                originalSize = GetOriginalSize(entry);
                                sizeLeft = originalSize;
                                if (instructionSize != 0)
                                {
                                    midInstructionSize = instructionSize;
                                }
                            }
                        }
                    }
                    break;
                }
                case 4:
                {
                    // skip base blocks
                    Debug.Assert(inBaseStream is not null);

                    while (instructionSize-- > 0)
                    {
                        Cas.GetUncompressedSize(inBaseStream);
                    }
                    break;
                }
            }
        }

        if (inBundle.Name ==
            "win32/characters/plants/playable/assaultcorn/faceprop/assaultcorn_faceprop_pinkmetalmaskgold_unlockasset_bpb")
        {

        }

        // ??? this makes no sense
        long extraSize = 0;

        while (inBaseStream?.Position - inBaseOffset <  inBaseSize)
        {
            Debug.Assert(inBaseStream is not null);

            if (entry is null)
            {
                extraSize += Cas.GetUncompressedSize(inBaseStream);
                continue;
            }

            if (entry is not null && sizeLeft == originalSize)
            {
                deltaOffset = (uint)inDeltaStream.Position;
                baseOffset = (uint)(inBaseStream?.Position ?? 0);
                midInstructionSize = -1;
            }
            sizeLeft -= Cas.GetUncompressedSize(inBaseStream);

            if (sizeLeft <= 0)
            {
                Debug.Assert(sizeLeft == 0);

                AddAsset(inDeltaStream, inBaseStream, inBundle, entry, deltaOffset, baseOffset, midInstructionSize);

                entry = getNextAsset();
                if (entry is not null)
                {
                    originalSize = GetOriginalSize(entry);
                    sizeLeft = originalSize;
                }
            }
        }

        if (extraSize != 0)
        {
            // TODO: this is not correct, we definitely fucked up the parsing somewhere
        }
    }

    private static long GetOriginalSize(AssetEntry inEntry) => inEntry is ChunkAssetEntry chunk
        ? (chunk.LogicalOffset & 0xFFFF) | chunk.LogicalSize
        : inEntry.OriginalSize;

    private static void AddAsset(DataStream inDeltaStream, DataStream? inBaseStream, BundleInfo inBundle, AssetEntry? entry,
        uint deltaOffset, uint baseOffset, int midInstructionSize)
    {
        switch (entry)
        {
            case EbxAssetEntry ebx:
                entry.AddFileInfo(new NonCasFileInfo(inBundle.Parent.Name, deltaOffset,
                    (uint)(inDeltaStream.Position - deltaOffset),
                    baseOffset,
                    (uint)(inBaseStream?.Position - baseOffset ?? 0), midInstructionSize));
                AssetManager.AddEbx(ebx, inBundle.Id);
                break;
            case ResAssetEntry res:
                entry.AddFileInfo(new NonCasFileInfo(inBundle.Parent.Name, deltaOffset,
                    (uint)(inDeltaStream.Position - deltaOffset),
                    baseOffset,
                    (uint)(inBaseStream?.Position - baseOffset ?? 0), midInstructionSize));
                AssetManager.AddRes(res, inBundle.Id);
                break;
            case ChunkAssetEntry chunk:
                entry.AddFileInfo(new NonCasFileInfo(inBundle.Parent.Name, deltaOffset,
                    (uint)(inDeltaStream.Position - deltaOffset),
                    baseOffset,
                    (uint)(inBaseStream?.Position - baseOffset ?? 0), midInstructionSize, chunk.LogicalOffset));
                AssetManager.AddChunk(chunk, inBundle.Id);
                break;
        }
    }

    private void LoadBundle(DataStream stream, long inOffset, long inSize, ref BundleInfo bundle, bool isNonCas, bool isDelta = false, bool inIsPatch = false)
    {
        stream.Position = inOffset;

        if (isNonCas)
        {
            LoadNonCasBundle(stream, bundle, inIsPatch);
        }
        else
        {
            LoadCasBundle(stream, bundle, isDelta);
        }

        Debug.Assert(stream.Position == inOffset + inSize, "Didnt read bundle correctly.");
    }

    private static void LoadNonCasBundle(DataStream stream, BundleInfo bundle, bool inIsPatch)
    {
        BinaryBundle bundleMeta = BinaryBundle.Deserialize(stream);

        foreach (EbxAssetEntry ebx in bundleMeta.EbxList)
        {
            uint offset = (uint)stream.Position;
            uint size = (uint)Helper.GetSize(stream, ebx.OriginalSize);
            ebx.AddFileInfo(new NonCasFileInfo(bundle.Parent.Name, inIsPatch, offset, size));

            AssetManager.AddEbx(ebx, bundle.Id);
        }

        foreach (ResAssetEntry res in bundleMeta.ResList)
        {
            uint offset = (uint)stream.Position;
            uint size = (uint)Helper.GetSize(stream, res.OriginalSize);
            res.AddFileInfo(new NonCasFileInfo(bundle.Parent.Name, inIsPatch, offset, size));

            AssetManager.AddRes(res, bundle.Id);
        }

        foreach (ChunkAssetEntry chunk in bundleMeta.ChunkList)
        {
            uint offset = (uint)stream.Position;
            // the size of the range is different than the logical size, since the range wont get decreased further once it fits in one block
            uint size = (uint)Helper.GetSize(stream, (chunk.LogicalOffset & 0xFFFF) | chunk.LogicalSize);
            chunk.AddFileInfo(new NonCasFileInfo(bundle.Parent.Name, inIsPatch, offset, size, chunk.LogicalOffset));

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

                IFileInfo? fileInfo =
                    ResourceManager.GetPatchFileInfo(entry.Sha1, deltaSha1, baseSha1);

                if (fileInfo is not null)
                {
                    entry.AddFileInfo(fileInfo);
                }
            }
            else
            {
                IFileInfo? fileInfo = ResourceManager.GetFileInfo(entry.Sha1);
                if (fileInfo is not null)
                {
                    entry.AddFileInfo(fileInfo);
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

                IFileInfo? fileInfo =
                    ResourceManager.GetPatchFileInfo(entry.Sha1, deltaSha1, baseSha1);

                if (fileInfo is not null)
                {
                    entry.AddFileInfo(fileInfo);
                }
            }
            else
            {
                IFileInfo? fileInfo = ResourceManager.GetFileInfo(entry.Sha1);
                if (fileInfo is not null)
                {
                    entry.AddFileInfo(fileInfo);
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

                IFileInfo? fileInfo =
                    ResourceManager.GetPatchFileInfo(entry.Sha1, deltaSha1, baseSha1);

                if (fileInfo is not null)
                {
                    entry.AddFileInfo(fileInfo);
                }
            }
            else
            {
                IFileInfo? fileInfo = ResourceManager.GetFileInfo(entry.Sha1);
                if (fileInfo is not null)
                {
                    entry.AddFileInfo(fileInfo);
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