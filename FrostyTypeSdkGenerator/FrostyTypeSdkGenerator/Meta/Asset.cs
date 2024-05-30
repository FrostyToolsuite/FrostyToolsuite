namespace Frosty.Sdk.Ebx;

public partial class Asset
{
    [OverrideAttribute]
    [IsReadOnlyAttribute()]
    [CategoryAttribute("Annotations")]
    public string Name { get; set; }
}