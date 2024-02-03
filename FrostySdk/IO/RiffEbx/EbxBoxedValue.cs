using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.IO.RiffEbx;

internal struct EbxBoxedValue
{
    public uint Offset;
    public int Count;
    public uint Hash;
    public TypeFlags Flags;
    public ushort TypeDescriptorRef;
}