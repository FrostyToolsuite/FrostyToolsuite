using System;

namespace Frosty.Dds;

[Flags]
public enum DdsPixelFormatFlags : uint
{
    AlphaPixels = 1u,
    Alpha = 2u,
    FourCc = 4u,
    Rgb = 0x40u,
    Yuv = 0x200u,
    Luminance = 0x20000u
}