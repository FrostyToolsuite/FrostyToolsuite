using System;
using System.IO;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.Managers.Infos.FileInfos;

public class NonCasFileInfo : IFileInfo
{
    private readonly string m_superBundlePath;
    private readonly long m_offset;
    private readonly uint m_size;
    private readonly uint m_logicalOffset;

    private readonly bool m_isDelta;
    private readonly string? m_superBundleBasePath;
    private readonly long m_baseOffset;
    private readonly uint m_baseSize;
    private readonly int m_midInstructionSize;

    private readonly string m_fullPath;
    private readonly string? m_fullBasePath;

    public NonCasFileInfo(string inSuperBundlePath, long inOffset, uint inSize, uint inLogicalOffset = 0)
    {
        m_superBundlePath = inSuperBundlePath;
        m_offset = inOffset;
        m_size = inSize;
        m_logicalOffset = inLogicalOffset;
        m_fullPath = Path.Combine(FileSystemManager.BasePath, m_superBundlePath);
    }

    public NonCasFileInfo(string inSuperBundlePath, string? inSuperBundleBasePath, long inDeltaOffset, uint inDeltaSize, long inBaseOffset, uint inBaseSize, int inMidInstructionSize, uint inLogicalOffset = 0)
    {
        m_isDelta = true;
        m_superBundlePath = inSuperBundlePath;
        m_superBundleBasePath = inSuperBundleBasePath;
        m_offset = inDeltaOffset;
        m_size = inDeltaSize;
        m_baseOffset = inBaseOffset;
        m_baseSize = inBaseSize;
        m_midInstructionSize = inMidInstructionSize;
        m_logicalOffset = inLogicalOffset;
        m_fullPath = Path.Combine(FileSystemManager.BasePath, m_superBundlePath);
        m_fullBasePath = m_superBundleBasePath is not null ? Path.Combine(FileSystemManager.BasePath, m_superBundleBasePath) : null;
    }

    public bool IsDelta() => m_isDelta;

    public bool IsComplete() => m_logicalOffset == 0;

    public bool FileExists() => true;

    public long GetOriginalSize()
    {
        if (m_isDelta)
        {
            BlockStream? baseStream = null;
            if (m_baseSize > 0)
            {
                baseStream = BlockStream.FromFile(m_fullBasePath!, m_baseOffset, (int)m_baseSize);
            }

            using (BlockStream deltaStream = BlockStream.FromFile(m_fullPath, m_offset, (int)m_size))
            {
                return Cas.GetOriginalSize(deltaStream, baseStream, m_midInstructionSize);
            }
        }

        using (BlockStream stream = BlockStream.FromFile(m_fullPath, m_offset, (int)m_size))
        {
            return Cas.GetOriginalSize(stream);
        }
    }

    public Block<byte> GetRawData()
    {
        if (m_isDelta)
        {
            throw new NotImplementedException();
        }

        using (FileStream stream = new(m_fullPath, FileMode.Open, FileAccess.Read))
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
                baseStream = BlockStream.FromFile(m_fullBasePath!, m_baseOffset, (int)m_baseSize);
            }

            using (BlockStream deltaStream = BlockStream.FromFile(m_fullPath, m_offset, (int)m_size))
            {
                Block<byte> retVal = Cas.DecompressData(deltaStream, baseStream, inOriginalSize, m_midInstructionSize);
                baseStream?.Dispose();
                return retVal;
            }
        }

        using (BlockStream stream = BlockStream.FromFile(m_fullPath, m_offset, (int)m_size))
        {
            return Cas.DecompressData(stream, inOriginalSize);
        }
    }

    public void SerializeInternal(DataStream stream)
    {
        stream.WriteBoolean(m_isDelta);
        stream.WriteNullTerminatedString(m_superBundlePath);

        if (m_isDelta)
        {
            stream.WriteNullTerminatedString(m_superBundleBasePath ?? string.Empty);
        }

        stream.WriteInt64(m_offset);
        stream.WriteUInt32(m_size);

        if (m_isDelta)
        {
            stream.WriteInt64(m_baseOffset);
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
            return new NonCasFileInfo(stream.ReadNullTerminatedString(), stream.ReadNullTerminatedString(),
                stream.ReadInt64(), stream.ReadUInt32(), stream.ReadInt64(), stream.ReadUInt32(), stream.ReadInt32(),
                stream.ReadUInt32());
        }
        return new NonCasFileInfo(stream.ReadNullTerminatedString(), stream.ReadInt64(), stream.ReadUInt32(),
            stream.ReadUInt32());
    }
}