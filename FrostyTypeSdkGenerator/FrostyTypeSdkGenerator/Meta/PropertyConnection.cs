namespace Frosty.Sdk.Ebx;

public partial struct PropertyConnection
{
    [Frosty.Sdk.Attributes.IsHiddenAttribute()]
    public uint Flags { get; set; }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Enum)]
    public PropertyConnectionTargetType TargetType
    {
        get => (PropertyConnectionTargetType)(Flags & 0x07u);
        set => Flags = (Flags & ~0x07u) | ((byte)value & 0x07u);
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool SourceCanNeverBeStatic
    {
        get => (Flags & 0x08u) != 0;
        set => Flags = value ? Flags | 0x08u : Flags & ~0x08u;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Enum)]
    public InputPropertyType InputPropertyType
    {
        get => (InputPropertyType)((Flags & 0x30u) >> 4);
        set => Flags = (Flags & ~0x30u) | (((byte)value & 0x02u) << 4);
    }
}