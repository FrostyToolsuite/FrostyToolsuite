namespace Frosty.ModSupport.Mod;

public class FrostyModCollection
{
    public IEnumerable<FrostyMod> Mods { get; }
    
    public static FrostyModCollection? Load(string inPath)
    {
        throw new NotImplementedException();
    }
    
    public static FrostyModDetails GetModDetails(string inPath)
    {
        throw new NotImplementedException();
    }
}