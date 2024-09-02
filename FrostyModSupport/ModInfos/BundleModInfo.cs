namespace Frosty.ModSupport.ModInfos;

public class BundleModInfo
{
    public string? Name;
    public BundleModAction Added = new();
    public BundleModAction Removed = new();
    public BundleModAction Modified = new();
}