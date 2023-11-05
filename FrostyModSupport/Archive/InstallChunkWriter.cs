using Frosty.Sdk;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Utils;

namespace Frosty.ModSupport.Archive;

public class InstallChunkWriter
{
    private InstallChunkInfo m_installChunk;
    private int m_installChunkIndex;
    private int m_casIndex;
    private string m_dir;
    private Dictionary<Sha1, (CasFileIdentifier, uint, uint)> m_data = new();
    
    public InstallChunkWriter(InstallChunkInfo inInstallChunk, string inGamePatchPath, string inModDataPath)
    {
        m_installChunk = inInstallChunk;
        m_installChunkIndex = FileSystemManager.GetInstallChunkIndex(m_installChunk);

        // get current cas index so we can use all the current cas files and dont have to rewrite the whole game
        string dir = Path.Combine(inGamePatchPath, m_installChunk.InstallBundle);
        if (Directory.Exists(dir))
        {
            foreach (string file in Directory.EnumerateFiles(dir, "*.cas"))
            {
                m_casIndex = Math.Max(int.Parse(file.AsSpan()[4..][..^4]), m_casIndex);
            }
        }

        // create mod dir
        m_dir = Path.Combine(inModDataPath, m_installChunk.InstallBundle);
        Directory.CreateDirectory(m_dir);
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

        retVal = (new CasFileIdentifier(false, m_installChunkIndex, m_casIndex), (uint)stream.Position,
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
    }

    private DataStream GetCurrentWriter(int size)
    {
        return default;
    }
}