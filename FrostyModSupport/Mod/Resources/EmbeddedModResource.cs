using Frosty.Sdk;
using Frosty.Sdk.IO;

namespace Frosty.ModSupport.Mod.Resources;

public sealed class EmbeddedModResource : BaseModResource
{
    public override ModResourceType Type => ModResourceType.Embedded;

    public EmbeddedModResource(DataStream inStream)
        : base(inStream)
    {
    }
    
    public EmbeddedModResource(int inResourceIndex, string inName)
        : base(inResourceIndex, inName, Sha1.Zero, 0, 0, 0,
            string.Empty, Enumerable.Empty<int>(), Enumerable.Empty<int>())
    {
    }
}