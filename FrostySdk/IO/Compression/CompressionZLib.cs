using Frosty.Sdk.Utils;
using System;
using System.Runtime.InteropServices;

namespace Frosty.Sdk.IO.Compression;

public partial class CompressionZLib : ICompressionFormat
{
    public string Identifier => "ZLib";
    private const string NativeLibName = "ThirdParty/libzlib";

    [LibraryImport(NativeLibName)] internal static partial int compress(nuint dest, nuint destLen, nuint source, nuint sourceLen);
    [LibraryImport(NativeLibName)] internal static partial int uncompress(nuint dst, nuint dstCapacity, nuint source, nuint compressedSize);
    [LibraryImport(NativeLibName)] internal static partial IntPtr zError(int code);

    public unsafe void Decompress<T>(Block<T> inData, ref Block<T> outData, CompressionFlags inFlags = CompressionFlags.None) where T : unmanaged
    {
        int destCapacity = outData.Size;
        int err = uncompress((nuint)outData.Ptr, (nuint)(&destCapacity), (nuint)inData.Ptr, (nuint)inData.Size);
        Error(err);
    }

    public unsafe void Compress<T>(Block<T> inData, ref Block<T> outData, CompressionFlags inFlags = CompressionFlags.None) where T : unmanaged
    {
        int err = compress((nuint)outData.Ptr, (nuint)outData.Size, (nuint)inData.Ptr, (nuint)inData.Size);
        Error(err);
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