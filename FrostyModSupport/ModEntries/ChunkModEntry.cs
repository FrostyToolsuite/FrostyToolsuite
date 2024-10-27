using Frosty.ModSupport.Interfaces;
using Frosty.ModSupport.Mod.Resources;
using Frosty.Sdk;
using System;

namespace Frosty.ModSupport.ModEntries;

public class ChunkModEntry : IModEntry
{
    public Guid Id { get; }
    public Sha1 Sha1 { get; }
    public uint RangeStart { get; }
    public uint RangeEnd { get; }
    public uint LogicalOffset { get; }
    public uint LogicalSize { get; }
    public long OriginalSize => LogicalOffset & (ProfilesLibrary.MaxBufferSize - 1) | LogicalSize;
    public int H32 { get; }
    public int FirstMip { get; }
    public long Size { get; }
    public IHandler? Handler { get; set; }

    public ChunkModEntry(ChunkModResource inResource, long inSize)
    {
        Id = Guid.Parse(inResource.Name);
        Sha1 = inResource.Sha1;
        RangeStart = inResource.RangeStart;
        RangeEnd = inResource.RangeEnd;
        LogicalOffset = inResource.LogicalOffset;
        LogicalSize = inResource.LogicalSize;
        H32 = inResource.H32;
        FirstMip = inResource.FirstMip;
        Size = inSize;
    }

    public ChunkModEntry(Guid inId, Sha1 inSha1, uint inLogicalOffset, uint inLogicalSize)
    {
        Id = inId;
        Sha1 = inSha1;
        LogicalOffset = inLogicalOffset;
        LogicalSize = inLogicalSize;
    }
}