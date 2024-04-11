using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using Frosty.Sdk.Exceptions;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Managers.Infos.FileInfos;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.Managers.Loaders;

public enum Code
{
    NotFound,
    Continue,
    Stop
}

public class Manifest2019AssetLoader : IAssetLoader
{
    [Flags]
    private enum Flags
    {
        HasBaseBundles = 1 << 0, // base toc has bundles that the patch doesnt have
        HasBaseChunks = 1 << 1, // base toc has chunks that the patch doesnt have
        HasCompressedNames = 1 << 2 // bundle names are huffman encoded
    }

    private readonly HashSet<Guid> m_removedChunks = new();
    private readonly HashSet<string> m_removedBundles = new();

    public void Load()
    {
        foreach (SuperBundleInfo sbInfo in FileSystemManager.EnumerateSuperBundles())
        {
            foreach (SuperBundleInstallChunk sbIc in sbInfo.InstallChunks)
            {
                m_removedChunks.Clear();
                m_removedBundles.Clear();
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
                    FrostyLogger.Logger?.LogWarning($"Couldn't find SuperBundle \"{sbIc.Name}\"");
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

        using (BlockStream stream = BlockStream.FromFile(path, true))
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

            Flags flags = (Flags)stream.ReadInt32(Endian.Big);

            uint namesCount = 0;
            uint tableCount = 0;
            uint tableOffset = uint.MaxValue;
            HuffmanDecoder? huffmanDecoder = null;

            if (flags.HasFlag(Flags.HasCompressedNames))
            {
                huffmanDecoder = new HuffmanDecoder();
                namesCount = stream.ReadUInt32(Endian.Big);
                tableCount = stream.ReadUInt32(Endian.Big);
                tableOffset = stream.ReadUInt32(Endian.Big);
            }

            if (bundlesCount != 0)
            {
                // stream for loading from sb file
                DataStream? sbStream = null;

                if (flags.HasFlag(Flags.HasCompressedNames))
                {
                    stream.Position = namesOffset;
                    huffmanDecoder!.ReadEncodedData(stream, namesCount, Endian.Big);

                    stream.Position = tableOffset;
                    huffmanDecoder.ReadHuffmanTable(stream, tableCount, Endian.Big);
                }

                stream.Position = bundleDataOffset;

                for (int i = 0; i < bundlesCount; i++)
                {
                    int nameOffset = stream.ReadInt32(Endian.Big);
                    uint bundleSize = stream.ReadUInt32(Endian.Big); // flag in first 2 bits: 0x40000000 inline sb
                    long bundleOffset = stream.ReadInt64(Endian.Big);

                    // get name either from huffman table or raw string table at the end
                    string name;
                    if (flags.HasFlag(Flags.HasCompressedNames))
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

                    Debug.Assert(bundleSize != uint.MaxValue && bundleOffset != -1, "removed?");

                    if (inSbIc.BundleMapping.ContainsKey(name))
                    {
                        continue;
                    }

                    BundleInfo bundle = AssetManager.AddBundle(name, inSbIc);

                    // load bundle
                    byte bundleLoadFlag = (byte)(bundleSize >> 30);
                    bundleSize &= 0x3FFFFFFFU;
                    DataStream bundleStream;
                    switch (bundleLoadFlag)
                    {
                        case 0:
                            bundleStream = sbStream ??= BlockStream.FromFile(path.Replace(".toc", ".sb"), false);
                            break;
                        case 1:
                            bundleStream = stream;
                            break;
                        default:
                            throw new UnknownValueException<byte>("bundle load flag", bundleLoadFlag);
                    }
                    LoadBundle(bundleStream, bundleOffset, bundleSize, ref bundle);
                }
                huffmanDecoder?.Dispose();
                sbStream?.Dispose();
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

                    Guid guid = new(b);

                    int index = stream.ReadInt32(Endian.Big);

                    if (index == -1)
                    {
                        // remove chunk
                        m_removedChunks.Add(guid);
                        continue;
                    }
                    if (m_removedChunks.Contains(guid))
                    {
                        // is removed, just skip
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

                    ChunkAssetEntry chunk = new(guid, Sha1.Zero, 0, 0, Utils.Utils.HashString(inSbIc.Name, true));

                    chunk.AddFileInfo(new CasFileInfo(casFileIdentifier, offset, size, 0));

                    AssetManager.AddSuperBundleChunk(chunk);
                }

                chunkData.Dispose();
            }

            if (flags.HasFlag(Flags.HasBaseBundles) || flags.HasFlag(Flags.HasBaseChunks))
            {
                return Code.Continue;
            }
        }

        return Code.Stop;
    }

    private void LoadBundle(DataStream stream, long inOffset, uint inSize, ref BundleInfo bundle)
    {
        long curPos = stream.Position;

        stream.Position = inOffset;

        int bundleOffset = stream.ReadInt32(Endian.Big);
        int bundleSize = stream.ReadInt32(Endian.Big);
        uint locationOffset = stream.ReadUInt32(Endian.Big);
        int totalCount = stream.ReadInt32(Endian.Big);
        uint dataOffset = stream.ReadUInt32(Endian.Big);

        // not used by any game rn, again maybe crypto stuff
        stream.Position += sizeof(uint);
        stream.Position += sizeof(uint);
        // maybe count for the offsets above
        stream.Position += sizeof(int);

        // bundles can be stored in this file or in a separate cas file, then the first file info is for the bundle.
        // Seems to be related to the flag for in which file the sb is stored
        bool inlineBundle = !(bundleOffset == 0 && bundleSize == 0);

        stream.Position = inOffset + locationOffset;

        Block<byte> fileIdentifierFlags = new(totalCount);
        stream.ReadExactly(fileIdentifierFlags);

        // the flags should be the last thing in the bundle
        Debug.Assert(stream.Position == inOffset + inSize, "Didnt read bundle correctly.");

        CasFileIdentifier file = default;
        int currentIndex = 0;

        // load the bundle meta
        BinaryBundle bundleMeta;
        if (inlineBundle)
        {
            stream.Position = inOffset + bundleOffset;
            bundleMeta = BinaryBundle.Deserialize(stream);
            Debug.Assert(stream.Position == inOffset + bundleOffset + bundleSize, "We did not read the bundle meta completely");

            // go to the start of the data
            stream.Position = inOffset + dataOffset;
        }
        else
        {
            stream.Position = inOffset + dataOffset;
            file = ReadCasFileIdentifier(stream, fileIdentifierFlags[currentIndex++], file);
            uint offset = stream.ReadUInt32(Endian.Big);
            int size = stream.ReadInt32(Endian.Big);
            string path = FileSystemManager.GetFilePath(file);
            if (string.IsNullOrEmpty(path))
            {
                throw new Exception("Corrupted data. File for bundle does not exist.");
            }
            using (BlockStream bundleStream = BlockStream.FromFile(path, offset, size))
            {
                bundleMeta = BinaryBundle.Deserialize(bundleStream);

                Debug.Assert(bundleStream.Position == bundleStream.Length, "We did not read the bundle meta completely");
            }
        }

        // load assets from bundle
        foreach (EbxAssetEntry ebx in bundleMeta.EbxList)
        {
            file = ReadCasFileIdentifier(stream, fileIdentifierFlags[currentIndex++], file);

            ebx.AddFileInfo(new CasFileInfo(file, stream.ReadUInt32(Endian.Big), stream.ReadUInt32(Endian.Big), 0));

            AssetManager.AddEbx(ebx, bundle.Id);
        }

        foreach (ResAssetEntry res in bundleMeta.ResList)
        {
            file = ReadCasFileIdentifier(stream, fileIdentifierFlags[currentIndex++], file);

            res.AddFileInfo(new CasFileInfo(file, stream.ReadUInt32(Endian.Big), stream.ReadUInt32(Endian.Big), 0));

            AssetManager.AddRes(res, bundle.Id);
        }

        foreach (ChunkAssetEntry chunk in bundleMeta.ChunkList)
        {
            file = ReadCasFileIdentifier(stream, fileIdentifierFlags[currentIndex++], file);

            chunk.AddFileInfo(new CasFileInfo(file, stream.ReadUInt32(Endian.Big), stream.ReadUInt32(Endian.Big), chunk.LogicalOffset));

            AssetManager.AddChunk(chunk, bundle.Id);
        }

        fileIdentifierFlags.Dispose();

        stream.Position = curPos;
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
}