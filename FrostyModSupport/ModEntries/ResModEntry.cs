using Frosty.ModSupport.Interfaces;
using Frosty.ModSupport.Mod.Resources;
using Frosty.Sdk;

namespace Frosty.ModSupport.ModEntries;

public class ResModEntry : IModEntry
{
    public string Name { get; }
    public Sha1 Sha1 { get; }
    public long OriginalSize { get; }
    public ulong ResRid { get; }
    public uint ResType { get; }
    public byte[] ResMeta { get; }
    public long Size { get; }
    public IHandler? Handler { get; set; }

    public ResModEntry(ResModResource inResource, long inSize)
    {
        Name = inResource.Name;
        Sha1 = inResource.Sha1;
        OriginalSize = inResource.OriginalSize;
        ResRid = inResource.ResRid;
        ResType = inResource.ResType;
        ResMeta = inResource.ResMeta;
        Size = inSize;
    }
}