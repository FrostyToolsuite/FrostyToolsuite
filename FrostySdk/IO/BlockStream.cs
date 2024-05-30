using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO;

public class BlockStream : DataStream
{
    private readonly Block<byte> m_block;
    private readonly bool m_leaveOpen;

    public BlockStream()
    {
        m_block = new Block<byte>(0);
        m_stream = m_block.ToStream();
    }

    public BlockStream(int inSize)
    {
        m_block = new Block<byte>(inSize);
        m_stream = m_block.ToStream();
    }

    public BlockStream(Block<byte> inBuffer, bool inLeaveOpen = false)
    {
        m_block = inBuffer;
        m_stream = m_block.ToStream();
        m_leaveOpen = inLeaveOpen;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ResizeStream(Position + buffer.Length);
        base.Write(buffer);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ResizeStream(Position + count);
        base.Write(buffer, offset, count);
    }

    public override void WriteByte(byte value)
    {
        ResizeStream(Position + sizeof(byte));
        base.WriteByte(value);
    }

    public override unsafe void CopyTo(DataStream destination, int bufferSize)
    {
        if (destination is not BlockStream stream)
        {
            base.CopyTo(destination, bufferSize);
            return;
        }

        if (bufferSize <= 0)
        {
            return;
        }

        stream.ResizeStream(stream.Position + bufferSize);

        if (stream.Length < stream.Position + bufferSize)
        {
            stream.m_stream.SetLength((int)stream.Position + bufferSize);
        }

        using (Block<byte> a = new(m_block.BasePtr + Position, bufferSize))
        using (Block<byte> b = new(stream.m_block.BasePtr + stream.Position, bufferSize))
        {
            a.MarkMemoryAsFragile();
            b.MarkMemoryAsFragile();
            a.CopyTo(b);
        }

        stream.Position += bufferSize;

        Position += bufferSize;
    }

    public override unsafe string ReadNullTerminatedString()
    {
        string retVal = new((sbyte*)(m_block.Ptr + Position));
        Position += retVal.Length + 1;
        return retVal;
    }

    public override unsafe DataStream CreateSubStream(long inStartOffset, int inSize)
    {
        Block<byte> sub = new(m_block.BasePtr + inStartOffset, inSize);
        sub.MarkMemoryAsFragile();
        return new BlockStream(sub);
    }

    /// <summary>
    /// Loads whole file into memory and deobfuscates it if necessary.
    /// </summary>
    /// <param name="inPath">The path of the file</param>
    /// <param name="inShouldDeobfuscate">The boolean if the file needs to be deobfuscated</param>
    /// <returns>A <see cref="BlockStream"/> that has the file loaded.</returns>
    public static BlockStream FromFile(string inPath, bool inShouldDeobfuscate)
    {
        using (FileStream stream = new(inPath, FileMode.Open, FileAccess.Read))
        {
            BlockStream? retVal;
            if (inShouldDeobfuscate)
            {
                if (CheckExtraObfuscation(stream, out int keySize))
                {
                    int size = (int)(stream.Length - keySize);
                    using (Block<byte> data = new(size))
                    {
                        stream.ReadExactly(data);

                        // this is not how the game actually decrypts the data, but also seems to work
                        // look at https://github.com/CadeEvs/FrostyToolsuite/blob/1.0.6.2/FrostySdk/Deobfuscators/MEADeobfuscator.cs for actual decryption algorithm
                        byte initialValue = data[0];
                        byte key = data[0];
                        for (int i = 0; i < size; i++)
                        {
                            byte nextKey = (byte)(((initialValue ^ data[i]) - i % 256) & 0xFF);
                            data[i] ^= key;
                            key = nextKey;
                        }

                        Span<byte> header = data.ToSpan();
                        data.Shift(0x22c);

                        using (Stream dataStream = data.ToStream())
                        {
                            if (Deobfuscate(header, dataStream, out retVal))
                            {
                                return retVal;
                            }
                        }
                    }
                }
                else
                {
                    Span<byte> header = stackalloc byte[0x22C];
                    stream.ReadExactly(header);

                    if (Deobfuscate(header, stream, out retVal))
                    {
                        return retVal;
                    }
                }

                stream.Position = 0;
            }

            retVal = new BlockStream((int)stream.Length);
            stream.ReadExactly(retVal.m_block);
            return retVal;
        }
    }

    /// <summary>
    /// Loads part of a file into memory.
    /// </summary>
    /// <param name="inPath">The path of the file.</param>
    /// <param name="inOffset">The offset of the data to load.</param>
    /// <param name="inSize">The size of the data to load</param>
    /// <returns>A <see cref="BlockStream"/> that has the data loaded.</returns>
    public static BlockStream FromFile(string inPath, long inOffset, int inSize)
    {
        using (FileStream stream = new(inPath, FileMode.Open, FileAccess.Read))
        {
            stream.Position = inOffset;

            BlockStream retVal = new(inSize);

            stream.ReadExactly(retVal.m_block);
            return retVal;
        }
    }

    /// <summary>
    /// <see cref="Aes"/> decrypt this <see cref="BlockStream"/>.
    /// </summary>
    /// <param name="inKey">The key to use for the decryption.</param>
    /// <param name="inPaddingMode">The <see cref="PaddingMode"/> to use for the decryption.</param>
    public void Decrypt(byte[] inKey, PaddingMode inPaddingMode)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = inKey;
            Span<byte> span = m_block.ToSpan((int)Position);
            aes.DecryptCbc(span, inKey, span, inPaddingMode);
        }
    }

    /// <summary>
    /// <see cref="Aes"/> decrypt part of this <see cref="BlockStream"/>.
    /// </summary>
    /// <param name="inKey">The key to use for the decryption.</param>
    /// <param name="inSize">The size of the data to decrypt.</param>
    /// <param name="inPaddingMode">The <see cref="PaddingMode"/> to use for the decryption.</param>
    public void Decrypt(byte[] inKey, int inSize, PaddingMode inPaddingMode)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = inKey;
            Span<byte> span = m_block.ToSpan((int)Position, inSize);
            aes.DecryptCbc(span, inKey, span, inPaddingMode);
        }
    }

    public override void Dispose()
    {
        if (m_leaveOpen && Position > Length)
        {
            Span<byte> padding = new byte[Position - Length];
            Position = Length;
            m_stream.Write(padding);
        }
        if (m_leaveOpen && m_block.Size != Length)
        {
            // resize the block if needed
            m_block.Resize((int)Length);
        }

        base.Dispose();
        if (!m_leaveOpen)
        {
            m_block.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    private static bool CheckExtraObfuscation(Stream inStream, out int keySize)
    {
        keySize = 0;

        // read signature
        inStream.Seek(-36, SeekOrigin.End);
        Span<byte> signature = stackalloc byte[36];
        inStream.ReadExactly(signature);
        inStream.Position = 0;

        // check signature
        const string magic = "@e!adnXd$^!rfOsrDyIrI!xVgHeA!6Vc";
        for (int i = 0; i < 32; i++)
        {
            if (signature[i + 4] != magic[i])
            {
                return false;
            }
        }

        // get key size
        keySize = signature[3] << 24 | signature[2] << 16 | signature[1] << 8 | signature[0] << 0;
        return true;
    }

    private static bool Deobfuscate(Span<byte> inHeader, Stream inStream, [NotNullWhen(returnValue:true)] out BlockStream? stream)
    {
        if (!(inHeader[0] == 0x00 && inHeader[1] == 0xD1 && inHeader[2] == 0xCE &&
              (inHeader[3] == 0x00 || inHeader[3] == 0x01 || inHeader[3] == 0x03))) // version 0 is not used in fb3
        {
            stream = null;
            return false;
        }

        stream = new BlockStream((int)(inStream.Length - 0x22C));
        inStream.ReadExactly(stream.m_block);

        // deobfuscate the data
        IDeobfuscator? deobfuscator = FileSystemManager.CreateDeobfuscator();
        deobfuscator?.Deobfuscate(inHeader, stream.m_block);

        return true;
    }

    private unsafe void ResizeStream(long inDesiredMinLength)
    {
        if (inDesiredMinLength > m_block.Size)
        {
            long position = Position;
            int oldSize = m_block.Size;
            int neededLength = (int)Math.Max(inDesiredMinLength, Environment.SystemPageSize + position);
            neededLength = neededLength + 15 & ~15;
            m_block.Resize(neededLength);

            // make sure resized memory is 0
            uint size = (uint)(neededLength - oldSize);
            if (size > 0)
            {
                NativeMemory.Clear(m_block.BasePtr + oldSize, size);
            }

            m_stream = m_block.ToStream();
            m_stream.SetLength(inDesiredMinLength);
            m_stream.Position = position;
        }
    }
}