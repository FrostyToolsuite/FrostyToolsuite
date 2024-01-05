using Frosty.Sdk.IO.Compression;
using System;
using System.Diagnostics;
using System.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Profiles;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO;

public static class Cas
{
    public static Block<byte> DecompressData(DataStream inStream, int inOriginalSize)
    {
        Block<byte> outBuffer = new(inOriginalSize);
        while (inStream.Position < inStream.Length)
        {
            ReadBlock(inStream, outBuffer);
        }

        outBuffer.ResetShift();
        return outBuffer;
    }

    public static Block<byte> DecompressData(DataStream inDeltaStream, DataStream inBaseStream, int inOriginalSize)
    {
        Block<byte> outBuffer = new(inOriginalSize);
        while (inDeltaStream.Position < inDeltaStream.Length)
        {
            uint packed = inDeltaStream.ReadUInt32(Endian.Big);
            int instructionType = (int)(packed & 0xF0000000) >> 28;
            int instructionSize = (int)(packed & 0x0FFFFFFF);

            switch (instructionType)
            {
                case 0:
                {
                    // read base blocks
                    while (instructionSize-- > 0)
                    {
                        ReadBlock(inBaseStream, outBuffer);
                    }
                    break;
                }
                case 1:
                {
                    // make large fixes in base block
                    using (Block<byte> toPatch = ReadBlock(inBaseStream))
                    {
                        while (instructionSize-- > 0)
                        {
                            ushort offset = inDeltaStream.ReadUInt16(Endian.Big);
                            ushort skipCount = inDeltaStream.ReadUInt16(Endian.Big);

                            // use base
                            int baseSize = offset - toPatch.ShiftAmount;
                            if (baseSize > 0)
                            {
                                toPatch.CopyTo(outBuffer, baseSize);
                                toPatch.Shift(baseSize);
                                outBuffer.Shift(baseSize);
                            }

                            // use delta
                            ReadBlock(inDeltaStream, outBuffer);

                            // skip base
                            toPatch.Shift(skipCount);
                        }

                        // fill rest with base block
                        if (toPatch.Size > 0)
                        {
                            toPatch.CopyTo(outBuffer, toPatch.Size);
                            toPatch.Shift(toPatch.Size);
                            outBuffer.Shift(toPatch.Size);
                        }
                    }

                    break;
                }
                case 2:
                {
                    // make small fixes in base block
                    int newBlockSize = inDeltaStream.ReadUInt16(Endian.Big) + 1;
                    int currentOffset = outBuffer.ShiftAmount;

                    long curPos = inDeltaStream.Position;

                    // read base block
                    using (Block<byte> toPatch = ReadBlock(inBaseStream))
                    {
                        while (inDeltaStream.Position < curPos + instructionSize)
                        {
                            ushort offset = inDeltaStream.ReadUInt16(Endian.Big);
                            byte skipCount = inDeltaStream.ReadByte();
                            byte addCount = inDeltaStream.ReadByte();

                            // use base
                            int baseSize = offset - toPatch.ShiftAmount;
                            if (baseSize > 0)
                            {
                                toPatch.CopyTo(outBuffer, baseSize);
                                toPatch.Shift(baseSize);
                                outBuffer.Shift(baseSize);
                            }

                            // skip base
                            toPatch.Shift(skipCount);

                            // add delta
                            inDeltaStream.ReadExactly(outBuffer.ToSpan(0, addCount));
                            outBuffer.Shift(addCount);
                        }

                        // fill rest with base block
                        int restSize = newBlockSize - (outBuffer.ShiftAmount - currentOffset);
                        if (restSize > 0)
                        {
                            toPatch.CopyTo(outBuffer, restSize);
                            toPatch.Shift(restSize);
                            outBuffer.Shift(restSize);
                        }

                        Debug.Assert(toPatch.Size == 0, "Didnt read base block completely");
                    }

                    Debug.Assert(outBuffer.ShiftAmount - currentOffset == newBlockSize, "Fuck");

                    break;
                }
                case 3:
                {
                    // read delta blocks
                    while (instructionSize-- > 0)
                    {
                        ReadBlock(inDeltaStream, outBuffer);
                    }
                    break;
                }
                case 4:
                {
                    // skip base blocks
                    while (instructionSize-- > 0)
                    {
                        ReadBlock(inBaseStream, null);
                    }
                    break;
                }
                default:
                    throw new InvalidDataException("block type");
            }
        }

        outBuffer.ResetShift();

        return outBuffer;
    }

    public static long GetUncompressedSize(DataStream inStream)
    {
        ulong packed = inStream.ReadUInt64(Endian.Big);

        if (packed == 0)
        {
            return 0;
        }

        int decompressedSize = (int)((packed >> 32) & 0x00FFFFFF);
        CompressionType compressionType = (CompressionType)(packed >> 24);
        Debug.Assert(((packed >> 20) & 0xF) == 7, "Invalid cas data");
        int bufferSize = (int)(packed & 0x000FFFFF);

        if ((compressionType & ~CompressionType.Obfuscated) == CompressionType.None)
        {
            bufferSize = decompressedSize;
        }

        inStream.Position += bufferSize;

        return decompressedSize;
    }

    public static long GetCompressedSize(DataStream inStream, long inUncompressedSize)
    {
        long compressedSize = 0;
        while (inStream.Position < inStream.Length)
        {
            ulong packed = inStream.ReadUInt64(Endian.Big);
            int decompressedSize = (int)((packed >> 32) & 0x00FFFFFF);
            CompressionType compressionType = (CompressionType)(packed >> 24);
            Debug.Assert(((packed >> 20) & 0xF) == 7, "Invalid cas data");
            int bufferSize = (int)(packed & 0x000FFFFF);

            if ((compressionType & ~CompressionType.Obfuscated) == CompressionType.None)
            {
                bufferSize = decompressedSize;
            }

            compressedSize += bufferSize + sizeof(ulong);

            inStream.Position += bufferSize;

            inUncompressedSize -= decompressedSize;
            if (inUncompressedSize <= 0)
            {
                break;
            }
        }

        return compressedSize;
    }

    private static unsafe void ReadBlock(DataStream inStream, Block<byte>? outBuffer)
    {
        ulong packed = inStream.ReadUInt64(Endian.Big);

        if (packed == 0)
        {
            // empty block just return, no point in parsing the header
            return;
        }

        byte flags = (byte)(packed >> 56);
        int decompressedSize = (int)((packed >> 32) & 0x00FFFFFF);
        CompressionType compressionType = (CompressionType)(packed >> 24);
        Debug.Assert(((packed >> 20) & 0xF) == 7, "Invalid cas data");
        int bufferSize = (int)(packed & 0x000FFFFF);

        if ((compressionType & ~CompressionType.Obfuscated) == CompressionType.None)
        {
            bufferSize = decompressedSize;
        }

        Block<byte> compressedBuffer;
        if ((compressionType & ~CompressionType.Obfuscated) == CompressionType.None && outBuffer is not null)
        {
            compressedBuffer = new Block<byte>(outBuffer.Ptr, bufferSize);
            compressedBuffer.MarkMemoryAsFragile();
        }
        else
        {
            compressedBuffer = new Block<byte>(bufferSize);
        }

        inStream.ReadExactly(compressedBuffer);
        if (outBuffer is not null)
        {
            Decompress(compressedBuffer, compressionType, flags, bufferSize, outBuffer);
        }

        compressedBuffer.Dispose();
        outBuffer?.Shift(decompressedSize);
    }

    private static Block<byte> ReadBlock(DataStream inStream)
    {
        ulong packed = inStream.ReadUInt64(Endian.Big);

        byte flags = (byte)(packed >> 56);
        int decompressedSize = (int)((packed >> 32) & 0x00FFFFFF);
        CompressionType compressionType = (CompressionType)(packed >> 24);
        Debug.Assert(((packed >> 20) & 0xF) == 7, "Invalid cas data");
        int bufferSize = (int)(packed & 0x000FFFFF);

        Block<byte> outBuffer = new(decompressedSize);

        if ((compressionType & ~CompressionType.Obfuscated) == CompressionType.None)
        {
            bufferSize = decompressedSize;
        }

        Block<byte> compressedBuffer;
        if ((compressionType & ~CompressionType.Obfuscated) == CompressionType.None)
        {
            compressedBuffer = outBuffer;
        }
        else
        {
            compressedBuffer = new Block<byte>(bufferSize);
        }

        inStream.ReadExactly(compressedBuffer);
        Decompress(compressedBuffer, compressionType, flags, bufferSize, outBuffer);

        // dispose compressed buffer, unless there wasn't a compressed buffer
        if ((compressionType & ~CompressionType.Obfuscated) != CompressionType.None)
        {
            compressedBuffer.Dispose();
        }

        return outBuffer;
    }

    private static void Decompress(Block<byte> inCompressedBuffer, CompressionType inCompressionType, byte inFlags,
        int inBufferSize, Block<byte> outBuffer)
    {
        if (inCompressionType.HasFlag(CompressionType.Obfuscated))
        {
            // this probably only exist in FIFA 19
            // should be fine to check for it here, since i doubt they will ever have 128 different compression types
            // currently they are at 25
            if (!ProfilesLibrary.IsLoaded(ProfileVersion.Fifa19))
            {
                throw new Exception("obfuscation");
            }

            byte[] key = KeyManager.GetKey("CasObfuscationKey");
            for (int i = 0; i < inBufferSize; i++)
            {
                inCompressedBuffer[i] ^= key[i & 0x3FFF];
            }
        }

        switch (inCompressionType & ~CompressionType.Obfuscated)
        {
            case CompressionType.None:
                // we already read the data into the outBuffer so nothing to do
                break;
            case CompressionType.ZLib:
                CompressionZLib zlib = new();
                zlib.Decompress(inCompressedBuffer, ref outBuffer);
                break;
            case CompressionType.ZStd:
                CompressionZStd zstd = new();
                zstd.Decompress(inCompressedBuffer, ref outBuffer, inFlags != 0 ? CompressionFlags.ZStdUseDicts : CompressionFlags.None);
                break;
            case CompressionType.LZ4:
                CompressionLZ4 lz4 = new();
                lz4.Decompress(inCompressedBuffer, ref outBuffer);
                break;
            case CompressionType.OodleKraken:
            {
                CompressionOodle oodle = new();
                oodle.Decompress(inCompressedBuffer, ref outBuffer, CompressionFlags.OodleKraken);
                break;
            }
            case CompressionType.OodleSelkie:
            {
                CompressionOodle oodle = new();
                oodle.Decompress(inCompressedBuffer, ref outBuffer, CompressionFlags.OodleSelkie);
                break;
            }
            case CompressionType.OodleLeviathan:
            {
                CompressionOodle oodle = new();
                oodle.Decompress(inCompressedBuffer, ref outBuffer, CompressionFlags.OodleLeviathan);
                break;
            }
            default:
                throw new NotImplementedException($"Compression type: {inCompressionType}");
        }
    }
}