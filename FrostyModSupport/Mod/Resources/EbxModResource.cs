using Frosty.Sdk;
using Frosty.Sdk.IO;

namespace Frosty.ModSupport.Mod.Resources;

public class EbxModResource : BaseModResource
{
    public override ModResourceType Type => ModResourceType.Ebx;

    public EbxModResource(DataStream inStream)
        : base(inStream)
    {
    }

    public EbxModResource(int inResourceIndex, string inName, Sha1 inSha1, long inOriginalSize,
        ResourceFlags inFlags, int inHandlerHash, string inUserData, IEnumerable<int> inBundlesToAdd,
        IEnumerable<int> inBundlesToRemove)
        : base(inResourceIndex, inName, inSha1, inOriginalSize, inFlags, inHandlerHash,
            inUserData, inBundlesToAdd, inBundlesToRemove)
    {
    }
}