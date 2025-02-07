using System;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO.Ebx;

public struct EbxImportReference : IEquatable<EbxImportReference>, IComparable<EbxImportReference>
{
    public Guid PartitionGuid;
    public Guid InstanceGuid;

    public override string ToString() => $"{PartitionGuid}/{InstanceGuid}";

    public static bool operator ==(EbxImportReference a, EbxImportReference b) => a.Equals(b);

    public static bool operator !=(EbxImportReference a, EbxImportReference b) => !a.Equals(b);

    public override bool Equals(object? obj)
    {
        if (obj is EbxImportReference b)
        {
            return PartitionGuid == b.PartitionGuid && InstanceGuid == b.InstanceGuid;
        }
        return false;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (int)2166136261;
            hash = (hash * 16777619) ^ PartitionGuid.GetHashCode();
            hash = (hash * 16777619) ^ InstanceGuid.GetHashCode();
            return hash;
        }
    }

    public bool Equals(EbxImportReference other)
    {
        return PartitionGuid.Equals(other.PartitionGuid) && InstanceGuid.Equals(other.InstanceGuid);
    }

    public int CompareTo(EbxImportReference other)
    {
        int partitionGuidComparison = PartitionGuid.CompareToBigEndian(other.PartitionGuid);
        if (partitionGuidComparison != 0)
        {
            return partitionGuidComparison;
        }
        return InstanceGuid.CompareToBigEndian(other.InstanceGuid);
    }
}