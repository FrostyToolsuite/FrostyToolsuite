using System;

namespace Frosty.Sdk.Ebx;

public readonly struct TypeRef
{
    public string? Name => m_type?.GetName();
    public Guid Guid => m_type?.GetGuid() ?? Guid.Empty;
    public Type? Type => m_type;

    private readonly Type? m_type;

    public TypeRef()
    {
    }

    public TypeRef(string inName)
    {
        m_type = TypeLibrary.GetType(inName);
    }

    public TypeRef(Guid inGuid)
    {
        m_type = TypeLibrary.GetType(inGuid);
    }

    public TypeRef(Type inType)
    {
        m_type = inType;
    }

    public static implicit operator string(TypeRef value) => value.Name ?? "null";

    public static implicit operator TypeRef(string value) => new(value);

    public static implicit operator TypeRef(Guid guid) => new(guid);

    public bool IsNull() => m_type is null;

    public override bool Equals(object? obj)
    {
        if (obj is not TypeRef b)
        {
            return false;
        }

        return Equals(b);
    }

    public bool Equals(TypeRef other)
    {
        return Guid == other.Guid;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Guid);
    }

    public static bool operator ==(TypeRef a, object b) => a.Equals(b);

    public static bool operator !=(TypeRef a, object b) => !a.Equals(b);

    public override string ToString() => $"TypeRef '{(IsNull() ? "(null)" : Name)}'";
}