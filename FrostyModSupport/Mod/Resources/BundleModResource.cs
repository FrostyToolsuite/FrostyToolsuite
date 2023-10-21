using Frosty.Sdk;
using Frosty.Sdk.IO;

namespace Frosty.ModSupport.Mod.Resources;

public class BundleModResource : BaseModResource
{
    public override ModResourceType Type => ModResourceType.Bundle;

    /// <summary>
    /// Fnv hash of the lowercase SuperBundle name of this bundle.
    /// </summary>
    public int SuperBundleHash { get; }
    
    public BundleModResource(DataStream inStream)
        : base(inStream)
    {
        SuperBundleHash = inStream.ReadInt32();
    }
    
    internal BundleModResource(string inName, int inSuperBundleHash)
        : base(-1, inName, Sha1.Zero, 0, 0, 0,
            string.Empty, Enumerable.Empty<int>(), Enumerable.Empty<int>())
    {
        SuperBundleHash = inSuperBundleHash;
    }

    public override void Write(DataStream stream)
    {
        base.Write(stream);
        
        stream.WriteInt32(SuperBundleHash);
    }
}