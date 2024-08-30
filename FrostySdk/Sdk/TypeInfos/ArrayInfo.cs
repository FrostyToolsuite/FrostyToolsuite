﻿using System.Collections.Generic;
using System.Text;
using Frosty.Sdk.IO;
using Frosty.Sdk.Sdk.TypeInfoDatas;

namespace Frosty.Sdk.Sdk.TypeInfos;

internal class ArrayInfo : TypeInfo
{
    public static Dictionary<long, long>? Mapping;

    public ArrayInfo(ArrayInfoData data)
        : base(data)
    {
    }

    public TypeInfo GetTypeInfo() => (m_data as ArrayInfoData)!.GetTypeInfo();

    public override void Read(MemoryReader reader)
    {
        if (Version < 3)
        {
            long ptr = (m_data as ArrayInfoData)!.GetTypeInfoPtr();
            if (ptr != 0)
            {
                Mapping!.Add(ptr, p_this);
            }
        }
        base.Read(reader);
    }

    public override string ReadDefaultValue(MemoryReader reader)
    {
        long p = reader.ReadLong();
        reader.Position = p - 4;
        int count = reader.ReadInt();
        if (count != 0)
        {
            FrostyLogger.Logger?.LogInfo("Default value for array not an empty array");
        }
        return "new()";
    }

    public new void CreateType(StringBuilder sb)
    {
        m_data.CreateType(sb);
    }
}