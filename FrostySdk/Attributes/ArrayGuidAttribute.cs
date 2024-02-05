using System;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(FrostyAttributeTargets.Type, Inherited = false)]
public class ArrayGuidAttribute : Attribute
{
    public Guid Guid { get; }

    public ArrayGuidAttribute(string guid)
    {
        Guid = Guid.Parse(guid);
    }
}