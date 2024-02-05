using System;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(FrostyAttributeTargets.Type | FrostyAttributeTargets.Field, Inherited = false)]
public class NameHashAttribute : Attribute
{
    public uint Hash { get; }
    public NameHashAttribute(uint inHash) { Hash = inHash; }
}