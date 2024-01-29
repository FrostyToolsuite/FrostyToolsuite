using System;
using System.Reflection;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.Ebx;

public struct BoxedValueRef
{
    public object? Value => m_value;
    public TypeFlags.TypeEnum Type => m_type;
    public TypeFlags.CategoryEnum Category => m_category;

    private object? m_value;
    private readonly TypeFlags.TypeEnum m_type;
    private readonly TypeFlags.CategoryEnum m_category;

    public BoxedValueRef()
    {
    }

    public BoxedValueRef(object? inValue, TypeFlags.TypeEnum inType)
    {
        m_value = inValue;
        m_type = inType;
    }

    public BoxedValueRef(object? inValue, TypeFlags.TypeEnum inType, TypeFlags.CategoryEnum inCategory)
    {
        m_value = inValue;
        m_type = inType;
        m_category = inCategory;
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
            $"BoxedValueRef '{(m_type == TypeFlags.TypeEnum.Array ? $"Array<{type.GenericTypeArguments[0].GetCustomAttribute<DisplayNameAttribute>()!.Name}>" : type.GetCustomAttribute<DisplayNameAttribute>()!.Name)}'";
    }

    public override bool Equals(object? obj)
    {
        if (obj is not BoxedValueRef b)
        {
            return false;
        }

        return m_value == b.m_value && m_type == b.m_type && m_category == b.m_category;
    }

    public bool Equals(BoxedValueRef other)
    {
        return Equals(m_value, other.m_value) && m_type == other.m_type && m_category == other.m_category;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(m_value, (int)m_type, (int)m_category);
    }

    public static bool operator ==(BoxedValueRef a, object b) => a.Equals(b);

    public static bool operator !=(BoxedValueRef a, object b) => !a.Equals(b);
}