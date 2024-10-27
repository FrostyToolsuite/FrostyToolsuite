using Frosty.Sdk.Sdk;
using System;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(FrostyAttributeTargets.Type | FrostyAttributeTargets.Field)]
public class EbxArrayMetaAttribute : Attribute
{
    public TypeFlags Flags { get; set; }

    public EbxArrayMetaAttribute(ushort flags)
    {
        Flags = flags;
    }
}