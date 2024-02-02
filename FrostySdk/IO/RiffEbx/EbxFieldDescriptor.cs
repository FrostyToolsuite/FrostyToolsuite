using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.IO.RiffEbx;

public struct EbxFieldDescriptor
{
    public uint NameHash;
    public uint DataOffset;
    public TypeFlags Flags;
    public ushort TypeRef;
}