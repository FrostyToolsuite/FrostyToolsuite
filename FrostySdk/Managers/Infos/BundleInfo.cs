using Frosty.Sdk.Managers.Entries;

namespace Frosty.Sdk.Managers.Infos;

public class BundleInfo
{
    public string Name { get; }
    
    public int Id { get; }
    
    public SuperBundleInstallChunk Parent { get; }
    
    // public BundleType Type { get; internal set; }

    public BundleInfo(string inName, SuperBundleInstallChunk inParent)
    {
        Name = inName;
        Parent = inParent;
        Id = Utils.Utils.HashString(Name + Parent.Name, true);
        Parent.BundleMapping.Add(Name, this);
    }
}