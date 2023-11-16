using System;
using System.Runtime.InteropServices;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO.Compression;

public partial class CompressionLZ4 : ICompressionFormat
{
    public string Identifier => "LZ4";
    private const string NativeLibName = "ThirdParty/liblz4";

    [LibraryImport(NativeLibName)]
    internal static partial int LZ4_compress_default(nuint src, nuint dst, int srcSize, int dstCapacity);

    [LibraryImport(NativeLibName)]
    internal static partial int LZ4_compressBound(int inputSize);

    [LibraryImport(NativeLibName)]
    internal static partial int LZ4_decompress_safe(nuint src, nuint dst, int compressedSize, int dstCapacity);

    public unsafe void Decompress<T>(Block<T> inData, ref Block<T> outData, CompressionFlags inFlags = CompressionFlags.None) where T : unmanaged
    {
        int err = LZ4_decompress_safe((nuint)inData.Ptr, (nuint)outData.Ptr, inData.Size, outData.Size);
        Error(err);
    }

    public unsafe void Compress<T>(Block<T> inData, ref Block<T> outData, CompressionFlags inFlags = CompressionFlags.None) where T : unmanaged
    {
        int err = LZ4_compress_default((nuint)inData.Ptr, (nuint)outData.Ptr, inData.Size, outData.Size);
        Error(err);
    }

    public void Error(int code)
    {
        if (code != 0)
        {
            return;
        }

        throw new Exception("LZ4 failed to compress/decompress.");
    }
}