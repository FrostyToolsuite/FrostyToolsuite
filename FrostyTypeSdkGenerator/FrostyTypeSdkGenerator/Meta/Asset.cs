namespace Frosty.Sdk.Ebx;

public partial class Asset
{
    [Frosty.Sdk.Attributes.IsReadOnlyAttribute()]
    [Frosty.Sdk.Attributes.CategoryAttribute("Annotations")]
    public Frosty.Sdk.Ebx.CString Name
    {
        get => _Name;
        set => _Name = value;
    }
}