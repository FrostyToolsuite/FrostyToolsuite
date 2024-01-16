using System;
using System.Runtime.InteropServices;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO.Compression;

public partial class CompressionZStd : ICompressionFormat
{
    public string Identifier => "ZStandard";
    private const string NativeLibName = "ThirdParty/libzstd";

    private static nuint s_DDict = nuint.Zero;

    [LibraryImport(NativeLibName)] internal static partial nuint ZSTD_getErrorName(nuint code);
    [LibraryImport(NativeLibName)] internal static partial nuint ZSTD_isError(nuint code);
    [LibraryImport(NativeLibName)] internal static partial nuint ZSTD_createDDict(nuint dict, nuint dictSize);
    [LibraryImport(NativeLibName)] internal static partial nuint ZSTD_createDCtx();
    [LibraryImport(NativeLibName)] internal static partial nuint ZSTD_freeDCtx(nuint dctx);
    [LibraryImport(NativeLibName)] internal static partial nuint ZSTD_decompress(nuint dst, nuint dstCapacity, nuint src, nuint compressedSize);
    [LibraryImport(NativeLibName)] internal static partial nuint ZSTD_decompress_usingDDict(nuint dctx, nuint dst, nuint dstCapacity, nuint src, nuint srcSize, nuint dict);
    [LibraryImport(NativeLibName)] internal static partial nuint ZSTD_compress(nuint dst, nuint dstCapacity, nuint src, nuint srcSize);

    /// <summary>
    /// Checks if the specified code is a valid ZStd error.
    /// </summary>
    private unsafe static void GetError(nuint code)
    {
        if (ZSTD_isError(code) != nuint.Zero)
        {
            string error = new((sbyte*)ZSTD_getErrorName(code));
            throw new Exception($"A ZStandard operation failed with error: \"{error}\"");
        }
    }

    /// <inheritdoc/>
    public unsafe void Decompress<T>(Block<T> inData, ref Block<T> outData, CompressionFlags inFlags = CompressionFlags.None) where T : unmanaged
    {
        nuint code;
        if (inFlags.HasFlag(CompressionFlags.ZStdUseDicts))
        {
            if (s_DDict == nuint.Zero)
            {
                using (Block<byte> ebxDict = FileSystemManager.GetFileFromMemoryFs("Dictionaries/ebx.dict"))
                {
                    s_DDict = ZSTD_createDDict((nuint)ebxDict.Ptr, (nuint)ebxDict.Size);
                }
            }

            nuint dctx = ZSTD_createDCtx();
            code = ZSTD_decompress_usingDDict(dctx, (nuint)outData.Ptr, (nuint)outData.Size, (nuint)inData.Ptr, (nuint)inData.Size, s_DDict);
            ZSTD_freeDCtx(dctx);
        }
        else
        {
            code = ZSTD_decompress((nuint)outData.Ptr, (nuint)outData.Size, (nuint)inData.Ptr, (nuint)inData.Size);
        }
        GetError(code);
    }

    /// <inheritdoc/>
    public unsafe void Compress<T>(Block<T> inData, ref Block<T> outData, CompressionFlags inFlags = CompressionFlags.None) where T : unmanaged
    {
        nuint code = ZSTD_compress((nuint)outData.Ptr, (nuint)outData.Size, (nuint)inData.Ptr, (nuint)inData.Size);
        GetError(code);
    }
}