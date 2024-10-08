using System;

namespace Frosty.Sdk.Ebx;

public readonly struct AssetClassGuid : IEquatable<AssetClassGuid>
{
    public Guid ExportedGuid => m_exportedGuid;
    public int InternalId => m_internalId;
    public bool IsExported => m_isExported;

    private readonly Guid m_exportedGuid;
    private readonly int m_internalId;
    private readonly bool m_isExported;

    public AssetClassGuid(Guid inGuid, int inId)
    {
        m_exportedGuid = inGuid;
        m_internalId = inId;
        m_isExported = (inGuid != Guid.Empty);
    }

    public AssetClassGuid(int inId)
    {
        m_exportedGuid = Guid.Empty;
        m_internalId = inId;
        m_isExported = false;
    }

    public static bool operator ==(AssetClassGuid a, object b) => a.Equals(b);

    public static bool operator !=(AssetClassGuid a, object b) => !a.Equals(b);

    public override bool Equals(object? obj)
    {
        switch (obj)
        {
            case null:
                return false;
            case AssetClassGuid reference:
                return Equals(reference);
            case Guid guid:
                return m_isExported && guid == m_exportedGuid;
            case int id:
                return m_internalId == id;
            default:
                return false;
        }
    }


    public bool Equals(AssetClassGuid other)
    {
        return m_exportedGuid.Equals(other.m_exportedGuid) && m_internalId == other.m_internalId && m_isExported == other.m_isExported;
    }
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (int)2166136261;
            hash = (hash * 16777619) ^ m_exportedGuid.GetHashCode();
            hash = (hash * 16777619) ^ m_internalId.GetHashCode();
            hash = (hash * 16777619) ^ m_isExported.GetHashCode();
            return hash;
        }
    }

    public override string ToString() => m_isExported ? m_exportedGuid.ToString() : $"00000000-0000-0000-0000-{m_internalId:x12}";
}