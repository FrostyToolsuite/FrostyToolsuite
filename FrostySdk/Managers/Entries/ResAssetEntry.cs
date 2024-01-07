using Frosty.Sdk.Profiles;

namespace Frosty.Sdk.Managers.Entries;

public class ResAssetEntry : AssetEntry
{
    public override string Type => ((ResourceType)ResType).ToString();

    public override string AssetType => "res";

    /// <summary>
    /// The Rid of this <see cref="ResAssetEntry"/>.
    /// </summary>
    public ulong ResRid { get; }

    /// <summary>
    /// The <see cref="ResourceType"/> of this <see cref="ResAssetEntry"/>.
    /// </summary>
    public uint ResType { get; }

    /// <summary>
    /// The Meta of this <see cref="ResAssetEntry"/>.
    /// </summary>
    public byte[] ResMeta { get; }

    public ResAssetEntry(string inName, Sha1 inSha1, long inOriginalSize, ulong inResRid, uint inResType, byte[] inResMeta)
        : base(inSha1, inOriginalSize)
    {
        Name = inName;
        ResRid = inResRid;
        ResType = inResType;
        ResMeta = inResMeta;
    }
}