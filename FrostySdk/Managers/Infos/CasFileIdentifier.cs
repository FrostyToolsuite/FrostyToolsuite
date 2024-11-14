using System;

namespace Frosty.Sdk.Managers.Infos;

public readonly struct CasFileIdentifier : IEquatable<CasFileIdentifier>, IComparable<CasFileIdentifier>
{
    public bool IsPatch { get; }
    public uint InstallChunkIndex { get; }
    public int CasIndex { get; }

    public static CasFileIdentifier FromFileIdentifier(uint file)
    {
        return new CasFileIdentifier(((file >> 16) & 0xFF) != 0, (file >> 8) & 0xFF, (int)(file & 0xFF));
    }

    public static CasFileIdentifier FromFileIdentifier(uint file1, uint file2)
    {
        return new CasFileIdentifier(((file1 >> 16) & 0xFF) != 0, ((file1 << 16) & 0xFFFF0000) | ((file2 >> 16) & 0xFFFF), (int)(file2 & 0xFFFF));
    }

    public static CasFileIdentifier FromFileIdentifier(ulong file)
    {
        return new CasFileIdentifier(((file >> 48) & 0xFF) != 0, (uint)((file >> 16) & 0xFFFFFFFF), (int)(file & 0xFFFF));
    }

    public static CasFileIdentifier FromManifestFileIdentifier(uint file)
    {
        return new CasFileIdentifier(((file >> 8) & 0xF) != 0, (uint)(file >> 12), (int)(file & 0xFF) + 1);
    }

    public static uint ToFileIdentifier(CasFileIdentifier file)
    {
        return (uint)((file.IsPatch ? 1 << 16 : 0) | (file.InstallChunkIndex << 8) | file.CasIndex);
    }

    public static ulong ToFileIdentifierLong(CasFileIdentifier file)
    {
        return (file.IsPatch ? 0x1000000000000ul : 0) | ((ulong)(uint)file.InstallChunkIndex << 16) | (uint)file.CasIndex;
    }

    public static uint ToManifestFileIdentifier(CasFileIdentifier file)
    {
        return (uint)((file.IsPatch ? 1 << 8 : 0) | (file.InstallChunkIndex << 12) | (file.CasIndex - 1));
    }

    public CasFileIdentifier(bool inIsPatch, uint inInstallChunkIndex, int inCasIndex)
    {
        IsPatch = inIsPatch;
        InstallChunkIndex = inInstallChunkIndex;
        CasIndex = inCasIndex;
    }

    public static bool operator ==(CasFileIdentifier a, CasFileIdentifier b) => a.Equals(b);
    public static bool operator !=(CasFileIdentifier a, CasFileIdentifier b) => !a.Equals(b);

    public bool Equals(CasFileIdentifier b)
    {
        return IsPatch == b.IsPatch && InstallChunkIndex == b.InstallChunkIndex && CasIndex == b.CasIndex;
    }

    public int CompareTo(CasFileIdentifier other)
    {
        if (IsPatch != other.IsPatch)
        {
            return IsPatch.CompareTo(other.IsPatch);
        }

        if (InstallChunkIndex != other.InstallChunkIndex)
        {
            return InstallChunkIndex.CompareTo(other.InstallChunkIndex);
        }

        return CasIndex.CompareTo(other.CasIndex);
    }

    public override bool Equals(object? obj)
    {
        if (obj is not CasFileIdentifier b)
        {
            return false;
        }

        return Equals(b);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(IsPatch, InstallChunkIndex, CasIndex);
    }
}