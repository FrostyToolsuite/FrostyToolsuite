using System;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.Sdk;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk;

public class TypeInfoAsset : IType
{
    public string Name { get; }
    public uint NameHash { get; }
    public Guid Guid { get; }
    public uint Signature { get; }
    public Type Type => throw new InvalidOperationException();

    public TypeInfoAsset(Guid inGuid, object inAsset)
    {
        Name = inAsset.GetProperty<string>("TypeName");
        NameHash = inAsset.GetProperty<uint>("TypeNameHash");
        Guid = inGuid;
        Signature = 0;
    }

    public TypeInfoAsset(string inName, uint inNameHash, Guid inGuid)
    {
        Name = inName;
        NameHash = inNameHash;
        Guid = inGuid;
        Signature = 0;
    }

    public bool IsSubClassOf(IType inType)
    {
        throw new NotImplementedException();
    }

    public TypeFlags GetFlags()
    {
        // i dont think these types ever get written in ebx
        return 0;
    }
}