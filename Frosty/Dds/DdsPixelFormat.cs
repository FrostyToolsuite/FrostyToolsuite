namespace Frosty.Dds;

public struct DdsPixelFormat
{
    public const uint DdsPixelFormatSize = 32u;

    public uint Size;

    public DdsPixelFormatFlags Flags;

    public DdsFourCc FourCC;

    public uint RGBBitCount;

    public uint RBitMask;

    public uint GBitMask;

    public uint BBitMask;

    public uint ABitMask;

    public static DdsPixelFormat Dx10 = new()
    {
        Size = 32u,
        Flags = DdsPixelFormatFlags.FourCc,
        FourCC = DdsFourCc.Dx10
    };
}