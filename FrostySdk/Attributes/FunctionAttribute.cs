using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(AttributeTargets.Struct)]
public class FunctionAttribute : Attribute
{
    public string[] ArgumentTypes { get; }

    public FunctionAttribute(params string[] inArgumentTypes)
    {
        ArgumentTypes = inArgumentTypes;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is not FunctionAttribute other)
        {
            return false;
        }

        return other.ArgumentTypes.SequenceEqual(ArgumentTypes);
    }

    protected bool Equals(FunctionAttribute other)
    {
        return base.Equals(other) && ArgumentTypes.Equals(other.ArgumentTypes);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(base.GetHashCode(), ArgumentTypes);
    }
}