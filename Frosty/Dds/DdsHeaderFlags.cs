using System;

namespace Frosty.Dds;

[Flags]
public enum DdsHeaderFlags : uint
{
    Caps = 1u,
    Height = 2u,
    Width = 4u,
    Pitch = 8u,
    PixelFormat = 0x1000u,
    MipMapCount = 0x20000u,
    LinearSize = 0x80000u,
    Depth = 0x800000u,
    Texture = 0x1007u
}