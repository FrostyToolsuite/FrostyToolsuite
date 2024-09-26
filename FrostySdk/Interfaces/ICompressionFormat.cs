using Frosty.Sdk.IO.Compression;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.Interfaces;

public interface ICompressionFormat
{
    public string Identifier { get; }

    /// <summary>
    /// Decompresses <see cref="inData"/> and writes it to <see cref="outData"/>.
    /// </summary>
    public void Decompress<T>(Block<T> inData, ref Block<T> outData, CompressionFlags inFlags = CompressionFlags.None) where T : unmanaged;

    /// <summary>
    /// Compresses <see cref="inData"/> and writes it to <see cref="outData"/>.
    /// </summary>
    public int Compress<T>(Block<T> inData, ref Block<T> outData, CompressionFlags inFlags = CompressionFlags.None) where T : unmanaged;

    public int GetCompressBounds(int inRawSize, CompressionFlags inFlags = CompressionFlags.None);
}