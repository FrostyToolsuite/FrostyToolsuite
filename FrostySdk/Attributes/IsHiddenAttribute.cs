﻿using System;

namespace Frosty.Sdk.Attributes;

/// <summary>
/// Specifies that this property is hidden from the property grid
/// </summary>
[AttributeUsage(FrostyAttributeTargets.Field)]
public class IsHiddenAttribute : Attribute
{
}