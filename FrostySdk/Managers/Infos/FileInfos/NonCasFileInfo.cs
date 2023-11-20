using System.IO;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.Managers.Infos.FileInfos;

public class NonCasFileInfo : IFileInfo
{
    private string m_superBundleName;
    private uint m_offset;
    private uint m_size;
    private uint m_logicalOffset;

    public NonCasFileInfo(string inSuperBundleName, uint inOffset, uint inSize, uint inLogicalOffset = 0)
    {
        m_superBundleName = inSuperBundleName;
        m_offset = inOffset;
        m_size = inSize;
        m_logicalOffset = inLogicalOffset;
    }

    public bool IsComplete()
    {
        return m_logicalOffset == 0;
    }

    public Block<byte> GetRawData()
    {
        using (FileStream stream = new(m_superBundleName, FileMode.Open, FileAccess.Read))
        {
            stream.Position = m_offset;

            Block<byte> retVal = new((int)m_size);

            stream.ReadExactly(retVal);
            return retVal;
        }
    }

    public Block<byte> GetData(int inOriginalSize)
    {
        using (BlockStream stream = BlockStream.FromFile(m_superBundleName, m_offset, (int)m_size))
        {
            return Cas.DecompressData(stream, inOriginalSize);
        }
    }

    public void SerializeInternal(DataStream stream)
    {
        stream.WriteNullTerminatedString(m_superBundleName);
        stream.WriteUInt32(m_offset);
        stream.WriteUInt32(m_size);
        stream.WriteUInt32(m_logicalOffset);
    }

    public static NonCasFileInfo DeserializeInternal(DataStream stream)
    {
        return new NonCasFileInfo(stream.ReadNullTerminatedString(), stream.ReadUInt32(), stream.ReadUInt32(),
            stream.ReadUInt32());
    }
}