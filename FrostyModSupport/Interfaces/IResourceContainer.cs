using Frosty.ModSupport.Mod.Resources;

namespace Frosty.ModSupport.Interfaces;

public interface IResourceContainer
{
    public IEnumerable<BaseModResource> Resources { get; }
}