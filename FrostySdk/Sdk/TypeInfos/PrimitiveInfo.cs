using Frosty.Sdk.IO;
using Frosty.Sdk.Sdk.TypeInfoDatas;

namespace Frosty.Sdk.Sdk.TypeInfos;

internal class PrimitiveInfo : TypeInfo
{
    public PrimitiveInfo(PrimitiveInfoData data)
        : base(data)
    {
    }

    public override string ReadDefaultValue(MemoryReader reader)
    {
        return (m_data as PrimitiveInfoData)?.ReadDefaultValue(reader) ?? string.Empty;
    }
}