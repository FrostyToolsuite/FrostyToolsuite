using System;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(FrostyAttributeTargets.Type)]
public class ArraySignatureAttribute : Attribute
{
    public uint Signature { get; }

    public ArraySignatureAttribute(uint inSignature)
    {
        Signature = inSignature;
    }
}