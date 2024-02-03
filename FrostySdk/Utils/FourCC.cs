using System;

namespace Frosty.Sdk.Utils;

public struct FourCC
{
    private uint m_value;

    public static implicit operator uint(FourCC value) => value.m_value;
    public static implicit operator FourCC(uint value) => new(){m_value = value};

    public static implicit operator string(FourCC value) => FromFourCC(value);
    public static implicit operator FourCC(string value) => ToFourCC(value);

    public static bool operator ==(FourCC a, object b) => a.Equals(b);

    public static bool operator !=(FourCC a, object b) => !a.Equals(b);

    public override bool Equals(object? obj)
    {
        if (obj is uint b)
        {
            return b == m_value;
        }

        if (obj is string s)
        {
            return Equals(ToFourCC(s));
        }

        return obj is FourCC other && Equals(other);
    }

    public bool Equals(FourCC other)
    {
        return m_value == other.m_value;
    }

    public override int GetHashCode()
    {
        return (int)m_value;
    }

    public static string FromFourCC(FourCC value)
    {
        char[] result = new char[4];
        result[0] = (char)(value.m_value & 0xFF);
        result[1] = (char)((value.m_value >> 8) & 0xFF);
        result[2] = (char)((value.m_value >> 16) & 0xFF);
        result[3] = (char)((value.m_value >> 24) & 0xFF);
        return new string(result);
    }

    public static FourCC ToFourCC(string value)
    {
        if (value.Length != 4)
        {
            throw new Exception();
        }

        uint result = (uint)((byte)value[3] << 24
                             | (byte)value[2] << 16
                             | (byte)value[1] << 8
                             | (byte)value[0]);

        return new FourCC { m_value = result };
    }
}