namespace Frosty.Sdk.Ebx;

public partial class Asset
{
    [IsReadOnlyAttribute()]
    [CategoryAttribute("Annotations")]
    public CString Name { get; set; }
}