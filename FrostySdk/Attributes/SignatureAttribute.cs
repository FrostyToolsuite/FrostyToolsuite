using System;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(FrostyAttributeTargets.Type)]
public class SignatureAttribute : Attribute
{
    public uint Signature { get; }

    public SignatureAttribute(uint inSignature)
    {
        Signature = inSignature;
    }
}