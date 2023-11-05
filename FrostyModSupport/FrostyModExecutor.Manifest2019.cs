using System.Diagnostics;
using Frosty.ModSupport.Archive;
using Frosty.ModSupport.ModEntries;
using Frosty.ModSupport.ModInfos;
using Frosty.Sdk.DbObjectElements;
using Frosty.Sdk.Exceptions;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Managers.Loaders;
using Frosty.Sdk.Utils;

namespace Frosty.ModSupport;

public partial class FrostyModExecutor
{
    private void ModManifest2019(SuperBundleInstallChunk inSbIc, SuperBundleModInfo inModInfo, InstallChunkWriter inInstallChunkWriter)
    {
        string tocPath = Path.Combine(m_gamePatchPath, $"{inSbIc.Name}.toc");
        if (!File.Exists(tocPath))
        {
            
        }

        List< (string, uint, long)> bundles = new();
        List<(Guid, int, CasFileIdentifier, uint, uint)> chunks = new();
        
        using (DataStream newBundleStream = new(new MemoryStream()))
        {
            using (BlockStream stream = BlockStream.FromFile(tocPath, true))
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
                        
                        int id = Utils.HashString(name + inSbIc.Name, true);
                        byte bundleLoadFlag = (byte)(bundleSize >> 30);
                        bundleSize &= 0x3FFFFFFFU;
                        
                        bundles.Add((name, (uint)newBundleStream.Position, bundleSize));
                        
                        if (!inModInfo.Modified.Bundles.TryGetValue(id, out BundleModInfo? bundleModInfo))
                        {
                            // load and write unmodified bundle
                            switch (bundleLoadFlag)
                            {
                                case 0:
                                    sbStream ??= BlockStream.FromFile(tocPath.Replace(".toc", ".sb"), false);
                                    sbStream.Position = bundleOffset;
                                    sbStream.CopyTo(newBundleStream, (int)bundleSize);
                                    break;
                                case 1:
                                    long curPos = stream.Position;
                                    stream.Position = bundleOffset;
                                    stream.CopyTo(newBundleStream, (int)bundleSize);
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
                                    sbStream ??= BlockStream.FromFile(tocPath.Replace(".toc", ".sb"), false);
                                    bundleStream = sbStream;
                                    break;
                                case 1:
                                    bundleStream = stream;
                                    break;
                                default:
                                    throw new UnknownValueException<byte>("bundle load flag", bundleLoadFlag);
                            }

                            (Block<byte> BundleMeta, List<(CasFileIdentifier, uint, uint)> Files, bool IsInline) bundle = LoadBundle(bundleStream, bundleOffset, bundleModInfo, inInstallChunkWriter);

                            // TODO: write modified bundle
                            using (DataStream bundleWriter = new(new MemoryStream()))
                            {
                                bundleWriter.WriteInt32(bundle.IsInline ? 0x20 : 0, Endian.Big);
                                bundleWriter.WriteInt32(bundle.IsInline ? bundle.BundleMeta.Size : 0, Endian.Big);
                                bundleWriter.WriteUInt32(0xDEADBEEF, Endian.Big); // fileIdentifiedFlags offset
                                bundleWriter.WriteInt32(bundle.Files.Count, Endian.Big);
                                bundleWriter.WriteUInt32(0xDEADBEEF, Endian.Big); // fileIdentifier offset
                                bundleWriter.WriteUInt32(0xDEADBEEF, Endian.Big); // unused
                                bundleWriter.WriteUInt32(0xDEADBEEF, Endian.Big); // unused
                                bundleWriter.WriteUInt32(0, Endian.Big); // unused

                                if (bundle.IsInline)
                                {
                                    bundleWriter.Write(bundle.BundleMeta);
                                }
                                else
                                {
                                    bundle.Files[0] = inInstallChunkWriter.WriteData(Utils.GenerateSha1(bundle.BundleMeta), bundle.BundleMeta);
                                }

                                bundleWriter.Pad(4);

                                byte[] fileFlags = new byte[bundle.Files.Count];
                                long fileIdentifierOffset = bundleWriter.Position;
                                CasFileIdentifier current = default;
                                for (int j = 0; j < bundle.Files.Count; j++)
                                {
                                    (CasFileIdentifier, uint, uint) file = bundle.Files[j];
                                    if (file.Item1 == current && j != 0)
                                    {
                                        fileFlags[j] = 0;
                                    }
                                    else
                                    {
                                        if (file.Item1.InstallChunkIndex > byte.MaxValue)
                                        {
                                            fileFlags[j] = 0x80;
                                            bundleWriter.WriteUInt64(CasFileIdentifier.ToFileIdentifierLong(file.Item1), Endian.Big);
                                        }
                                        else
                                        {
                                            fileFlags[j] = 1;
                                            bundleWriter.WriteUInt32(CasFileIdentifier.ToFileIdentifier(file.Item1), Endian.Big);
                                        }
                                        
                                        
                                    }
                                    bundleWriter.WriteUInt32(file.Item2, Endian.Big);
                                    bundleWriter.WriteUInt32(file.Item3, Endian.Big);
                                }

                                long fileFlagOffset = bundleWriter.Position;
                                foreach (byte flag in fileFlags)
                                {
                                    bundleWriter.WriteByte(flag);
                                }

                                bundleWriter.Position = 8;
                                bundleWriter.WriteUInt32((uint)fileFlagOffset);
                                bundleWriter.Position = 16;
                                bundleWriter.WriteUInt32((uint)fileIdentifierOffset);
                            }
                            
                            bundle.BundleMeta.Dispose();
                            
                            // remove bundle so we can check if the base superbundle needs to be loaded to modify a base bundle
                            inModInfo.Modified.Bundles.Remove(id);
                        }
                    }
                    huffmanDecoder?.Dispose();
                }
            }
        }

        bool encodeStrings = true;
        uint offset = encodeStrings ? 0x3Cu : 0x30u;
        using (DataStream stream = new(new MemoryStream()))
        {
            stream.WriteUInt32(offset, Endian.Big);
            offset += (uint)bundles.Count * sizeof(int);
            stream.WriteUInt32(offset, Endian.Big);
            offset += (uint)bundles.Count * (sizeof(int) + sizeof(uint) + sizeof(long));
            stream.WriteInt32(bundles.Count, Endian.Big);
            
            stream.WriteUInt32(offset, Endian.Big);
            offset += (uint)chunks.Count * sizeof(int);
            stream.WriteUInt32(offset, Endian.Big);
            offset += (uint)chunks.Count * (16 + sizeof(int));
            stream.WriteInt32(chunks.Count, Endian.Big);
        }
    }

    private (Block<byte>, List<(CasFileIdentifier, uint, uint)>, bool) LoadBundle(BlockStream stream, long inOffset, BundleModInfo inModInfo, InstallChunkWriter inInstallChunkWriter)
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
        
        bool inlineBundle = !(bundleOffset == 0 && bundleSize == 0);

        stream.Position = inOffset + locationOffset;
        
        Block<byte> fileIdentifierFlags = new(totalCount);
        stream.ReadExactly(fileIdentifierFlags);
        
        CasFileIdentifier file = default;
        int currentIndex = 0;

        List<(CasFileIdentifier, uint, uint)> files = new(totalCount);
        for (; currentIndex < totalCount; currentIndex++)
        {
            file = ReadCasFileIdentifier(stream, fileIdentifierFlags[currentIndex], file);
            
            files.Add((file, stream.ReadUInt32(Endian.Big), stream.ReadUInt32(Endian.Big)));
        }
        
        Block<byte> bundleMeta;
        if (inlineBundle)
        {
            stream.Position = inOffset + bundleOffset;
            bundleMeta = BinaryBundle.Modify(stream, inModInfo, m_modifiedEbx, m_modifiedRes, m_modifiedChunks,
                (entry, i, isAdded) =>
                {
                    if (isAdded)
                    {
                        files.Insert(i, inInstallChunkWriter.GetFileInfo(entry.Sha1));
                    }
                    else
                    {
                        files[i] = inInstallChunkWriter.GetFileInfo(entry.Sha1);
                    }
                });
            
            // go to the start of the data
            stream.Position = inOffset + dataOffset;
        }
        else
        {
            stream.Position = inOffset + dataOffset;
            file = ReadCasFileIdentifier(stream, fileIdentifierFlags[0], file);
            uint offset = stream.ReadUInt32(Endian.Big);
            int size = stream.ReadInt32(Endian.Big);
            string path = FileSystemManager.GetFilePath(file);
            if (string.IsNullOrEmpty(path))
            {
                throw new Exception("Corrupted data. File for bundle does not exist.");
            }
            using (BlockStream bundleStream = BlockStream.FromFile(path, offset, size))
            {
                bundleMeta = BinaryBundle.Modify(stream, inModInfo, m_modifiedEbx, m_modifiedRes, m_modifiedChunks,
                    (entry, i, isAdded) =>
                    {
                        if (isAdded)
                        {
                            files.Insert(i + 1, inInstallChunkWriter.GetFileInfo(entry.Sha1));
                        }
                        else
                        {
                            files[i + 1] = inInstallChunkWriter.GetFileInfo(entry.Sha1);
                        }
                    });                
                Debug.Assert(bundleStream.Position == bundleStream.Length, "We did not read the bundle meta completely");
            }
        }
        
        fileIdentifierFlags.Dispose();
        
        stream.Position = curPos;

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
}