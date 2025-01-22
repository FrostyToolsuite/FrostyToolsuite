using System;
using System.Reflection;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.Ebx;

public struct BoxedValueRef : IEquatable<BoxedValueRef>
{
    public object? Value => m_value;
    public TypeFlags.TypeEnum Type => m_flags.GetTypeEnum();
    public TypeFlags.CategoryEnum Category => m_flags.GetCategoryEnum();

    private object? m_value;
    private TypeFlags m_flags;

    private static readonly string s_collectionName = "ObservableCollection`1";

    public BoxedValueRef()
    {
    }

    public BoxedValueRef(object? inValue, TypeFlags inFlags)
    {
        m_value = inValue;
        m_flags = inFlags;
    }

    public void SetValue(object inValue)
    {
        m_value = inValue;
    }

    public override string ToString()
    {
        if (m_value is null)
        {
            return "BoxedValueRef '(null)'";
        }

        Type type = m_value.GetType();

        return
            $"BoxedValueRef '{(type.Name == s_collectionName ? $"Array<{type.GenericTypeArguments[0].GetName()}>" : type == typeof(PointerRef) ? "Class" : type.GetName())}'";
    }

    public override bool Equals(object? obj)
    {
        if (obj is not BoxedValueRef b)
        {
            return false;
        }

        return Equals(b);
    }

    public bool Equals(BoxedValueRef other)
    {
        return m_value == other.m_value && m_flags == other.m_flags;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(m_value, (ushort)m_flags);
    }

    public static bool operator ==(BoxedValueRef a, object b) => a.Equals(b);

    public static bool operator !=(BoxedValueRef a, object b) => !a.Equals(b);
}