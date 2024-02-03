using System;
using System.IO;
using Frosty.Sdk.IO;

namespace Frosty.Sdk.Utils;

public class RiffStream
{
    public bool Eof { get; private set; }

    private readonly DataStream m_stream;

    public RiffStream(DataStream inStream)
    {
        m_stream = inStream;
    }

    public void ReadHeader(out FourCC fileFourCc, out uint size)
    {
        FourCC fourCc = m_stream.ReadUInt32();

        if (fourCc != "RIFF" && fourCc != "RIFX")
        {
            throw new FormatException("Not a valid RIFF format.");
        }

        size = m_stream.ReadUInt32();

        fileFourCc = m_stream.ReadUInt32();
    }

    public void ReadNextChunk(ref uint size, Action<DataStream, FourCC, uint> processChunkFunc)
    {
        if (Eof)
        {
            throw new EndOfStreamException();
        }

        FourCC fourCc = m_stream.ReadUInt32();

        uint subSize = m_stream.ReadUInt32();
        uint paddedSize = subSize + 1u & ~1u;

        long curPos = m_stream.Position;

        size -= paddedSize;

        processChunkFunc(m_stream, fourCc, subSize);

        m_stream.Position = curPos + paddedSize;

        if (m_stream.Position == m_stream.Length)
        {
            Eof = true;
        }
    }
}