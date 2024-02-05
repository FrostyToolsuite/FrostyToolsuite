using System;

namespace Frosty.Sdk.Attributes;

/// <summary>
/// Specifies that this property should not be saved
/// </summary>
[AttributeUsage(FrostyAttributeTargets.Field)]
public class IsTransientAttribute : Attribute
{
}