using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO;

public unsafe class DataStream : IDisposable
{
    /// <inheritdoc cref="Stream.Position"/>
    public long Position
    {
        get => m_stream.Position;
        set => m_stream.Position = value;
    }

    /// <inheritdoc cref="Stream.Length"/>
    public long Length => m_stream.Length;

    protected Stream m_stream;
    private readonly StringBuilder m_stringBuilder;
    private readonly Stack<long> m_steps = new();

    protected DataStream()
    {
        m_stream = Stream.Null;
        m_stringBuilder = new StringBuilder();
    }

    public DataStream(Stream inStream)
    {
        m_stream = inStream;
        m_stringBuilder = new StringBuilder();
    }

    /// <inheritdoc cref="Stream.Seek"/>
    public long Seek(long offset, SeekOrigin origin) => m_stream.Seek(offset, origin);

    /// <inheritdoc cref="Stream.CopyTo(Stream)"/>
    public void CopyTo(Stream destination) => CopyTo(destination, (int)(Length - Position));
    /// <inheritdoc cref="Stream.CopyTo(Stream, int)"/>
    public virtual void CopyTo(Stream destination, int bufferSize) => m_stream.CopyTo(destination, bufferSize);

    public void CopyTo(DataStream destination) => CopyTo(destination, (int)(Length - Position));
    public virtual void CopyTo(DataStream destination, int bufferSize) => CopyTo((Stream)destination, bufferSize);

    /// <inheritdoc cref="Stream.SetLength"/>
    public void SetLength(int value) => m_stream.SetLength(value);

    #region -- Read --

    /// <summary>
    /// Reads count number of bytes from the current stream and advances the position within the stream.
    /// </summary>
    /// <param name="count">The number of bytes to be read from the current stream.</param>
    /// <returns>A byte array containing the read bytes.</returns>
    /// <exception cref="EndOfStreamException">The end of the stream is reached before reading count number of bytes.</exception>
    public byte[] ReadBytes(int count)
    {
        byte[] retVal = new byte[count];
        m_stream.ReadExactly(retVal, 0, retVal.Length);
        return retVal;
    }

    /// <inheritdoc cref="Stream.ReadExactly(Span{byte})"/>
    public void ReadExactly(Span<byte> buffer) => m_stream.ReadExactly(buffer);

    /// <inheritdoc cref="Stream.Read(byte[], int, int)"/>
    public int Read(byte[] buffer, int offset, int count) => m_stream.Read(buffer, offset, count);

    #region -- Basic Types --

    public byte ReadByte()
    {
        int retVal = m_stream.ReadByte();
        if (retVal == -1)
        {
            throw new EndOfStreamException();
        }

        return (byte)retVal;
    }

    public sbyte ReadSByte()
    {
        return (sbyte)ReadByte();
    }

    public bool ReadBoolean()
    {
        return ReadByte() != 0;
    }

    public char ReadChar()
    {
        return (char)ReadByte();
    }

    public short ReadInt16(Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[sizeof(short)];
        m_stream.ReadExactly(buffer);

        if (endian == Endian.Big)
        {
            return BinaryPrimitives.ReadInt16BigEndian(buffer);
        }

        return BinaryPrimitives.ReadInt16LittleEndian(buffer);
    }

    public ushort ReadUInt16(Endian endian = Endian.Little)
    {
        return (ushort)ReadInt16(endian);
    }

    public int ReadInt32(Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        m_stream.ReadExactly(buffer);

        if (endian == Endian.Big)
        {
            return BinaryPrimitives.ReadInt32BigEndian(buffer);
        }

        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    public uint ReadUInt32(Endian endian = Endian.Little)
    {
        return (uint)ReadInt32(endian);
    }

    public long ReadInt64(Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        m_stream.ReadExactly(buffer);

        if (endian == Endian.Big)
        {
            return BinaryPrimitives.ReadInt64BigEndian(buffer);
        }

        return BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    public ulong ReadUInt64(Endian endian = Endian.Little)
    {
        return (ulong)ReadInt64(endian);
    }

    public Half ReadHalf(Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[sizeof(Half)];
        m_stream.ReadExactly(buffer);

        if (endian == Endian.Big)
        {
            return BinaryPrimitives.ReadHalfBigEndian(buffer);
        }

        return BinaryPrimitives.ReadHalfLittleEndian(buffer);
    }

    public float ReadSingle(Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[sizeof(float)];
        m_stream.ReadExactly(buffer);

        if (endian == Endian.Big)
        {
            return BinaryPrimitives.ReadSingleBigEndian(buffer);
        }

        return BinaryPrimitives.ReadSingleLittleEndian(buffer);
    }

    public double ReadDouble(Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[sizeof(double)];
        m_stream.ReadExactly(buffer);

        if (endian == Endian.Big)
        {
            return BinaryPrimitives.ReadDoubleBigEndian(buffer);
        }

        return BinaryPrimitives.ReadDoubleLittleEndian(buffer);
    }

    #endregion

    #region -- Strings --

    public virtual string ReadNullTerminatedString()
    {
        m_stringBuilder.Clear();
        while (true)
        {
            char c = ReadChar();
            if (c == 0)
            {
                return m_stringBuilder.ToString();
            }

            m_stringBuilder.Append(c);
        }
    }

    public string ReadFixedSizedString(int size)
    {
        using (Block<byte> buffer = new(size))
        {
            m_stream.ReadExactly(buffer);
            return Encoding.ASCII.GetString(buffer).TrimEnd((char)0);
        }
    }

    public string ReadSizedString()
    {
        return ReadFixedSizedString(Read7BitEncodedInt32());
    }

    #endregion

    public int Read7BitEncodedInt32()
    {
        uint num1 = 0;
        for (int index = 0; index < 28; index += 7)
        {
            byte num2 = ReadByte();
            num1 |= (uint) ((num2 & sbyte.MaxValue) << index);
            if (num2 <= 127)
            {
                return (int) num1;
            }
        }
        byte num3 = ReadByte();
        if (num3 > 15)
        {
            throw new FormatException();
        }

        return (int) (num1 | (uint) num3 << 28);
    }

    public long Read7BitEncodedInt64()
    {
        ulong num1 = 0;
        for (int index = 0; index < 63; index += 7)
        {
            byte num2 = ReadByte();
            num1 |= (ulong) (((long) num2 & sbyte.MaxValue) << index);
            if (num2 <= 127)
            {
                return (long) num1;
            }
        }
        byte num3 = ReadByte();
        if (num3 > 1)
        {
            throw new FormatException();
        }

        return (long) (num1 | (ulong) num3 << 63);
    }

    public Guid ReadGuid(Endian endian = Endian.Little)
    {
        Span<byte> span = stackalloc byte[sizeof(Guid)];
        m_stream.ReadExactly(span);

        if (endian == Endian.Big)
        {
            return new Guid(BinaryPrimitives.ReadInt32BigEndian(span),
                BinaryPrimitives.ReadInt16BigEndian(span[4..]), BinaryPrimitives.ReadInt16BigEndian(span[6..]),
                span[8..].ToArray());
        }

        return new Guid(span);
    }

    public Sha1 ReadSha1()
    {
        Span<byte> span = stackalloc byte[sizeof(Sha1)];
        m_stream.ReadExactly(span);

        return new Sha1(span);
    }

    #endregion

    #region -- Write --

    public virtual void Write(ReadOnlySpan<byte> buffer) => m_stream.Write(buffer);

    public virtual void Write(byte[] buffer, int offset, int count) => m_stream.Write(buffer, offset, count);

    #region -- Basic Types --

    public virtual void WriteByte(byte value) => m_stream.WriteByte(value);

    public void WriteSByte(sbyte value)
    {
        WriteByte((byte)value);
    }

    public void WriteBoolean(bool value)
    {
        WriteByte((byte)(value ? 1 : 0));
    }

    public void WriteChar(char value)
    {
        WriteByte((byte)value);
    }

    public void WriteInt16(short value, Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[sizeof(short)];

        if (endian == Endian.Big)
        {
            BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        }

        Write(buffer);
    }

    public void WriteUInt16(ushort value, Endian endian = Endian.Little)
    {
        WriteInt16((short)value, endian);
    }

    public void WriteInt32(int value, Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];

        if (endian == Endian.Big)
        {
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        }

        Write(buffer);
    }

    public void WriteUInt32(uint value, Endian endian = Endian.Little)
    {
        WriteInt32((int)value, endian);
    }

    public void WriteInt64(long value, Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];

        if (endian == Endian.Big)
        {
            BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        }

        Write(buffer);
    }

    public void WriteUInt64(ulong value, Endian endian = Endian.Little)
    {
        WriteInt64((long)value, endian);
    }

    public void WriteHalf(Half value, Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[sizeof(Half)];

        if (endian == Endian.Big)
        {
            BinaryPrimitives.WriteHalfBigEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteHalfLittleEndian(buffer, value);
        }

        Write(buffer);
    }

    public void WriteSingle(float value, Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[sizeof(float)];

        if (endian == Endian.Big)
        {
            BinaryPrimitives.WriteSingleBigEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        }

        Write(buffer);
    }

    public void WriteDouble(double value, Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[sizeof(double)];

        if (endian == Endian.Big)
        {
            BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        }

        Write(buffer);
    }

    #endregion

    #region -- Strings --

    public void WriteNullTerminatedString(string value)
    {
        using (Block<byte> buffer = new(value.Length + 1))
        {
            Encoding.ASCII.GetBytes(value, buffer);
            buffer[value.Length] = 0;
            Write(buffer);
        }
    }

    public void WriteFixedSizedString(string value, int size)
    {
        using (Block<byte> buffer = new(size))
        {
            Encoding.ASCII.GetBytes(value, buffer);
            for (int i = value.Length; i < size; i++)
            {
                buffer[i] = 0;
            }
            Write(buffer);
        }
    }

    public void WriteSizedString(string value)
    {
        int size = value.Length + 1;
        Write7BitEncodedInt32(size);
        WriteFixedSizedString(value, size);
    }

    #endregion

    public void WriteGuid(Guid value, Endian endian = Endian.Little)
    {
        Span<byte> buffer = stackalloc byte[sizeof(Guid)];

        value.TryWriteBytes(buffer);

        if (endian == Endian.Big)
        {
            Unsafe.As<byte, int>(ref buffer[0]) = BinaryPrimitives.ReverseEndianness(Unsafe.As<byte, int>(ref buffer[0]));
            Unsafe.As<byte, short>(ref buffer[4]) = BinaryPrimitives.ReverseEndianness(Unsafe.As<byte, short>(ref buffer[4]));
            Unsafe.As<byte, short>(ref buffer[6]) = BinaryPrimitives.ReverseEndianness(Unsafe.As<byte, short>(ref buffer[6]));
        }

        Write(buffer);
    }

    public void WriteSha1(Sha1 value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(Sha1)];

        value.TryWriteBytes(buffer);

        Write(buffer);
    }

    public void Write7BitEncodedInt32(int value)
    {
        uint num;
        for (num = (uint) value; num > (uint) sbyte.MaxValue; num >>= 7)
        {
            WriteByte((byte)(num | 0xFFFFFF80U));
        }

        WriteByte((byte)num);
    }

    public void Write7BitEncodedInt64(long value)
    {
        ulong num;
        for (num = (ulong) value; num > (ulong) sbyte.MaxValue; num >>= 7)
        {
            WriteByte((byte)((uint)num | 0xFFFFFF80U));
        }

        WriteByte((byte)num);
    }

    #endregion

    public void Pad(int alignment)
    {
        if (m_stream.Position % alignment != 0)
        {
            m_stream.Position += alignment - (m_stream.Position % alignment);
        }
    }

    public void StepIn(long inPosition)
    {
        m_steps.Push(Position);
        Position = inPosition;
    }

    public void StepOut()
    {
        Debug.Assert(m_steps.Count > 0, "StepOut called when there were no steps taken.");
        Position = m_steps.Pop();
    }

    public static implicit operator Stream(DataStream stream) => stream.m_stream;

    public virtual void Dispose()
    {
        m_stream.Dispose();
    }

    public virtual DataStream CreateSubStream(long inStartOffset, int inSize)
    {
        StepIn(inStartOffset);

        DataStream retVal = new(new MemoryStream(ReadBytes(inSize)));

        StepOut();

        return retVal;
    }
}