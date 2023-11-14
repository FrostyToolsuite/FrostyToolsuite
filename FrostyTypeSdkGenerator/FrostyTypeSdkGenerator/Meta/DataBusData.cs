namespace Frosty.Sdk.Ebx;

public partial class DataBusData
{
    [Frosty.Sdk.Attributes.IsHiddenAttribute()]
    public uint Flags { get; set; }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool NeedsNetworkId
    {
        get => (Flags & 0x1u) != 0;
        set => Flags = value ? Flags | 0x1u : Flags & ~0x1u;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool InterfaceHasConnections
    {
        get => (Flags & 0x2u) != 0;
        set => Flags = value ? Flags | 0x2u : Flags & ~0x2u;
    }
}