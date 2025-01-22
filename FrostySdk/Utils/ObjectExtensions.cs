using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Frosty.Sdk.Interfaces;

namespace Frosty.Sdk.Utils;

public static class ObjectExtensions
{
    public static T GetProperty<T>(this object obj, string inPropertyName)
    {
        Type type = obj.GetType();
        PropertyInfo? property = type.GetProperty(inPropertyName);
        if (property is null)
        {
            throw new Exception($"Property  {inPropertyName} is not found in type {type}");
        }
        object? value = property.GetValue(obj);
        if (value is T result)
        {
            return result;
        }
        if (value is IPrimitive primitive && primitive.ToActualType() is T result2)
        {
            return result2;
        }
        throw new InvalidCastException();
    }

    public static bool TryGetProperty<T>(this object obj, string inPropertyName, [NotNullWhen(true)] out T? result)
    {
        Type type = obj.GetType();
        PropertyInfo? property = type.GetProperty(inPropertyName);
        if (property is null)
        {
            result = default;
            return false;
        }
        object? value = property.GetValue(obj);
        if (value is T temp)
        {
            result = temp;
            return true;
        }
        if (value is IPrimitive primitive && primitive.ToActualType() is T result2)
        {
            result = result2;
        }

        result = default;
        return false;
    }
}