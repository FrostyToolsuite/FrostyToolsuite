using Frosty.ModSupport.Interfaces;
using Frosty.Sdk;

namespace Frosty.ModSupport.ModEntries;

public interface IModEntry
{
    public Sha1 Sha1 { get; }
    
    public IHandler? Handler { get; set; }
}