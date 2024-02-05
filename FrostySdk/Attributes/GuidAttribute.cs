using System;

namespace Frosty.Sdk.Attributes;

/// <summary>
/// Specifies the guid for the class, used when looking up type refs
/// </summary>
[AttributeUsage(FrostyAttributeTargets.Type, Inherited = false)]
public class GuidAttribute : Attribute
{
    public Guid Guid { get; set; }

    public GuidAttribute(string inGuid)
    {
        Guid = Guid.Parse(inGuid);
    }
}