using System;

namespace Frosty.Ktx;

public struct KtxHeader
{
    public unsafe fixed byte Identifier[12];

    public uint VkFormat;

    public uint TypeSize;

    public uint PixelWidth;

    public uint PixelHeight;

    public uint PixelDepth;

    public uint LayerCount;

    public uint FaceCount;

    public uint LevelCount;

    public uint SupercompressionScheme;

    public KtxIndexEntry32 DataFormatDescriptor;

    public KtxIndexEntry32 KeyValueData;

    public KtxIndexEntry64 SupercompressionGlobalData;

    public unsafe bool VerifyHeader()
    {
        Span<byte> span = stackalloc byte[12]
        {
            171, 75, 84, 88, 32, 50, 48, 187, 13, 10,
            26, 10
        };
        for (int i = 0; i < span.Length; i++)
        {
            if (Identifier[i] != span[i])
            {
                return false;
            }
        }
        return true;
    }
}