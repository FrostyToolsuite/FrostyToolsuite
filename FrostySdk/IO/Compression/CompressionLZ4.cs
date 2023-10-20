using System;
using System.Runtime.InteropServices;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO.Compression;

public partial class CompressionLZ4 : ICompressionFormat
{
    public string Identifier => "LZ4";
    private const string NativeLibName = "lz4";

    [LibraryImport(NativeLibName)] 
    internal static partial int LZ4_compress_default(IntPtr src, IntPtr dst, int srcSize, int dstCapacity);

    [LibraryImport(NativeLibName)]
    internal static partial int LZ4_compressBound(int inputSize);

    [LibraryImport(NativeLibName)]
    internal static partial int LZ4_decompress_safe(IntPtr src, IntPtr dst, int compressedSize, int dstCapacity);
    
    public unsafe void Decompress<T>(Block<T> inData, ref Block<T> outData, CompressionFlags inFlags = CompressionFlags.None) where T : unmanaged
    {
        int err = LZ4_decompress_safe((IntPtr)inData.Ptr, (IntPtr)outData.Ptr, inData.Size, outData.Size);
        Error(err);
    }

    public unsafe void Compress<T>(Block<T> inData, ref Block<T> outData, CompressionFlags inFlags = CompressionFlags.None) where T : unmanaged
    {
        int err = LZ4_compress_default((IntPtr)inData.Ptr, (IntPtr)outData.Ptr, inData.Size, outData.Size);
        Error(err);
    }

    public void Error(int code)
    {
        if (code != 0)
            return;
        throw new Exception("LZ4 failed to compress/decompress.");
    }
}