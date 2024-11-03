namespace Frosty.Dds;

public struct DdsHeader
{
    public const uint DdsHeaderMagic = 542327876u;

    public const uint DdsHeaderSize = 124u;

    public uint Magic;

    public uint Size;

    public DdsHeaderFlags Flags;

    public uint Height;

    public uint Width;

    public uint PitchOrLinearSize;

    public uint Depth;

    public uint MipMapCount;

    public unsafe fixed uint Reserved1[11];

    public DdsPixelFormat PixelFormat;

    public DdsCaps Caps;

    public DdsCaps2 Caps2;

    public uint Caps3;

    public uint Caps4;

    public readonly uint Reserved2;
}