namespace Frosty.Sdk.Ebx;

public partial class Asset
{
    [OverrideAttribute]
    [IsReadOnlyAttribute()]
    [CategoryAttribute("Annotations")]
    public CString Name { get; set; }
}