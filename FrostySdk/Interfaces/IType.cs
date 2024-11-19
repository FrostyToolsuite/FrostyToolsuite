using System;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.Interfaces;

public interface IType
{
    public string Name { get; }
    public uint NameHash { get; }
    public Guid Guid { get; }
    public uint Signature { get; }
    public Type Type { get; }

    public bool IsSubClassOf(IType inType);

    public TypeFlags GetFlags();
}