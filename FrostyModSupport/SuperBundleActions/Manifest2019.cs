using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Frosty.ModSupport.Archive;
using Frosty.ModSupport.ModEntries;
using Frosty.ModSupport.ModInfos;
using Frosty.ModSupport.Utils;
using Frosty.Sdk;
using Frosty.Sdk.Exceptions;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Managers.Loaders;
using Frosty.Sdk.Utils;
using Microsoft.Extensions.Logging;

namespace Frosty.ModSupport;

internal class Manifest2019 : IDisposable
{
    private class StringHelper
    {
        public class String
        {
            public uint Offset { get; set; }
            public string Value { get; }

            public String(string inValue)
            {
                Value = inValue;
            }
        }

        public bool EncodeStrings;
        private uint m_currentOffset;
        private readonly Dictionary<string, String> m_mapping = new();
        public IList<uint>? Tree;
        public byte[]? Data;

        public String AddString(string inString)
        {
            if (m_mapping.TryGetValue(inString, out String? retVal))
            {
                return retVal;
            }

            retVal = new String(inString) { Offset = m_currentOffset };
            m_currentOffset += (uint)(inString.Length + 1);

            m_mapping.Add(inString, retVal);

            return retVal;
        }

        public void Fixup()
        {
            if (!EncodeStrings)
            {
                return;
            }

            EncodingResult result = HuffmanEncoder.Encode(m_mapping.Keys, Endian.Big);

            foreach (IdentifierPositionTuple<string> position in result.EncodedTextPositions)
            {
                m_mapping[position.Identifier].Offset = (uint)position.Position;
            }

            Tree = result.EncodingTree;
            Data = result.EncodedTexts;
        }

        public void Write(DataStream inStream)
        {
            List<String> sorted = new(m_mapping.Values);
            sorted.Sort(((a, b) => a.Offset.CompareTo(b.Offset)));
            foreach (String s in sorted)
            {
                inStream.WriteNullTerminatedString(s.Value);
            }
        }
    }

    public Block<byte>? TocData { get; private set; }
    public Block<byte>? SbData { get; private set; }

    private readonly Dictionary<string, EbxModEntry> m_modifiedEbx;
    private readonly Dictionary<string, ResModEntry> m_modifiedRes;
    private readonly Dictionary<Guid, ChunkModEntry> m_modifiedChunks;

    public Manifest2019(Dictionary<string, EbxModEntry> inModifiedEbx,
        Dictionary<string, ResModEntry> inModifiedRes, Dictionary<Guid, ChunkModEntry> inModifiedChunks)
    {
        m_modifiedEbx = inModifiedEbx;
        m_modifiedRes = inModifiedRes;
        m_modifiedChunks = inModifiedChunks;
    }

    public void ModSuperBundle(string inPath, bool inCreateNewPatch, SuperBundleInstallChunk inSbIc, SuperBundleModInfo inModInfo, InstallChunkWriter inInstallChunkWriter)
    {
        List<(StringHelper.String, uint, long)> bundles = new();
        List<(Guid, CasFileIdentifier, uint, uint)> chunks = new();
        StringHelper stringHelper;

        byte bundleLoadFlag;

        stringHelper = new StringHelper();

        Block<byte> modifiedSuperBundle = new(0);
        using (BlockStream modifiedStream = new(modifiedSuperBundle, true))
        {
            bundleLoadFlag = ProcessBundles(inPath, inCreateNewPatch, inSbIc, inModInfo, inInstallChunkWriter, stringHelper, modifiedStream, bundles, chunks);

            // if we modify some bundles that are not in the patch we need to parse the base superbundle as well
            if (inModInfo.Modified.Bundles.Count > 0 || inModInfo.Modified.Chunks.Count > 0)
            {
                string basePath = FileSystemManager.ResolvePath(false, $"{inSbIc.Name}.toc");
                ProcessBundles(basePath, true, inSbIc, inModInfo, inInstallChunkWriter, stringHelper, modifiedStream, bundles, chunks);
            }

            Debug.Assert(inModInfo.Modified.Bundles.Count == 0 && inModInfo.Modified.Chunks.Count == 0);
        }

        if (bundleLoadFlag == 0)
        {
            SbData = modifiedSuperBundle;
        }

        stringHelper.Fixup();

        TocData = new Block<byte>(44 + (stringHelper.EncodeStrings ? 12 : 0) + bundles.Count * 20 + chunks.Count * (36) + (bundleLoadFlag == 1 ? modifiedSuperBundle.Size : 0));
        TocData.Clear();
        using (BlockStream stream = new(TocData, true))
        {
            stream.WriteUInt32(stringHelper.EncodeStrings ? 0x3Cu : 0x30u, Endian.Big); // bundleHashMap
            stream.WriteUInt32(0xdeadbeef); // bundles
            stream.WriteInt32(bundles.Count, Endian.Big);

            stream.WriteUInt32(0xdeadbeef); // chunkHashMap
            stream.WriteUInt32(0xdeadbeef); // chunks
            stream.WriteInt32(chunks.Count, Endian.Big);

            stream.WriteUInt32(0xdeadbeef); // unused
            stream.WriteUInt32(0xdeadbeef); // unused

            stream.WriteUInt32(0xdeadbeef); // stringsOffset

            stream.WriteUInt32(0xdeadbeef); // chunkData
            stream.WriteUInt32(0xdeadbeef); // dataCount

            Manifest2019AssetLoader.Flags flags = 0;
            if (stringHelper.EncodeStrings)
            {
                flags |= Manifest2019AssetLoader.Flags.HasCompressedNames;
            }
            if (FileSystemManager.Sources.Count > 1)
            {
                flags |= Manifest2019AssetLoader.Flags.HasBaseBundles | Manifest2019AssetLoader.Flags.HasBaseChunks;
            }

            stream.WriteInt32((int)flags, Endian.Big);

            if (stringHelper.EncodeStrings)
            {
                stream.WriteUInt32(0xdeadbeef); // stringCount
                stream.WriteUInt32(0xdeadbeef); // tableCount
                stream.WriteUInt32(0xdeadbeef); // tableOffset
            }

            List<int> hashMap = HashMap.CreateHashMap(ref bundles, (bundle, count, initial) =>
            {
                Span<byte> b = stackalloc byte[bundle.Item1.Value.Length];
                Encoding.ASCII.GetBytes(bundle.Item1.Value.ToLower(), b);
                return HashMap.GetIndex(b, count, initial);
            });

            foreach (int hash in hashMap)
            {
                stream.WriteInt32(hash, Endian.Big);
            }

            stream.Pad(8);

            uint bundlesOffset = (uint)stream.Position;
            foreach ((StringHelper.String, uint, long) bundle in bundles)
            {
                stream.WriteUInt32(bundle.Item1.Offset, Endian.Big);
                stream.WriteUInt32(bundle.Item2 | (uint)(bundleLoadFlag << 30), Endian.Big);
                stream.WriteInt64(bundle.Item3, Endian.Big);
            }

            stream.Pad(4);
            uint chunkHashMapOffset = (uint)stream.Position;

            hashMap = HashMap.CreateHashMap(ref chunks, (chunk, count, initial) =>
            {
                Span<byte> b = chunk.Item1.ToByteArray();
                return HashMap.GetIndex(b, count, initial);
            });

            foreach (int hash in hashMap)
            {
                stream.WriteInt32(hash, Endian.Big);
            }

            stream.Pad(8);
            uint chunksOffset = (uint)stream.Position;

            List<uint> chunkData = new(chunks.Count * 4);
            foreach ((Guid Id, CasFileIdentifier Identifier, uint Offset, uint Size) chunk in chunks)
            {
                // TODO: maybe write some helper that does this better
                Span<byte> buffer = chunk.Id.ToByteArray();
                buffer.Reverse();
                stream.Write(buffer);

                int index = chunkData.Count;

                int flag;
                if ((chunk.Identifier.InstallChunkIndex & ~byte.MaxValue) != 0)
                {
                    ulong b = CasFileIdentifier.ToFileIdentifierLong(chunk.Identifier);
                    chunkData.Add((uint)(b >> 32));
                    chunkData.Add((uint)(b & uint.MaxValue));
                    flag = 0x80;
                }
                else
                {
                    chunkData.Add(CasFileIdentifier.ToFileIdentifier(chunk.Identifier));
                    flag = 1;
                }

                stream.WriteInt32(index | (flag << 24), Endian.Big);

                chunkData.Add(chunk.Offset);
                chunkData.Add(chunk.Size);

            }

            stream.Pad(8);
            uint chunkDataOffset = (uint)stream.Position;

            foreach (uint data in chunkData)
            {
                stream.WriteUInt32(data, Endian.Big);
            }

            uint stringsOffset = (uint)stream.Position;
            if (stringHelper.EncodeStrings)
            {
                stream.Write(stringHelper.Data!);

                Debug.Assert((stringHelper.Data!.Length & 3) == 0, "Huffman not padded!");

                foreach (uint node in stringHelper.Tree!)
                {
                    stream.WriteUInt32(node, Endian.Big);
                }
            }
            else
            {
                stringHelper.Write(stream);
            }

            if (bundleLoadFlag == 1)
            {
                stream.Pad(4);
                long sbOffset = stream.Position;
                stream.Write(modifiedSuperBundle);

                // fixup bundle offset if its inline
                stream.Position = bundlesOffset;
                foreach ((StringHelper.String, uint, long) bundle in bundles)
                {
                    stream.Position += 8;
                    stream.WriteInt64(bundle.Item3 + sbOffset, Endian.Big);
                }
            }

            // fixup offsets
            stream.Position = 4; // bundleHashMap
            stream.WriteUInt32(bundlesOffset, Endian.Big);
            stream.Position += 4; // bundleCount
            stream.WriteUInt32(chunkHashMapOffset, Endian.Big);
            stream.WriteUInt32(chunksOffset, Endian.Big);
            stream.Position += 4; // chunkCount
            stream.WriteUInt32(chunkDataOffset, Endian.Big); // unused
            stream.WriteUInt32(chunkDataOffset, Endian.Big); // unused
            stream.WriteUInt32(stringsOffset, Endian.Big);
            stream.WriteUInt32(chunkDataOffset, Endian.Big); // unused
            stream.WriteInt32(chunkData.Count, Endian.Big);

            if (stringHelper.EncodeStrings)
            {
                stream.Position += 4;
                stream.WriteInt32((stringHelper.Data!.Length + 3 & ~3) / 4, Endian.Big);
                stream.WriteInt32(stringHelper.Tree!.Count, Endian.Big);
                stream.WriteUInt32(stringsOffset + ((uint)stringHelper.Data!.Length + 3u & ~3u), Endian.Big);
            }
        }
    }

    private byte ProcessBundles(string inPath, bool inOnlyUseModified, SuperBundleInstallChunk inSbIc,
        SuperBundleModInfo inModInfo, InstallChunkWriter inInstallChunkWriter, StringHelper inStringHelper,
        BlockStream inModifiedStream, List<(StringHelper.String, uint, long)> inBundles, List<(Guid, CasFileIdentifier, uint, uint)> inChunks)
    {
        byte bundleLoadFlag = 0;
        using (BlockStream stream = BlockStream.FromFile(inPath, true))
        {
            stream.Position += sizeof(uint); // bundleHashMapOffset
            uint bundleDataOffset = stream.ReadUInt32(Endian.Big);
            int bundlesCount = stream.ReadInt32(Endian.Big);

            stream.Position += sizeof(uint); // chunkHashMapOffset
            uint chunkGuidOffset = stream.ReadUInt32(Endian.Big);
            int chunksCount = stream.ReadInt32(Endian.Big);

            // not used by any game rn, maybe crypto stuff
            stream.Position += sizeof(uint);
            stream.Position += sizeof(uint);

            uint namesOffset = stream.ReadUInt32(Endian.Big);

            uint chunkDataOffset = stream.ReadUInt32(Endian.Big);
            int dataCount = stream.ReadInt32(Endian.Big);

            Manifest2019AssetLoader.Flags flags = (Manifest2019AssetLoader.Flags)stream.ReadInt32(Endian.Big);

            uint namesCount = 0;
            uint tableCount = 0;
            uint tableOffset = uint.MaxValue;
            HuffmanDecoder? huffmanDecoder = null;

            if (flags.HasFlag(Manifest2019AssetLoader.Flags.HasCompressedNames))
            {
                // TODO: encoding broken atm fix it then add this again
                // inStringHelper.EncodeStrings = true;
                huffmanDecoder = new HuffmanDecoder();
                namesCount = stream.ReadUInt32(Endian.Big);
                tableCount = stream.ReadUInt32(Endian.Big);
                tableOffset = stream.ReadUInt32(Endian.Big);
            }

            if (bundlesCount != 0)
            {
                if (flags.HasFlag(Manifest2019AssetLoader.Flags.HasCompressedNames))
                {
                    stream.Position = namesOffset;
                    huffmanDecoder!.ReadEncodedData(stream, namesCount, Endian.Big);

                    stream.Position = tableOffset;
                    huffmanDecoder.ReadHuffmanTable(stream, tableCount, Endian.Big);
                }

                stream.Position = bundleDataOffset;
                BlockStream? sbStream = null;
                for (int i = 0; i < bundlesCount; i++)
                {
                    int nameOffset = stream.ReadInt32(Endian.Big);
                    uint bundleSize = stream.ReadUInt32(Endian.Big);
                    long bundleOffset = stream.ReadInt64(Endian.Big);

                    // get name either from huffman table or raw string table at the end
                    string name;
                    if (flags.HasFlag(Manifest2019AssetLoader.Flags.HasCompressedNames))
                    {
                        name = huffmanDecoder!.ReadHuffmanEncodedString(nameOffset);
                    }
                    else
                    {
                        long curPos = stream.Position;
                        stream.Position = namesOffset + nameOffset;
                        name = stream.ReadNullTerminatedString();
                        stream.Position = curPos;
                    }

                    int id = Frosty.Sdk.Utils.Utils.HashString(name + inSbIc.Name, true);
                    bundleLoadFlag = (byte)(bundleSize >> 30);
                    bundleSize &= 0x3FFFFFFFU;

                    inModifiedStream.Pad(4);
                    long newOffset = inModifiedStream.Position;
                    uint newBundleSize;

                    if (!inModInfo.Modified.Bundles.TryGetValue(id, out BundleModInfo? bundleModInfo))
                    {
                        if (inOnlyUseModified)
                        {
                            // we create a new patch to a base superbundle, so we only need modified bundles
                            continue;
                        }

                        // load and write unmodified bundle
                        newBundleSize = bundleSize;
                        switch (bundleLoadFlag)
                        {
                            case 0:
                                sbStream ??= BlockStream.FromFile(inPath.Replace(".toc", ".sb"), false);
                                sbStream.Position = bundleOffset;
                                sbStream.CopyTo(inModifiedStream, (int)bundleSize);
                                break;
                            case 1:
                                long curPos = stream.Position;
                                stream.Position = bundleOffset;
                                stream.CopyTo(inModifiedStream, (int)bundleSize);
                                stream.Position = curPos;
                                break;
                            default:
                                throw new UnknownValueException<byte>("bundle load flag", bundleLoadFlag);
                        }
                    }
                    else
                    {
                        // load, modify and write bundle
                        BlockStream bundleStream;
                        switch (bundleLoadFlag)
                        {
                            case 0:
                                sbStream ??= BlockStream.FromFile(inPath.Replace(".toc", ".sb"), false);
                                bundleStream = sbStream;
                                break;
                            case 1:
                                bundleStream = stream;
                                break;
                            default:
                                throw new UnknownValueException<byte>("bundle load flag", bundleLoadFlag);
                        }

                        (Block<byte> BundleMeta, List<(CasFileIdentifier, uint, uint)> Files, bool IsInline)
                            bundle = ModifyBundle(bundleStream, bundleOffset, bundleModInfo,
                                inInstallChunkWriter);

                        Block<byte> data = WriteModifiedBundle(bundle, inInstallChunkWriter);
                        inModifiedStream.Write(data);
                        newBundleSize = (uint)data.Size;
                        data.Dispose();

                        // remove bundle so we can check if the base superbundle needs to be loaded to modify a base bundle
                        inModInfo.Modified.Bundles.Remove(id);
                    }

                    // add new bundle to toc
                    inBundles.Add((inStringHelper.AddString(name), newBundleSize, newOffset));
                }
                huffmanDecoder?.Dispose();
                sbStream?.Dispose();
            }

            foreach (BundleModInfo bundleModInfo in inModInfo.Added.Bundles.Values)
            {
                FrostyLogger.Logger?.LogError("Adding bundles not yet implemented.");
            }

            if (chunksCount != 0)
            {
                stream.Position = chunkDataOffset;
                Block<uint> chunkData = new(dataCount);
                stream.ReadExactly(chunkData.ToBlock<byte>());

                stream.Position = chunkGuidOffset;
                Span<byte> b = stackalloc byte[16];
                for (int i = 0; i < chunksCount; i++)
                {
                    stream.ReadExactly(b);
                    b.Reverse();

                    Guid id = new(b);

                    int index = stream.ReadInt32(Endian.Big);

                    if (index == -1)
                    {
                        // removed chunk
                        continue;
                    }

                    byte fileIdentifierFlag = (byte)(index >> 24);

                    index &= 0x00FFFFFF;

                    CasFileIdentifier casFileIdentifier;
                    if (fileIdentifierFlag == 1)
                    {
                        casFileIdentifier = CasFileIdentifier.FromFileIdentifier(BinaryPrimitives.ReverseEndianness(chunkData[index++]));
                    }
                    else if (fileIdentifierFlag == 0x80)
                    {
                        casFileIdentifier = CasFileIdentifier.FromFileIdentifier(BinaryPrimitives.ReverseEndianness(chunkData[index++]), BinaryPrimitives.ReverseEndianness(chunkData[index++]));
                    }
                    else
                    {
                        throw new UnknownValueException<byte>("file identifier flag", fileIdentifierFlag);
                    }

                    uint offset = BinaryPrimitives.ReverseEndianness(chunkData[index++]);
                    uint size = BinaryPrimitives.ReverseEndianness(chunkData[index]);

                    if (!inModInfo.Modified.Chunks.Contains(id))
                    {
                        if (inOnlyUseModified)
                        {
                            // we create a new patch to a base superbundle, so we only need modified chunks
                            continue;
                        }

                        // use the original info
                    }
                    else
                    {
                        // modify chunk
                        (CasFileIdentifier, uint, uint) info = inInstallChunkWriter.GetFileInfo(m_modifiedChunks[id].Sha1);

                        casFileIdentifier = info.Item1;
                        offset = info.Item2;
                        size = info.Item3;

                        // remove chunk so we can check if the base superbundle needs to be loaded to modify a base chunk
                        inModInfo.Modified.Chunks.Remove(id);
                    }

                    inChunks.Add((id, casFileIdentifier, offset, size));
                }

                chunkData.Dispose();
            }

            foreach (Guid id in inModInfo.Added.Chunks)
            {
                // add chunk
                (CasFileIdentifier, uint, uint) info = inInstallChunkWriter.GetFileInfo(m_modifiedChunks[id].Sha1);

                inChunks.Add((id, info.Item1, info.Item2, info.Item3));
            }
        }
        return bundleLoadFlag;
    }

    private static Block<byte> WriteModifiedBundle((Block<byte> BundleMeta, List<(CasFileIdentifier, uint, uint)> Files, bool IsInline) inBundle, InstallChunkWriter inInstallChunkWriter)
    {
        Block<byte> retVal = new(0);
        using (BlockStream bundleWriter = new(retVal, true))
        {
            bundleWriter.WriteUInt32(inBundle.IsInline ? 0xDEADBEEF : 0, Endian.Big);
            bundleWriter.WriteInt32(inBundle.IsInline ? inBundle.BundleMeta.Size : 0, Endian.Big);
            bundleWriter.WriteUInt32(0xDEADBEEF, Endian.Big); // fileIdentifiedFlags offset
            bundleWriter.WriteInt32(inBundle.Files.Count, Endian.Big);
            bundleWriter.WriteUInt32(0xDEADBEEF, Endian.Big); // fileIdentifier offset
            bundleWriter.WriteUInt32(0xDEADBEEF, Endian.Big); // unused
            bundleWriter.WriteUInt32(0xDEADBEEF, Endian.Big); // unused
            bundleWriter.WriteUInt32(0, Endian.Big); // unused
            if (ProfilesLibrary.FrostbiteVersion >= "2023")
            {
                bundleWriter.WriteUInt32(0, Endian.Big); // unknown
            }

            uint bundleMetaOffset = (uint)bundleWriter.Position;
            if (inBundle.IsInline)
            {
                bundleWriter.Write(inBundle.BundleMeta);
                bundleWriter.Pad(4);
            }
            else
            {
                inBundle.Files[0] =
                    inInstallChunkWriter.WriteData(Frosty.Sdk.Utils.Utils.GenerateSha1(inBundle.BundleMeta), inBundle.BundleMeta);
            }

            inBundle.BundleMeta.Dispose();

            byte[] fileFlags = new byte[inBundle.Files.Count];
            long fileIdentifierOffset = bundleWriter.Position;
            CasFileIdentifier current = default;
            for (int j = 0; j < inBundle.Files.Count; j++)
            {
                (CasFileIdentifier, uint, uint) file = inBundle.Files[j];
                if (file.Item1 == current && j != 0)
                {
                    fileFlags[j] = 0;
                }
                else
                {
                    if ((file.Item1.InstallChunkIndex & ~byte.MaxValue) != 0)
                    {
                        fileFlags[j] = 0x80;
                        bundleWriter.WriteUInt64(CasFileIdentifier.ToFileIdentifierLong(file.Item1), Endian.Big);
                    }
                    else
                    {
                        fileFlags[j] = 1;
                        bundleWriter.WriteUInt32(CasFileIdentifier.ToFileIdentifier(file.Item1), Endian.Big);
                    }

                    current = file.Item1;
                }

                bundleWriter.WriteUInt32(file.Item2, Endian.Big);
                bundleWriter.WriteUInt32(file.Item3, Endian.Big);
            }

            long fileFlagOffset = bundleWriter.Position;
            foreach (byte flag in fileFlags)
            {
                bundleWriter.WriteByte(flag);
            }

            if (inBundle.IsInline)
            {
                bundleWriter.Position = 0;
                bundleWriter.WriteUInt32(bundleMetaOffset, Endian.Big);
            }
            bundleWriter.Position = 8;
            bundleWriter.WriteUInt32((uint)fileFlagOffset, Endian.Big);
            bundleWriter.Position = 16;
            bundleWriter.WriteUInt32((uint)fileIdentifierOffset, Endian.Big);
            bundleWriter.WriteUInt32((uint)fileIdentifierOffset, Endian.Big);
            bundleWriter.WriteUInt32((uint)fileIdentifierOffset, Endian.Big);
        }

        return retVal;
    }

    private (Block<byte>, List<(CasFileIdentifier, uint, uint)>, bool) ModifyBundle(BlockStream inStream,
        long inOffset, BundleModInfo inModInfo, InstallChunkWriter inInstallChunkWriter)
    {
        long curPos = inStream.Position;

        inStream.Position = inOffset;

        int bundleOffset = inStream.ReadInt32(Endian.Big);
        int bundleSize = inStream.ReadInt32(Endian.Big);
        uint locationOffset = inStream.ReadUInt32(Endian.Big);
        int totalCount = inStream.ReadInt32(Endian.Big);
        uint dataOffset = inStream.ReadUInt32(Endian.Big);

        // not used by any game rn, again maybe crypto stuff
        inStream.Position += sizeof(uint);
        inStream.Position += sizeof(uint);
        // maybe count for the offsets above
        inStream.Position += sizeof(int);

        bool inlineBundle = !(bundleOffset == 0 && bundleSize == 0);

        inStream.Position = inOffset + locationOffset;

        Block<byte> fileIdentifierFlags = new(totalCount);
        inStream.ReadExactly(fileIdentifierFlags);

        inStream.Position = inOffset + dataOffset;

        CasFileIdentifier file = default;

        List<(CasFileIdentifier, uint, uint)> files = new(totalCount);
        for (int i = 0; i < totalCount; i++)
        {
            file = ReadCasFileIdentifier(inStream, fileIdentifierFlags[i], file);

            files.Add((file, inStream.ReadUInt32(Endian.Big), inStream.ReadUInt32(Endian.Big)));
        }

        Block<byte> bundleMeta;
        if (inlineBundle)
        {
            inStream.Position = inOffset + bundleOffset;
            bundleMeta = BinaryBundle.Modify(inStream, inModInfo, m_modifiedEbx, m_modifiedRes, m_modifiedChunks,
                (entry, i, isAdded, isModified, _) =>
                {
                    if (!isModified)
                    {
                        return;
                    }

                    (CasFileIdentifier File, uint Offset, uint Size) info = inInstallChunkWriter.GetFileInfo(entry.Sha1);
                    if (entry is ChunkModEntry chunk && chunk.FirstMip > 0)
                    {
                        info.Offset += chunk.RangeStart;
                        info.Size = chunk.RangeEnd - chunk.RangeStart;
                    }

                    if (isAdded)
                    {
                        files.Insert(i, info);
                    }
                    else
                    {
                        files[i] = info;
                    }
                });
        }
        else
        {
            string path = FileSystemManager.GetFilePath(files[0].Item1);
            if (string.IsNullOrEmpty(path))
            {
                throw new Exception("Corrupted data. File for bundle does not exist.");
            }
            using (BlockStream bundleStream = BlockStream.FromFile(path, files[0].Item2, (int)files[0].Item3))
            {
                bundleMeta = BinaryBundle.Modify(bundleStream, inModInfo, m_modifiedEbx, m_modifiedRes, m_modifiedChunks,
                    (entry, i, isAdded, isModified, _) =>
                    {
                        if (!isModified)
                        {
                            return;
                        }
                        (CasFileIdentifier File, uint Offset, uint Size) info = inInstallChunkWriter.GetFileInfo(entry.Sha1);
                        if (entry is ChunkModEntry chunk && chunk.FirstMip > 0)
                        {
                            info.Offset += chunk.RangeStart;
                            info.Size = chunk.RangeEnd - chunk.RangeStart;
                        }

                        if (isAdded)
                        {
                            files.Insert(i + 1, info);
                        }
                        else
                        {
                            files[i + 1] = info;
                        }
                    });
                Debug.Assert(bundleStream.Position == bundleStream.Length, "We did not read the bundle meta completely");
            }
        }

        fileIdentifierFlags.Dispose();

        inStream.Position = curPos;

        return (bundleMeta, files, inlineBundle);
    }

    private CasFileIdentifier ReadCasFileIdentifier(DataStream stream, byte inFlag, CasFileIdentifier current)
    {
        switch (inFlag)
        {
            case 0:
                return current;
            case 1:
                return CasFileIdentifier.FromFileIdentifier(stream.ReadUInt32(Endian.Big));
            case 0x80:
                return CasFileIdentifier.FromFileIdentifier(stream.ReadUInt32(Endian.Big), stream.ReadUInt32(Endian.Big));
            default:
                throw new UnknownValueException<byte>("file identifier flag", inFlag);
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
    private void ModManifest2019(SuperBundleInstallChunk inSbIc, SuperBundleModInfo inModInfo, InstallChunkWriter inInstallChunkWriter)
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

        using (Manifest2019 action = new(m_modifiedEbx, m_modifiedRes, m_modifiedChunks))
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