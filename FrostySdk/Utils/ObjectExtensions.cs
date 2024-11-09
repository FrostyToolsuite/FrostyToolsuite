using System;
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
}