using System;

namespace Frosty.Sdk.Exceptions;

public class MissingEncryptionKeyException : Exception
{
    public override string Message => $"Missing encryption key for {m_key}";
    
    private string m_key;

    public MissingEncryptionKeyException(string inKey)
    {
        m_key = inKey;
    }
}