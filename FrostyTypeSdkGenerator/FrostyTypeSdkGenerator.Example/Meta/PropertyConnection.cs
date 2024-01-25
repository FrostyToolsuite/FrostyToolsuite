using Frosty.Sdk.Attributes;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.Ebx;

public partial struct PropertyConnection
{
    [OverrideAttribute]
    [IsHiddenAttribute()]
    public uint Flags { get; set; }

    [DependsOnAttribute(Flags)]
    [IsTransientAttribute()]
    [EbxFieldMetaAttribute(TypeFlags.TypeEnum.Enum)]
    public PropertyConnectionTargetType TargetType
    {
        get => (PropertyConnectionTargetType)(Flags & 0x07u);
        set => Flags = (Flags & ~0x07u) | ((byte)value & 0x07u);
    }

    [DependsOnAttribute(Flags)]
    [IsTransientAttribute()]
    [EbxFieldMetaAttribute(TypeFlags.TypeEnum.Boolean)]
    public bool SourceCanNeverBeStatic
    {
        get => (Flags & 0x08u) != 0;
        set => Flags = value ? Flags | 0x08u : Flags & ~0x08u;
    }

    [DependsOnAttribute(Flags)]
    [EbxFieldMetaAttribute(TypeFlags.TypeEnum.Enum)]
    public InputPropertyType InputPropertyType
    {
        get => (InputPropertyType)((Flags & 0x30u) >> 4);
        set => Flags = (Flags & ~0x30u) | (((byte)value & 0x02u) << 4);
    }
}