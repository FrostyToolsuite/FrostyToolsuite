using System;
using System.Linq;
using Frosty.Sdk.IO;
using Frosty.Sdk.Sdk.TypeInfoDatas;

namespace Frosty.Sdk.Sdk.TypeInfos;

internal class StructInfo : TypeInfo
{
    public StructInfo(StructInfoData data)
        : base(data)
    {
    }

    public void ReadDefaultValues(MemoryReader reader)
    {
        (m_data as StructInfoData)?.ReadDefaultValues(reader);
    }

    public override string ReadDefaultValue(MemoryReader reader)
    {
        return (m_data as StructInfoData)?.ReadDefaultValue(reader) ?? string.Empty;
    }
}

