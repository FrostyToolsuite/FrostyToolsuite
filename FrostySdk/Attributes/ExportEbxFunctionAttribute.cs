using System;
using Frosty.Sdk.Ebx;

namespace Frosty.Sdk.Attributes;

public delegate bool ExportEbxDelegate(EbxAsset inEntry, string inPath);

[AttributeUsage(AttributeTargets.Method)]
public class ExportEbxFunctionAttribute : Attribute
{
    public string Type { get; set; }

    public ExportEbxFunctionAttribute(string inType)
    {
        Type = inType;
    }
}