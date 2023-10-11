namespace Frosty.Sdk.Ebx;

public partial class DataBusPeer
{
    [Frosty.Sdk.Attributes.IsHiddenAttribute()]
    public int Flags
    {
        get => _Flags;
        set => _Flags = value;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool IsClientEventConnectionTarget
    {
        get => (_Flags & 0x02000000) != 0;
        set => _Flags = value ? _Flags | 0x02000000 : _Flags & ~0x02000000;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool IsServerEventConnectionTarget
    {
        get => (_Flags & 0x04000000) != 0;
        set => _Flags = value ? _Flags | 0x04000000 : _Flags & ~0x04000000;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool IsClientPropertyConnectionTarget
    {
        get => (_Flags & 0x08000000) != 0;
        set => _Flags = value ? _Flags | 0x08000000 : _Flags & ~0x08000000;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool IsServerPropertyConnectionTarget
    {
        get => (_Flags & 0x10000000) != 0;
        set => _Flags = value ? _Flags | 0x10000000 : _Flags & ~0x10000000;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool IsClientLinkConnectionSource
    {
        get => (_Flags & 0x20000000) != 0;
        set => _Flags = value ? _Flags | 0x20000000 : _Flags & ~0x20000000;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool IsServerLinkConnectionSource
    {
        get => (_Flags & 0x40000000) != 0;
        set => _Flags = value ? _Flags | 0x40000000 : _Flags & ~0x40000000;
    }
}