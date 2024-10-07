using Frosty.Sdk;

namespace Frosty.ModSupport.Interfaces;

public interface IModEntry
{
    public Sha1 Sha1 { get; }

    public long OriginalSize { get; }

    public IHandler? Handler { get; set; }
}