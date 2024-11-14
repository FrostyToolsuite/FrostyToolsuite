using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Frosty.Sdk;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.CatResources;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Utils;

namespace Frosty.ModSupport.Archive;

public class InstallChunkWriter : IDisposable
{
    private const int c_maxCasFileSize = 1073741824;

    private readonly InstallChunkInfo m_installChunk;
    private readonly uint m_installChunkIndex;
    private int m_casIndex;
    private readonly bool m_isPatch;
    private readonly string m_dir;
    private readonly Dictionary<Sha1, (CasFileIdentifier, uint, uint)> m_data = new();
    private DataStream? m_currentStream;

    public InstallChunkWriter(InstallChunkInfo inInstallChunk, string inGamePatchPath, string inModDataPath, bool inIsPatch)
    {
        m_installChunk = inInstallChunk;
        m_installChunkIndex = FileSystemManager.GetInstallChunkIndex(m_installChunk);
        m_isPatch = inIsPatch;

        // create mod dir
        m_dir = Path.Combine(inModDataPath, m_installChunk.InstallBundle);
        Directory.CreateDirectory(m_dir);

        // create links to already existing cas files in this install bundle
        string dir = Path.Combine(inGamePatchPath, m_installChunk.InstallBundle);
        if (Directory.Exists(dir))
        {
            foreach (string file in Directory.EnumerateFiles(dir, "*.cas"))
            {
                int index = int.Parse(Path.GetFileName(file).AsSpan()[4..][..^4]);
                m_casIndex = Math.Max(index, m_casIndex);
                File.CreateSymbolicLink(Path.Combine(m_dir, $"cas_{index:D2}.cas"), file);
            }
        }

        m_casIndex++;
    }

    public (CasFileIdentifier, uint, uint) WriteData(Sha1 inSha1, Block<byte> inData)
    {
        if (m_data.TryGetValue(inSha1, out (CasFileIdentifier, uint, uint) retVal))
        {
            return retVal;
        }

        DataStream stream = GetCurrentWriter(inData.Size);

        if (ProfilesLibrary.FrostbiteVersion <= "2014.4.11")
        {
            // write faceoff header
            stream.WriteUInt32(0xFACE0FF0, Endian.Big);
            stream.WriteSha1(inSha1);
            stream.WriteInt64(inData.Size);
        }

        retVal = (new CasFileIdentifier(m_isPatch, m_installChunkIndex, m_casIndex), (uint)stream.Position,
            (uint)inData.Size);
        m_data.Add(inSha1, retVal);
        stream.Write(inData);

        return retVal;
    }

    public (CasFileIdentifier, uint, uint) GetFileInfo(Sha1 inSha1)
    {
        return m_data[inSha1];
    }

    public void WriteCatalog()
    {
        Block<byte> catalog = new(16);
        if (!FileSystemManager.TryResolvePath(Path.Combine(m_installChunk.InstallBundle, "cas.cat"), out string? originalPath))
        {
            throw new Exception();
        }

        using (BlockStream stream = new(catalog, true))
        {
            stream.WriteFixedSizedString("NyanNyanNyanNyan", 16);
            bool isNewFormat = ProfilesLibrary.FrostbiteVersion > "2014.4.11";
            using (CatStream catStream = new(originalPath))
            {
                if (isNewFormat)
                {
                    stream.WriteUInt32(catStream.ResourceCount + (uint)m_data.Count);
                    stream.WriteUInt32(catStream.PatchCount);
                    if (ProfilesLibrary.FrostbiteVersion >= "2015")
                    {
                        stream.WriteUInt32(catStream.EncryptedCount);
                        stream.Position += 12;
                    }
                }

                for (int i = 0; i < catStream.ResourceCount; i++)
                {
                    CatResourceEntry entry = catStream.ReadResourceEntry();
                    stream.WriteSha1(entry.Sha1);
                    stream.WriteUInt32(entry.Offset);
                    stream.WriteUInt32(entry.Size);

                    if (isNewFormat)
                    {
                        stream.WriteUInt32(entry.LogicalOffset);
                    }

                    stream.WriteInt32(entry.ArchiveIndex);
                }

                foreach (KeyValuePair<Sha1,(CasFileIdentifier Identifier, uint Offset, uint Size)> data in m_data)
                {
                    stream.WriteSha1(data.Key);
                    stream.WriteUInt32(data.Value.Offset);
                    stream.WriteUInt32(data.Value.Size);

                    if (isNewFormat)
                    {
                        stream.WriteUInt32(0);
                    }

                    stream.WriteInt32(data.Value.Identifier.CasIndex);
                }

                for (int i = 0; i < catStream.EncryptedCount; i++)
                {
                    CatResourceEntry entry = catStream.ReadEncryptedEntry();
                    stream.WriteSha1(entry.Sha1);
                    stream.WriteUInt32(entry.Offset);
                    stream.WriteUInt32(entry.Size);
                    stream.WriteUInt32(entry.LogicalOffset);
                    stream.WriteInt32(entry.ArchiveIndex | (1 << 8));
                    stream.WriteUInt32(entry.OriginalSize);
                    stream.WriteFixedSizedString(entry.KeyId, 8);
                    stream.Write(entry.Checksum);
                }

                for (int i = 0; i < catStream.PatchCount; i++)
                {
                    CatPatchEntry entry = catStream.ReadPatchEntry();
                    stream.WriteSha1(entry.Sha1);
                    stream.WriteSha1(entry.BaseSha1);
                    stream.WriteSha1(entry.DeltaSha1);
                }
            }
        }

        string fileName = Path.Combine(m_dir, "cas.cat");
        using (FileStream stream = new(fileName, FileMode.CreateNew))
        {
            if (ProfilesLibrary.FrostbiteVersion > "2014.4.11")
            {
                ObfuscationHeader.Write(stream);
            }
            stream.Write(catalog);
        }
    }

    public List<CasFileIdentifier> GetFiles()
    {
        List<CasFileIdentifier> retVal = new(m_data.Values.Count);
        foreach ((CasFileIdentifier, uint, uint) value in m_data.Values)
        {
            retVal.Add(value.Item1);
        }
        return retVal;
    }

    private DataStream GetCurrentWriter(int size)
    {
        FileInfo currentFile = new(Path.Combine(m_dir, $"cas_{m_casIndex:D2}.cas"));

        if (currentFile.Exists && currentFile.Length + size > c_maxCasFileSize)
        {
            m_casIndex++;
            currentFile = new FileInfo(Path.Combine(m_dir, $"cas_{m_casIndex:D2}.cas"));
            Debug.Assert(!currentFile.Exists, "Trying to create new cas archive, even tho it already exists");
            m_currentStream?.Dispose();
            m_currentStream = new DataStream(currentFile.Create());
        }
        else if (m_currentStream is null)
        {
            Debug.Assert(!currentFile.Exists, "First cas archive used should not exist");
            m_currentStream = new DataStream(currentFile.Create());
            m_currentStream.Seek(0, SeekOrigin.End);
        }

        return m_currentStream;
    }

    public void Dispose()
    {
        m_currentStream?.Dispose();
    }
}