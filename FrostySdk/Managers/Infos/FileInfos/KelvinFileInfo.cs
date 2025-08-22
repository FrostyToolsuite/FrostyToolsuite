using System;
using System.IO;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.Managers.Infos.FileInfos;

public class KelvinFileInfo : IFileInfo
{
    private readonly string m_path;
    private readonly int m_casIndex;
    private readonly uint m_offset;
    private readonly uint m_size;
    private readonly uint m_logicalOffset;

    public KelvinFileInfo(int inCasIndex, uint inOffset, uint inSize, uint inLogicalOffset)
    {
        m_casIndex = inCasIndex;
        m_offset = inOffset;
        m_size = inSize;
        m_logicalOffset = inLogicalOffset;
        m_path = FileSystemManager.GetFilePath(m_casIndex);
    }

    public bool IsDelta() => false;

    public bool IsComplete() => m_logicalOffset != 0;

    public bool FileExists() => true;

    public long GetOriginalSize()
    {
        using (BlockStream stream = BlockStream.FromFile(m_path, m_offset, (int)m_size))
        {
            return Cas.GetOriginalSize(stream);
        }
    }

    public Block<byte> GetRawData()
    {
        using (FileStream stream = new(m_path, FileMode.Open, FileAccess.Read))
        {
            stream.Position = m_offset;

            Block<byte> retVal = new((int)m_size);

            stream.ReadExactly(retVal);
            return retVal;
        }
    }

    public Block<byte> GetData(int originalSize)
    {
        using (BlockStream stream = BlockStream.FromFile(m_path, m_offset, (int)m_size))
        {
            return Cas.DecompressData(stream, originalSize);
        }
    }

    void IFileInfo.SerializeInternal(DataStream stream)
    {
        stream.WriteInt32(m_casIndex);
        stream.WriteUInt32(m_offset);
        stream.WriteUInt32(m_size);
        stream.WriteUInt32(m_logicalOffset);
    }

    internal static KelvinFileInfo DeserializeInternal(DataStream stream)
    {
        return new KelvinFileInfo(stream.ReadInt32(), stream.ReadUInt32(), stream.ReadUInt32(),
            stream.ReadUInt32());
    }

    public bool Equals(KelvinFileInfo b)
    {
        return m_casIndex == b.m_casIndex &&
               m_offset == b.m_offset &&
               m_size == b.m_size &&
               m_logicalOffset == b.m_logicalOffset;
    }

    public override bool Equals(object? obj)
    {
        if (obj is KelvinFileInfo b)
        {
            return Equals(b);
        }

        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(m_casIndex, m_offset, m_size, m_logicalOffset);
    }
}