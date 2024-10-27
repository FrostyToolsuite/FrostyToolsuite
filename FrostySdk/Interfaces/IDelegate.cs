using System;

namespace Frosty.Sdk.Interfaces;

public interface IDelegate
{
    public Type? FunctionType { get; set; }
}