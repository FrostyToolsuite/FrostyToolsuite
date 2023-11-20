using System;
using System.Reflection;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.IO.Ebx;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.Ebx;

public class BoxedValueRef
{
    public object? Value => m_value;
    public TypeFlags.TypeEnum Type => m_type;
    public EbxFieldCategory Category => m_category;

    private object? m_value;
    private readonly TypeFlags.TypeEnum m_type;
    private readonly EbxFieldCategory m_category;

    public BoxedValueRef()
    {
    }

    public BoxedValueRef(object? inValue, TypeFlags.TypeEnum inType)
    {
        m_value = inValue;
        m_type = inType;
    }

    public BoxedValueRef(object? inValue, TypeFlags.TypeEnum inType, EbxFieldCategory inCategory)
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
}