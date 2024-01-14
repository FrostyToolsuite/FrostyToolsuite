using System.Diagnostics;
using Frosty.ModSupport.Archive;
using Frosty.ModSupport.ModEntries;
using Frosty.ModSupport.ModInfos;
using Frosty.Sdk;
using Frosty.Sdk.Exceptions;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Managers.Loaders;
using Frosty.Sdk.Utils;

namespace Frosty.ModSupport;

internal class Manifest2019 : IDisposable
{
    private class StringHelper
    {
        public class String
        {
            public uint Offset { get; set; }
        }

        private bool m_encodeStrings;
        private uint m_currentOffset;
        private Dictionary<string, String> m_mapping = new();
        private Dictionary<int, string> m_ids = new();

        public StringHelper(bool inEncodeStrings)
        {
            m_encodeStrings = inEncodeStrings;
        }

        public String AddString(string inString)
        {
            if (m_mapping.TryGetValue(inString, out String? retVal))
            {
                return retVal;
            }

            retVal = new String();
            if (!m_encodeStrings)
            {
                retVal.Offset = m_currentOffset;
                m_currentOffset += (uint)(inString.Length + 1);
            }

            m_mapping.Add(inString, retVal);

            return retVal;
        }

        public void Fixup()
        {
            if (!m_encodeStrings)
            {
                return;
            }

            HuffmanEncoder encoder = new();
            // TODO: encode strings
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
        List< (StringHelper.String, uint, long)> bundles = new();
        List<(Guid, int, CasFileIdentifier, uint, uint)> chunks = new();
        StringHelper stringHelper;

        bool encodeStrings = false;
        byte bundleLoadFlag = 0;

        Block<byte> modifiedSuperBundle = new(100); // TODO: estimate size
        using (BlockStream modifiedStream = new(modifiedSuperBundle, true))
        {
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
                    encodeStrings = true;
                    huffmanDecoder = new HuffmanDecoder();
                    namesCount = stream.ReadUInt32(Endian.Big);
                    tableCount = stream.ReadUInt32(Endian.Big);
                    tableOffset = stream.ReadUInt32(Endian.Big);
                }

                stringHelper = new StringHelper(encodeStrings);

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
                        bundleLoadFlag = (byte)(bundleSize >> 30);
                        bundleSize &= 0x3FFFFFFFU;

                        uint newOffset = (uint)modifiedStream.Position;
                        long newBundleSize;

                        if (!inModInfo.Modified.Bundles.TryGetValue(id, out BundleModInfo? bundleModInfo))
                        {
                            // load and write unmodified bundle
                            newBundleSize = bundleSize;
                            switch (bundleLoadFlag)
                            {
                                case 0:
                                    sbStream ??= BlockStream.FromFile(inPath.Replace(".toc", ".sb"), false);
                                    sbStream.Position = bundleOffset;
                                    sbStream.CopyTo(modifiedStream, (int)bundleSize);
                                    break;
                                case 1:
                                    long curPos = stream.Position;
                                    stream.Position = bundleOffset;
                                    stream.CopyTo(modifiedStream, (int)bundleSize);
                                    stream.Position = curPos;
                                    break;
                                default:
                                    throw new UnknownValueException<byte>("bundle load flag", bundleLoadFlag);
                            }
                        }
                        else if (inCreateNewPatch)
                        {
                            // we create a new patch to a base superbundle, so we only need modified bundles
                            continue;
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
                            modifiedStream.Write(data);
                            newBundleSize = data.Size;
                            data.Dispose();

                            // remove bundle so we can check if the base superbundle needs to be loaded to modify a base bundle
                            inModInfo.Modified.Bundles.Remove(id);
                        }

                        bundles.Add((stringHelper.AddString(name), newOffset, newBundleSize));
                    }
                    huffmanDecoder?.Dispose();
                }

                if (chunksCount != 0)
                {

                }
            }
        }

        stringHelper.Fixup();

        uint offset = encodeStrings ? 0x3Cu : 0x30u;
        TocData = new Block<byte>(100); // TODO: size
        using (BlockStream stream = new(TocData, true))
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

            stream.WriteUInt32(offset, Endian.Big);
            stream.WriteUInt32(offset, Endian.Big);

            stream.WriteUInt32(0xdeadbeef); // stringsOffset

            stream.WriteUInt32(offset, Endian.Big);
            stream.WriteUInt32(0xdeadbeef); // dataCount

            if (encodeStrings)
            {
                stream.WriteUInt32(0xdeadbeef); // stringCount
                stream.WriteUInt32(0xdeadbeef); // tableCount
                stream.WriteUInt32(0xdeadbeef); // tableOffset
            }

            foreach ((StringHelper.String, uint, long) bundle in bundles)
            {
                stream.WriteUInt32(bundle.Item1.Offset, Endian.Big);
                stream.WriteUInt32(bundle.Item2 | (uint)(bundleLoadFlag << 30), Endian.Big);
                stream.WriteInt64(bundle.Item3, Endian.Big);
            }
        }
    }

    private static Block<byte> WriteModifiedBundle((Block<byte> BundleMeta, List<(CasFileIdentifier, uint, uint)> Files, bool IsInline) inBundle, InstallChunkWriter inInstallChunkWriter)
    {
        Block<byte> retVal = new(100); // TODO: size
        using (BlockStream bundleWriter = new(retVal, true))
        {
            bundleWriter.WriteInt32(inBundle.IsInline ? 0x20 : 0, Endian.Big);
            bundleWriter.WriteInt32(inBundle.IsInline ? inBundle.BundleMeta.Size : 0, Endian.Big);
            bundleWriter.WriteUInt32(0xDEADBEEF, Endian.Big); // fileIdentifiedFlags offset
            bundleWriter.WriteInt32(inBundle.Files.Count, Endian.Big);
            bundleWriter.WriteUInt32(0xDEADBEEF, Endian.Big); // fileIdentifier offset
            bundleWriter.WriteUInt32(0xDEADBEEF, Endian.Big); // unused
            bundleWriter.WriteUInt32(0xDEADBEEF, Endian.Big); // unused
            bundleWriter.WriteUInt32(0, Endian.Big); // unused

            if (inBundle.IsInline)
            {
                bundleWriter.Write(inBundle.BundleMeta);
            }
            else
            {
                inBundle.Files[0] =
                    inInstallChunkWriter.WriteData(Utils.GenerateSha1(inBundle.BundleMeta), inBundle.BundleMeta);
            }

            inBundle.BundleMeta.Dispose();

            bundleWriter.Pad(4);

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

        return retVal;
    }

    private (Block<byte>, List<(CasFileIdentifier, uint, uint)>, bool) ModifyBundle(BlockStream stream,
        long inOffset, BundleModInfo inModInfo, InstallChunkWriter inInstallChunkWriter)
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

        }

        using (Manifest2019 action = new(m_modifiedEbx, m_modifiedRes, m_modifiedChunks))
        {
            action.ModSuperBundle(tocPath, createNewPatch, inSbIc, inModInfo, inInstallChunkWriter);

            FileInfo modifiedToc = new(Path.Combine(m_modDataPath, $"{inSbIc.Name}.toc"));
            Directory.CreateDirectory(modifiedToc.DirectoryName!);

            using (FileStream stream = new(modifiedToc.FullName, FileMode.Create, FileAccess.Write))
            {
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
                    File.CreateSymbolicLink(sbPath, modifiedToc.FullName.Replace(".toc", ".sb"));
                }
            }
        }
    }
}