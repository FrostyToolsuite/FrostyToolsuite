using Frosty.ModSupport.Interfaces;
using Frosty.ModSupport.Mod.Resources;
using Frosty.Sdk;

namespace Frosty.ModSupport.ModEntries;

public class EbxModEntry : IModEntry
{
    public string Name { get; }
    public Sha1 Sha1 { get; }
    public long OriginalSize { get; }
    public long Size { get; }
    public IHandler? Handler { get; set; }

    public EbxModEntry(EbxModResource inResource, long inSize)
    {
        Name = inResource.Name;
        Sha1 = inResource.Sha1;
        OriginalSize = inResource.OriginalSize;
        Size = inSize;
    }
}