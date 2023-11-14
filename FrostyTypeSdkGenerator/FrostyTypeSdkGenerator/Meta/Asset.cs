namespace Frosty.Sdk.Ebx;

public partial class Asset
{
    [Frosty.Sdk.Attributes.IsReadOnlyAttribute()]
    [Frosty.Sdk.Attributes.CategoryAttribute("Annotations")]
    public CString Name { get; set; }
}