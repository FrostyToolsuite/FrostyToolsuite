using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.IO.RiffEbx;

internal struct EbxTypeDescriptor
{
    public uint NameHash;
    public int FieldIndex;
    public ushort FieldCount;
    public TypeFlags Flags;
    public ushort Size;
    public ushort Alignment;
}