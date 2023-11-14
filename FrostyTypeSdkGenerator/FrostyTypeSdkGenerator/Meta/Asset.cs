namespace Frosty.Sdk.Ebx;

public partial class Asset
{
    [Frosty.Sdk.Attributes.IsReadOnlyAttribute()]
    [Frosty.Sdk.Attributes.CategoryAttribute("Annotations")]
    public Frostbite.Core.CString Name
    {
        get => _Name;
        set => _Name = value;
    }
}