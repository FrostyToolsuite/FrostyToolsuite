using Frosty.ModSupport.Mod.Resources;
using Frosty.Sdk.Utils;

namespace Frosty.ModSupport.Interfaces;

public interface IResourceContainer
{
    /// <summary>
    /// The Resources of this resource container.
    /// </summary>
    public IEnumerable<BaseModResource> Resources { get; }
    
    /// <summary>
    /// Gets the data of a resource
    /// </summary>
    /// <param name="inIndex">The index of the resource.</param>
    /// <returns></returns>
    public Block<byte> GetData(int inIndex);
}