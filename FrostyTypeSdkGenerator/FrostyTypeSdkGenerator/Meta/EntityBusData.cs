namespace Frosty.Sdk.Ebx;

public partial class EntityBusData
{
    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool EntityBusClientFlag
    {
        get => (Flags & 0x8u) != 0;
        set => Flags = value ? Flags | 0x8u : Flags & ~0x8u;
    }

    [Frosty.Sdk.Attributes.IsTransientAttribute()]
    [Frosty.Sdk.Attributes.EbxFieldMetaAttribute(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.Boolean)]
    public bool EntityBusServerFlag
    {
        get => (Flags & 0x10u) != 0;
        set => Flags = value ? Flags | 0x10u : Flags & ~0x10u;
    }
}