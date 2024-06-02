using Frosty.Sdk;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Utils;

namespace Frosty.ModSupport.Archive;

public class InstallChunkWriter
{
    private const int c_maxCasFileSize = 1073741824;

    private InstallChunkInfo m_installChunk;
    private int m_installChunkIndex;
    private int m_casIndex;
    private bool m_isPatch;
    private string m_dir;
    private Dictionary<Sha1, (CasFileIdentifier, uint, uint)> m_data = new();
    private BlockStream? m_currentStream;
    private Block<byte>? m_currentBlock;

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
            stream.WriteUInt32(0xFACE0FF, Endian.Big);
            stream.WriteSha1(inSha1);
            stream.WriteInt64(inData.Size);
        }

        retVal = (new CasFileIdentifier(m_isPatch, m_installChunkIndex, m_casIndex), (uint)stream.Position,
            (uint)inData.Size);
        m_data.Add(inSha1, retVal);
        stream.Write(inData);

        stream.Dispose();

        return retVal;
    }

    public (CasFileIdentifier, uint, uint) GetFileInfo(Sha1 inSha1)
    {
        return m_data[inSha1];
    }

    public void WriteCatalog()
    {
    }

    private DataStream GetCurrentWriter(int size)
    {
        FileInfo currentFile = new(Path.Combine(m_dir, $"cas_{m_casIndex:D2}.cas"));

        if (currentFile.Exists && currentFile.Length + size > c_maxCasFileSize)
        {
            m_casIndex++;
            currentFile = new FileInfo(Path.Combine(m_dir, $"cas_{m_casIndex:D2}.cas"));
        }

        DataStream stream;
        if (!currentFile.Exists)
        {
            stream = new DataStream(currentFile.Create());
        }
        else
        {
            stream = new DataStream(currentFile.OpenWrite());
        }

        stream.Seek(0, SeekOrigin.End);

        return stream;
    }
}