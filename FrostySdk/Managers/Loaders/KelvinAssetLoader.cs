using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Managers.Infos.FileInfos;

namespace Frosty.Sdk.Managers.Loaders;

public class KelvinAssetLoader : IAssetLoader
{
    private readonly struct FileIdentifier
    {
        public readonly int FileIndex;
        public readonly uint Offset;
        public readonly uint Size;

        public FileIdentifier(int inFileIndex, uint inOffset, uint inSize)
        {
            FileIndex = inFileIndex;
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
            uint magic = stream.ReadUInt32();
            uint bundlesOffset = stream.ReadUInt32();
            uint chunksOffset = stream.ReadUInt32();

            if (magic == 0xC3E5D5C3)
            {
                stream.Decrypt(KeyManager.GetKey("BundleEncryptionKey"), PaddingMode.None);
            }

            if (bundlesOffset != 0xFFFFFFFF)
            {
                stream.Position = bundlesOffset;

                int bundleCount = stream.ReadInt32();

                // bundle hashmap
                stream.Position += sizeof(int) * bundleCount;

                for (int i = 0; i < bundleCount; i++)
                {
                    uint bundleOffset = stream.ReadUInt32();

                    long curPos = stream.Position;

                    stream.Position = bundleOffset;

                    string name = ReadString(stream, stream.ReadInt32());

                    List<FileIdentifier> files = new();
                    while (true)
                    {
                        int file = stream.ReadInt32();
                        uint fileOffset = stream.ReadUInt32();
                        uint fileSize = stream.ReadUInt32();

                        files.Add(new FileIdentifier(file & 0x7FFFFFFF, fileOffset, fileSize));
                        if ((file & 0x80000000) == 0)
                        {
                            break;
                        }
                    }

                    stream.Position = curPos;

                    if (inSbIc.BundleMapping.ContainsKey(name))
                    {
                        continue;
                    }

                    BundleInfo bundle = AssetManager.AddBundle(name, inSbIc);

                    int index = 0;
                    FileIdentifier resourceInfo = files[index];

                    BlockStream dataStream = BlockStream.FromFile(
                        FileSystemManager.ResolvePath(FileSystemManager.GetFilePath(resourceInfo.FileIndex)),
                        resourceInfo.Offset, (int)resourceInfo.Size);

                    BinaryBundle bundleMeta = BinaryBundle.Deserialize(dataStream);

                    foreach (EbxAssetEntry ebx in bundleMeta.EbxList)
                    {
                         if (dataStream.Position == resourceInfo.Size)
                         {
                             dataStream.Dispose();
                             resourceInfo = files[++index];
                             dataStream = BlockStream.FromFile(
                                 FileSystemManager.ResolvePath(FileSystemManager.GetFilePath(resourceInfo.FileIndex)),
                                 resourceInfo.Offset, (int)resourceInfo.Size);
                         }

                         uint offset = (uint)dataStream.Position;
                         uint size = (uint)Cas.GetCompressedSize(dataStream, ebx.OriginalSize);

                         ebx.AddFileInfo(new KelvinFileInfo(resourceInfo.FileIndex,
                             resourceInfo.Offset + offset, size, 0));

                         AssetManager.AddEbx(ebx, bundle.Id);
                    }
                    foreach (ResAssetEntry res in bundleMeta.ResList)
                    {
                         if (dataStream.Position == resourceInfo.Size)
                         {
                             dataStream.Dispose();
                             resourceInfo = files[++index];
                             dataStream = BlockStream.FromFile(
                                 FileSystemManager.ResolvePath(FileSystemManager.GetFilePath(resourceInfo.FileIndex)),
                                 resourceInfo.Offset, (int)resourceInfo.Size);
                         }

                         uint offset = (uint)dataStream.Position;
                         uint size = (uint)Cas.GetCompressedSize(dataStream, res.OriginalSize);

                         res.AddFileInfo(new KelvinFileInfo(resourceInfo.FileIndex,
                             resourceInfo.Offset + offset, size, 0));

                         AssetManager.AddRes(res, bundle.Id);
                    }
                    foreach (ChunkAssetEntry chunk in bundleMeta.ChunkList)
                    {
                         if (dataStream.Position == resourceInfo.Size)
                         {
                             dataStream.Dispose();
                             resourceInfo = files[++index];
                             dataStream = BlockStream.FromFile(
                                 FileSystemManager.ResolvePath(FileSystemManager.GetFilePath(resourceInfo.FileIndex)),
                                 resourceInfo.Offset, (int)resourceInfo.Size);
                         }

                         uint offset = (uint)dataStream.Position;
                         uint size = (uint)Cas.GetCompressedSize(dataStream,
                             (chunk.LogicalOffset & 0xFFFF) | chunk.LogicalSize);

                         chunk.AddFileInfo(new KelvinFileInfo(resourceInfo.FileIndex,
                             resourceInfo.Offset + offset, size, chunk.LogicalOffset));

                         AssetManager.AddChunk(chunk, bundle.Id);
                    }

                    dataStream.Dispose();
                }
            }

            if (chunksOffset != 0xFFFFFFFF)
            {
                stream.Position = chunksOffset;
                int chunksCount = stream.ReadInt32();

                // hashmap
                stream.Position += sizeof(int) * chunksCount;

                for (int i = 0; i < chunksCount; i++)
                {
                    int offset = stream.ReadInt32();

                    long pos = stream.Position;
                    stream.Position = offset;

                    Guid guid = stream.ReadGuid();
                    int fileIndex = stream.ReadInt32();
                    uint dataOffset = stream.ReadUInt32();
                    uint dataSize = stream.ReadUInt32();

                    ChunkAssetEntry chunk = new(guid, Sha1.Zero, 0, 0, Utils.Utils.HashString(inSbIc.Name, true));

                    chunk.AddFileInfo(new KelvinFileInfo(fileIndex, dataOffset, dataSize, 0));

                    AssetManager.AddSuperBundleChunk(chunk);

                    stream.Position = pos;
                }
            }
        }

        return Code.Stop;
    }

    private string ReadString(DataStream reader, int offset)
    {
        long curPos = reader.Position;
        StringBuilder sb = new();

        do
        {
            reader.Position = offset - 1;
            string tmp = reader.ReadNullTerminatedString();
            offset = reader.ReadInt32();

            sb.Append(tmp);
        } while (offset != 0);

        reader.Position = curPos;
        return new string(sb.ToString().Reverse().ToArray());
    }
}