namespace Frosty.Sdk.IO.PartitionEbx;

public struct EbxArray
{
    public uint Offset;
    public uint Count;

    public int TypeDescriptorRef;

    // Only needed for writer
    public byte Alignment;
}