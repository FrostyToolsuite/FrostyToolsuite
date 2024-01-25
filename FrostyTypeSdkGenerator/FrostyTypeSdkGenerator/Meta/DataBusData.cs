namespace Frosty.Sdk.Ebx;

public partial class DataBusData
{
    [OverrideAttribute]
    [IsHiddenAttribute()]
    public ushort Flags { get; set; }

    [IsTransientAttribute()]
    [EbxFieldMetaAttribute(TypeFlags.TypeEnum.Boolean)]
    public bool NeedsNetworkId
    {
        get => (Flags & 0x1u) != 0;
        set => Flags = (ushort)(value ? Flags | 0x1u : Flags & ~0x1u);
    }

    [IsTransientAttribute()]
    [EbxFieldMetaAttribute(TypeFlags.TypeEnum.Boolean)]
    public bool InterfaceHasConnections
    {
        get => (Flags & 0x2u) != 0;
        set => Flags = (ushort)(value ? Flags | 0x2u : Flags & ~0x2u);
    }
}