using System;

namespace Frosty.Sdk.Attributes;

/// <summary>
/// Overrides the display name of the property/class in the Property Grid
/// </summary>
[AttributeUsage(FrostyAttributeTargets.Type | FrostyAttributeTargets.Field, Inherited = false)]
public class DisplayNameAttribute : Attribute
{
    public string Name { get; set; }

    public DisplayNameAttribute(string name)
    {
        Name = name;
    }
}