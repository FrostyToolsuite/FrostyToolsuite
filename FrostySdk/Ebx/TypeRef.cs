using System;
using System.Reflection;
using Frosty.Sdk.Attributes;

namespace Frosty.Sdk.Ebx;

public struct TypeRef
{
    public string Name => m_typeName ?? string.Empty;
    public Guid Guid => m_typeGuid;

    private readonly Guid m_typeGuid;
    private readonly string? m_typeName;

    public TypeRef()
    {
        m_typeName = string.Empty;
    }

    public TypeRef(string value)
    {
        m_typeName = value;
    }

    public TypeRef(Guid guid)
    {
        m_typeGuid = guid;
        m_typeName = TypeLibrary.GetType(guid)?.GetCustomAttribute<DisplayNameAttribute>()?.Name ?? m_typeGuid.ToString();
    }

    public Type GetReferencedType()
    {
        if (m_typeGuid == Guid.Empty)
        {
            Type? refType = TypeLibrary.GetType(m_typeName!);
            if (refType == null)
            {
                throw new Exception($"Could not find the type {m_typeName}");
            }
            return refType;
        }
        else
        {

            Type? refType = TypeLibrary.GetType(m_typeGuid);
            if (refType == null)
            {
                throw new Exception($"Could not find the type {m_typeName}");
            }
            return refType;
        }
    }

    public static implicit operator string(TypeRef value) => value.m_typeName ?? "null";

    public static implicit operator TypeRef(string value) => new(value);

    public static implicit operator TypeRef(Guid guid) => new(guid);

    public bool IsNull() => string.IsNullOrEmpty(m_typeName);

    public override bool Equals(object? obj)
    {
        if (obj is not TypeRef b)
        {
            return false;
        }

        return m_typeName == b.m_typeName && m_typeGuid == b.m_typeGuid;
    }

    public bool Equals(TypeRef other)
    {
        return m_typeGuid == other.m_typeGuid && m_typeName == other.m_typeName;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(m_typeGuid, m_typeName);
    }

    public static bool operator ==(TypeRef a, object b) => a.Equals(b);

    public static bool operator !=(TypeRef a, object b) => !a.Equals(b);

    public override string ToString() => $"TypeRef '{(IsNull() ? "(null)" : m_typeName)}'";
}