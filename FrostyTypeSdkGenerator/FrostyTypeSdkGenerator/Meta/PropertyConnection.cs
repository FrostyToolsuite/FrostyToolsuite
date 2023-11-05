namespace Frosty.Sdk.Ebx;

public partial struct PropertyConnection
{
    [Frosty.Sdk.Attributes.IsHiddenAttribute()]
    public uint Flags
    {
        get => _Flags;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Enum)]
    public PropertyConnectionTargetType TargetType
    {
        get => (PropertyConnectionTargetType)(_Flags & 0x07u);
        set => _Flags = (_Flags & ~0x07u) | ((byte)value & 0x07u);
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool SourceCanNeverBeStatic
    {
        get => (_Flags & 0x08u) != 0;
        set => _Flags = value ? _Flags | 0x08u : _Flags & ~0x08u;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Enum)]
    public InputPropertyType InputPropertyType
    {
        get => (InputPropertyType)((_Flags & 0x30u) >> 4);
        set => _Flags = (_Flags & ~0x30u) | (((byte)value & 0x02u) << 4);
    }
}