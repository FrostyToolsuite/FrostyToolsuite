using Frosty.Sdk;

namespace Frosty.ModSupport.ModEntries;

public class EbxModEntry
{
    public string Name { get; }
    public Sha1 Sha1 { get; }
    public long OriginalSize { get; }
    public long Size { get; }
    
}