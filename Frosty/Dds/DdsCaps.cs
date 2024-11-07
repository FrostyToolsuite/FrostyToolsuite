using System;

namespace Frosty.Dds;

[Flags]
public enum DdsCaps : uint
{
    Complex = 8u,
    MipMap = 0x400000u,
    Texture = 0x1000u
}

[Flags]
public enum DdsCaps2 : uint
{
    CubeMap = 0x200u,
    CubeMapPositiveX = 0x400u,
    CubeMapNegativeX = 0x800u,
    CubeMapPositiveY = 0x1000u,
    CubeMapNegativeY = 0x2000u,
    CubeMapPositiveZ = 0x4000u,
    CubeMapNegativeZ = 0x8000u,
    Volume = 0x200000u,
    CubeMapAllFaces = 0xFC00u
}