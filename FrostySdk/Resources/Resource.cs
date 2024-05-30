using System;
using Frosty.Sdk.IO;

namespace Frosty.Sdk.Resources;

public abstract class Resource
{
    public abstract void Deserialize(DataStream inStream, ReadOnlySpan<byte> inResMeta);

    public abstract void Serialize(DataStream stream, Span<byte> resMeta);
}