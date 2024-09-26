using Frosty.Sdk.Utils;
using System;
using System.Runtime.InteropServices;
using Frosty.Sdk.Interfaces;

namespace Frosty.Sdk.IO.Compression;

public partial class CompressionZLib : ICompressionFormat
{
    public string Identifier => "ZLib";
    private const string NativeLibName = "ThirdParty/libzlib";

    [LibraryImport(NativeLibName)] internal static partial int compress(nuint dest, nuint destLen, nuint source, nuint sourceLen);
    [LibraryImport(NativeLibName)] internal static partial int uncompress(nuint dst, nuint dstCapacity, nuint source, nuint compressedSize);
    [LibraryImport(NativeLibName)] internal static partial nint zError(int code);
    [LibraryImport(NativeLibName)] internal static partial nuint compressBound(nuint sourceLen);

    public unsafe void Decompress<T>(Block<T> inData, ref Block<T> outData, CompressionFlags inFlags = CompressionFlags.None) where T : unmanaged
    {
        int destCapacity = outData.Size;
        int err = uncompress((nuint)outData.Ptr, (nuint)(&destCapacity), (nuint)inData.Ptr, (nuint)inData.Size);
        Error(err);
    }

    public unsafe int Compress<T>(Block<T> inData, ref Block<T> outData, CompressionFlags inFlags = CompressionFlags.None) where T : unmanaged
    {
        int destSize = outData.Size;
        int err = compress((nuint)outData.Ptr, (nuint)(&destSize), (nuint)inData.Ptr, (nuint)inData.Size);
        Error(err);
        return destSize;
    }

    public int GetCompressBounds(int inRawSize, CompressionFlags inFlags = CompressionFlags.None)
    {
        return (int)compressBound((nuint)inRawSize);
    }

    private unsafe void Error(int code)
    {
        if (code != 0)
        {
            string error = new((sbyte*)zError(code));
            throw new Exception(error);
        }
    }
}