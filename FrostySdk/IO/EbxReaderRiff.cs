using System.IO;

namespace Frosty.Sdk.IO;

public class EbxReaderRiff : EbxReader
{
    public EbxReaderRiff(DataStream inStream)
        : base(inStream)
    {
    }
}