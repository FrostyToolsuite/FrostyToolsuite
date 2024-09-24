using System.Linq;
using Frosty.Sdk;
using Frosty.Sdk.IO;

namespace Frosty.ModSupport.Mod.Resources;

public class BundleModResource : BaseModResource
{
    public override ModResourceType Type => ModResourceType.Bundle;

    public int BundleHash { get; }

    /// <summary>
    /// Fnv hash of the lowercase SuperBundle name of this bundle.
    /// </summary>
    public int SuperBundleHash { get; }

    public BundleModResource(DataStream inStream)
        : base(inStream)
    {
        BundleHash = inStream.ReadInt32();
        SuperBundleHash = inStream.ReadInt32();
    }

    internal BundleModResource(string inName, int inBundleHash, int inSuperBundleHash)
        : base(-1, inName, Sha1.Zero, 0, 0, 0,
            string.Empty, Enumerable.Empty<int>(), Enumerable.Empty<int>())
    {
        BundleHash = inBundleHash;
        SuperBundleHash = inSuperBundleHash;
    }

    public override void Write(DataStream stream)
    {
        base.Write(stream);

        stream.WriteInt32(BundleHash);
        stream.WriteInt32(SuperBundleHash);
    }
}