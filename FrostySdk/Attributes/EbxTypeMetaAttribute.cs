using System;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.Attributes;

/// <summary>
/// Mandatory attribute for all Ebx based classes
/// </summary>
[AttributeUsage(FrostyAttributeTargets.Type)]
public class EbxTypeMetaAttribute : Attribute
{
    public TypeFlags Flags { get; set; }
    public byte Alignment { get; set; }
    public ushort Size { get; set; }

    public EbxTypeMetaAttribute(ushort inFlags, byte inAlignment, ushort inSize)
    {
        Flags = inFlags;
        Alignment = inAlignment;
        Size = inSize;
    }

    public EbxTypeMetaAttribute(TypeFlags.TypeEnum type, TypeFlags.CategoryEnum category = TypeFlags.CategoryEnum.None)
    {
        Flags = new TypeFlags(type, category);
    }
}