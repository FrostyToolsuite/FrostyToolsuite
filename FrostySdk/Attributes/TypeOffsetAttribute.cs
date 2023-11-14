using System;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate, Inherited = false)]
public class TypeOffsetAttribute : Attribute
{
    public long Offset { get; }

    public TypeOffsetAttribute(long inOffset)
    {
        Offset = inOffset;
    }
}