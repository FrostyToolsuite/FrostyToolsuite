using System;

namespace Frosty.Sdk.Attributes;

public class ArrayNameAttribute : Attribute
{
    public string Name { get; }

    public ArrayNameAttribute(string inName)
    {
        Name = inName;
    }
}