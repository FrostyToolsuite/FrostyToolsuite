using System;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate)]
public class EbxArrayMetaAttribute : Attribute
{
    public TypeFlags Flags { get; set; }

    public EbxArrayMetaAttribute(ushort flags)
    {
        Flags = flags;
    }
}