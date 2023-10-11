using System;

namespace Frosty.Sdk.Exceptions;

public class UnknownValueException<T> : Exception
{
    public override string Message => $"Unknown value of {m_name}: {m_value}.";

    private readonly string m_name;
    private readonly T m_value;
    
    public UnknownValueException(string inName, T inValue)
    {
        m_name = inName;
        m_value = inValue;
    }
}