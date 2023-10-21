using Frosty.Sdk;
using Frosty.Sdk.IO;

namespace Frosty.ModSupport.Mod.Resources;

public class ChunkModResource : BaseModResource
{
    public override ModResourceType Type => ModResourceType.Chunk;

    public uint RangeStart { get; }
    public uint RangeEnd { get; }
    public uint LogicalOffset { get; }
    public uint LogicalSize { get; }
    public int H32 { get; }
    public int FirstMip { get; }
    public IEnumerable<int> AddedSuperBundles => m_superBundlesToAdd;
    public IEnumerable<int> RemovedSuperBundles => m_superBundlesToRemove;
    
    protected HashSet<int> m_superBundlesToAdd = new();
    protected HashSet<int> m_superBundlesToRemove = new();

    public ChunkModResource(DataStream inStream)
        : base(inStream)
    {
        RangeStart = inStream.ReadUInt32();
        RangeEnd = inStream.ReadUInt32();
        LogicalOffset = inStream.ReadUInt32();
        LogicalSize = inStream.ReadUInt32();
        H32 = inStream.ReadInt32();
        FirstMip = inStream.ReadInt32();
        
        int addedCount = inStream.ReadInt32();
        for (int i = 0; i < addedCount; i++)
        {
            m_superBundlesToAdd.Add(inStream.ReadInt32());
        }
        
        int removedCount = inStream.ReadInt32();
        for (int i = 0; i < removedCount; i++)
        {
            m_superBundlesToRemove.Add(inStream.ReadInt32());
        }
    }

    public ChunkModResource(int inResourceIndex, string inName, Sha1 inSha1, long inOriginalSize,
        ResourceFlags inFlags, int inHandlerHash, string inUserData, IEnumerable<int> inBundlesToAdd,
        IEnumerable<int> inBundlesToRemove, uint inRangeStart, uint inRangeEnd, uint inLogicalOffset,
        uint inLogicalSize, int inH32, int inFirstMip, IEnumerable<int> inSuperBundlesToAdd,
        IEnumerable<int> inSuperBundlesToRemove)
        : base(inResourceIndex, inName, inSha1, inOriginalSize, inFlags, inHandlerHash,
            inUserData, inBundlesToAdd, inBundlesToRemove)
    {
        RangeStart = inRangeStart;
        RangeEnd = inRangeEnd;
        LogicalOffset = inLogicalOffset;
        LogicalSize = inLogicalSize;
        H32 = inH32;
        FirstMip = inFirstMip;
        m_superBundlesToAdd.UnionWith(inSuperBundlesToAdd);
        m_superBundlesToRemove.UnionWith(inSuperBundlesToRemove);
    }

    public override void Write(DataStream stream)
    {
        base.Write(stream);
        
        stream.WriteUInt32(RangeStart);
        stream.WriteUInt32(RangeEnd);
        stream.WriteUInt32(LogicalOffset);
        stream.WriteUInt32(LogicalSize);
        stream.WriteInt32(H32);
        stream.WriteInt32(FirstMip);
        
        stream.WriteInt32(m_superBundlesToAdd.Count);
        foreach (int superBundleId in m_superBundlesToAdd)
        {
            stream.WriteInt32(superBundleId);
        }
        
        stream.WriteInt32(m_superBundlesToRemove.Count);
        foreach (int superBundleId in m_superBundlesToRemove)
        {
            stream.WriteInt32(superBundleId);
        }
    }
}