using System;
using System.Collections.Generic;

namespace Frosty.Sdk.Ebx;

public struct TypeRef
{
    public string Name => m_typeName ?? string.Empty;
    public Guid Guid => m_typeGuid;
    public Type? Type => m_type;

    private readonly Guid m_typeGuid;
    private readonly string? m_typeName;
    private readonly Type? m_type;

    public TypeRef()
    {
        m_typeName = string.Empty;
    }

    public TypeRef(string inName)
    {
        m_type = TypeLibrary.GetType(inName);
        m_typeName = inName;
        m_typeGuid = m_type?.GetGuid() ?? Guid.Empty;
    }

    public TypeRef(Guid inGuid)
    {
        m_type = TypeLibrary.GetType(inGuid);
        m_typeGuid = inGuid;
        m_typeName = m_type?.GetName() ?? m_typeGuid.ToString();
    }

    public TypeRef(Type inType)
    {
        m_type = inType;
        m_typeGuid = inType.GetGuid();
        m_typeName = inType.GetName();
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