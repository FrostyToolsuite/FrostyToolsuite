namespace Frosty.Sdk.Ebx;

public partial class DataBusPeer
{
    [Frosty.Sdk.Attributes.IsHiddenAttribute()]
    public uint Flags { get; set; }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool IsClientEventConnectionTarget
    {
        get => (Flags & 0x02000000u) != 0;
        set => Flags = value ? Flags | 0x02000000u : Flags & ~0x02000000u;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool IsServerEventConnectionTarget
    {
        get => (Flags & 0x04000000u) != 0;
        set => Flags = value ? Flags | 0x04000000u : Flags & ~0x04000000u;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool IsClientPropertyConnectionTarget
    {
        get => (Flags & 0x08000000u) != 0;
        set => Flags = value ? Flags | 0x08000000u : Flags & ~0x08000000u;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool IsServerPropertyConnectionTarget
    {
        get => (Flags & 0x10000000u) != 0;
        set => Flags = value ? _Flags | 0x10000000u : Flags & ~0x10000000u;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool IsClientLinkConnectionSource
    {
        get => (Flags & 0x20000000u) != 0;
        set => Flags = value ? Flags | 0x20000000u : Flags & ~0x20000000u;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool IsServerLinkConnectionSource
    {
        get => (Flags & 0x40000000u) != 0;
        set => Flags = value ? Flags | 0x40000000u : Flags & ~0x40000000u;
    }
}