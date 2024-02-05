using System;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(FrostyAttributeTargets.Type, Inherited = false)]
public class ArrayHashAttribute : Attribute
{
    public uint Hash { get; set; }

    public ArrayHashAttribute(uint inHash)
    {
        Hash = inHash;
    }
}