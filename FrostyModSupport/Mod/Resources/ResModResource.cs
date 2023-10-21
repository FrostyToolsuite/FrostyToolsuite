using Frosty.Sdk;
using Frosty.Sdk.IO;

namespace Frosty.ModSupport.Mod.Resources;

public class ResModResource : BaseModResource
{
    public override ModResourceType Type => ModResourceType.Res;

    public uint ResType { get; }
    public ulong ResRid { get; }
    public byte[] ResMeta { get; }

    public ResModResource(DataStream inStream)
        : base(inStream)
    {
        ResType = inStream.ReadUInt32();
        ResRid = inStream.ReadUInt64();
        ResMeta = inStream.ReadBytes(inStream.ReadInt32());
    }
    
    public ResModResource(int inResourceIndex, string inName, Sha1 inSha1, long inOriginalSize,
        ResourceFlags inFlags, int inHandlerHash, string inUserData, IEnumerable<int> inBundlesToAdd,
        IEnumerable<int> inBundlesToRemove, uint inResType, ulong inResRid, byte[] inResMeta)
        : base(inResourceIndex, inName, inSha1, inOriginalSize, inFlags, inHandlerHash,
            inUserData, inBundlesToAdd, inBundlesToRemove)
    {
        ResType = inResType;
        ResRid = inResRid;
        ResMeta = inResMeta;
    }

    public override void Write(DataStream stream)
    {
        base.Write(stream);
        
        stream.WriteUInt32(ResType);
        stream.WriteUInt64(ResRid);
        stream.WriteInt32(ResMeta.Length);
        stream.Write(ResMeta);
    }
}