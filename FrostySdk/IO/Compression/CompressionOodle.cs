using System;
using System.Runtime.InteropServices;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO.Compression;

public partial class CompressionOodle : ICompressionFormat
{
    public string Identifier => "Oodle";
    private const string NativeLibName = "ThirdParty/oo2core";

    internal enum OodleLZ_FuzzSafe
    {
        No = 0,
        Yes = 1
    }

    internal enum OodleLZ_CheckCRC
    {
        No = 0,
        Yes = 1,
        Force32 = 0x40000000
    }

    internal enum OodleLZ_Verbosity
    {
        None = 0,
        Minimal = 1,
        Some = 2,
        Lots = 3,
        Force32 = 0x40000000
    }

    internal enum OodleLZ_Decode_ThreadPhase
    {
        ThreadPhase1 = 1,
        ThreadPhase2 = 2,
        ThreadPhaseAll = 3,
        Unthreaded = ThreadPhaseAll
    }

    [LibraryImport(NativeLibName)]
    internal static partial nuint OodleLZ_Decompress(nuint compBuf, nuint compBufSize, nuint rawBuf, nuint rawLen,
        OodleLZ_FuzzSafe fuzzSafe = OodleLZ_FuzzSafe.Yes, OodleLZ_CheckCRC checkCRC = OodleLZ_CheckCRC.No,
        OodleLZ_Verbosity verbosity = OodleLZ_Verbosity.None, IntPtr decBufBase = 0, nuint decBufSize = 0,
        nuint fpCallback = 0, nuint callbackUserData = 0, nuint decoderMemory = 0, nuint decoderMemorySize = 0,
        OodleLZ_Decode_ThreadPhase threadPhase = OodleLZ_Decode_ThreadPhase.Unthreaded);

    public unsafe void Decompress<T>(Block<T> inData, ref Block<T> outData, CompressionFlags inFlags = CompressionFlags.None) where T : unmanaged
    {
        nuint retCode = OodleLZ_Decompress((nuint)inData.Ptr, (nuint)inData.Size, (nuint)outData.Ptr, (nuint)outData.Size);
        if (retCode == 0)
        {
            throw new Exception($"An Oodle operation failed.");
        }
    }

    public void Compress<T>(Block<T> inData, ref Block<T> outData, CompressionFlags inFlags = CompressionFlags.None) where T : unmanaged
    {
        throw new System.NotImplementedException();
    }
}