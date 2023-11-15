namespace Frosty.Sdk.Ebx;

public partial class EntityBusData
{
    [IsTransientAttribute()]
    [EbxFieldMetaAttribute(TypeFlags.TypeEnum.Boolean)]
    public bool EntityBusClientFlag
    {
        get => (Flags & 0x8u) != 0;
        set => Flags = (ushort)(value ? Flags | 0x8u : Flags & ~0x8u);
    }

    [IsTransientAttribute()]
    [EbxFieldMetaAttribute(TypeFlags.TypeEnum.Boolean)]
    public bool EntityBusServerFlag
    {
        get => (Flags & 0x10u) != 0;
        set => Flags = (ushort)(value ? Flags | 0x10u : Flags & ~0x10u);
    }
}