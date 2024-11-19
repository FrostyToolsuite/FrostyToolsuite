using System;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.Attributes;

/// <summary>
/// Mandatory attribute for all Ebx based fields
/// </summary>
[AttributeUsage(FrostyAttributeTargets.Field)]
public class EbxFieldMetaAttribute : Attribute
{
    public TypeFlags Flags { get; set; }
    public uint Offset { get; set; }
    public Type? BaseType { get; set; }

    public EbxFieldMetaAttribute(ushort flags, uint offset, Type? baseType)
    {
        Flags = flags;
        Offset = offset;
        BaseType = baseType;
    }

    public EbxFieldMetaAttribute(TypeFlags.TypeEnum type, uint offset = 0, string baseType = "")
    {
        if (!string.IsNullOrEmpty(baseType))
        {
            BaseType = TypeLibrary.GetType(baseType)?.Type;
        }

        Flags = new TypeFlags(type);
        Offset = offset;
    }
}