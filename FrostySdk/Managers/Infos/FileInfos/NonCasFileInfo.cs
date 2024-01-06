using System;
using System.IO;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.Managers.Infos.FileInfos;

public class NonCasFileInfo : IFileInfo
{
    private readonly string m_superBundleName;
    private readonly bool m_isPatch;
    private readonly uint m_offset;
    private readonly uint m_size;
    private readonly uint m_logicalOffset;

    private readonly bool m_isDelta;
    private readonly uint m_baseOffset;
    private readonly uint m_baseSize;
    private readonly int m_midInstructionSize;

    public NonCasFileInfo(string inSuperBundleName, bool inIsPatch, uint inOffset, uint inSize, uint inLogicalOffset = 0)
    {
        m_superBundleName = inSuperBundleName;
        m_isPatch = inIsPatch;
        m_offset = inOffset;
        m_size = inSize;
        m_logicalOffset = inLogicalOffset;
    }

    public NonCasFileInfo(string inSuperBundleName, uint inDeltaOffset, uint inDeltaSize, uint inBaseOffset, uint inBaseSize, int inMidInstructionSize, uint inLogicalOffset = 0)
    {
        m_isDelta = true;
        m_superBundleName = inSuperBundleName;
        m_offset = inDeltaOffset;
        m_size = inDeltaSize;
        m_baseOffset = inBaseOffset;
        m_baseSize = inBaseSize;
        m_midInstructionSize = inMidInstructionSize;
        m_logicalOffset = inLogicalOffset;
    }

    public bool IsDelta() => m_isDelta;

    public bool IsComplete()
    {
        return m_logicalOffset == 0;
    }

    public Block<byte> GetRawData()
    {
        if (m_isDelta)
        {
            throw new NotImplementedException();
        }

        using (FileStream stream = new(FileSystemManager.ResolvePath(m_isPatch, $"{m_superBundleName}.sb"), FileMode.Open, FileAccess.Read))
        {
            stream.Position = m_offset;

            Block<byte> retVal = new((int)m_size);

            stream.ReadExactly(retVal);
            return retVal;
        }
    }

    public Block<byte> GetData(int inOriginalSize)
    {
        if (m_isDelta)
        {
            BlockStream? baseStream = null;
            if (m_baseSize > 0)
            {
                baseStream = BlockStream.FromFile(FileSystemSource.Base.ResolvePath($"{m_superBundleName}.sb"),
                    m_baseOffset, (int)m_baseSize);
            }

            using (BlockStream deltaStream = BlockStream.FromFile(FileSystemManager.ResolvePath(true, $"{m_superBundleName}.sb"), m_offset, (int)m_size))
            {
                var retVal = Cas.DecompressData(deltaStream, baseStream, inOriginalSize, m_midInstructionSize);
                baseStream?.Dispose();
                return retVal;
            }
        }

        using (BlockStream stream = BlockStream.FromFile(FileSystemManager.ResolvePath(m_isPatch, $"{m_superBundleName}.sb"), m_offset, (int)m_size))
        {
            return Cas.DecompressData(stream, inOriginalSize);
        }
    }

    public void SerializeInternal(DataStream stream)
    {
        stream.WriteBoolean(m_isDelta);
        stream.WriteNullTerminatedString(m_superBundleName);

        if (!m_isDelta)
        {
            stream.WriteBoolean(m_isPatch);
        }

        stream.WriteUInt32(m_offset);
        stream.WriteUInt32(m_size);

        if (m_isDelta)
        {
            stream.WriteUInt32(m_baseOffset);
            stream.WriteUInt32(m_baseSize);
            stream.WriteInt32(m_midInstructionSize);
        }

        stream.WriteUInt32(m_logicalOffset);
    }

    public static NonCasFileInfo DeserializeInternal(DataStream stream)
    {
        bool isDelta = stream.ReadBoolean();
        if (isDelta)
        {
            return new NonCasFileInfo(stream.ReadNullTerminatedString(), stream.ReadUInt32(), stream.ReadUInt32(),
                stream.ReadUInt32(), stream.ReadUInt32(), stream.ReadInt32(), stream.ReadUInt32());
        }
        return new NonCasFileInfo(stream.ReadNullTerminatedString(), stream.ReadBoolean(), stream.ReadUInt32(), stream.ReadUInt32(),
            stream.ReadUInt32());
    }
}