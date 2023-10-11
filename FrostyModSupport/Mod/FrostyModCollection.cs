using Frosty.ModSupport.Interfaces;
using Frosty.ModSupport.Mod.Resources;

namespace Frosty.ModSupport.Mod;

public class FrostyModCollection : IResourceContainer
{
    public IEnumerable<BaseModResource> Resources { get; }
    
    public static FrostyModCollection Load(string inPath)
    {
        throw new NotImplementedException();
    }
    
    public static FrostyModDetails GetModDetails(string inPath)
    {
        throw new NotImplementedException();
    }
}