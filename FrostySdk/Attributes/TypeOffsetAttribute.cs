using System;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(FrostyAttributeTargets.Type, Inherited = false)]
public class TypeOffsetAttribute : Attribute
{
    public long Offset { get; }

    public TypeOffsetAttribute(long inOffset)
    {
        Offset = inOffset;
    }
}