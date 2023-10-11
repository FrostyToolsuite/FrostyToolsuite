using Frosty.Sdk.IO;

namespace Frosty.Sdk.Managers.Loaders;

public static class Helper
{
    public static long GetSize(DataStream stream, long originalSize)
    {
        long size = 0;
        while (originalSize > 0)
        {
            ReadBlock(stream, ref originalSize, ref size);
        }

        return size;
    }
    
    private static void ReadBlock(DataStream stream, ref long originalSize, ref long size)
    {
        ulong packed = stream.ReadUInt64(Endian.Big);

        int decompressedSize = (int)((packed >> 32) & 0x00FFFFFF);
        byte compressionType = (byte)((packed >> 24) & 0x7F);
        int bufferSize = (int)(packed & 0x000FFFFF);

        originalSize -= decompressedSize;

        if (compressionType == 0)
        {
            bufferSize = decompressedSize;
        }

        size += bufferSize + 8;
        stream.Position += bufferSize;
    }
}