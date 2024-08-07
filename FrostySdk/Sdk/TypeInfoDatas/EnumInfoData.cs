using System.Collections.Generic;
using System.Linq;
using System.Text;
using Frosty.Sdk.IO;

namespace Frosty.Sdk.Sdk.TypeInfoDatas;

internal class EnumInfoData : TypeInfoData
{
    private List<FieldInfo> m_fieldInfos = new();

    public override void Read(MemoryReader reader)
    {
        base.Read(reader);

        if (ProfilesLibrary.HasStrippedTypeNames && string.IsNullOrEmpty(m_name))
        {
            m_name = $"Enum_{m_nameHash:x8}";
        }

        long pFieldInfos = reader.ReadLong();

        reader.Position = pFieldInfos;
        for (int i = 0; i < m_fieldCount; i++)
        {
            m_fieldInfos.Add(new FieldInfo());
            m_fieldInfos[i].Read(reader, m_nameHash, m_name);
        }
    }

    public string ReadDefaultValue(MemoryReader reader)
    {
        int value = reader.ReadInt();
        string? strValue = m_fieldInfos.FirstOrDefault(field => field.GetEnumValue() == value)?.GetName();
        if (string.IsNullOrEmpty(strValue))
        {
            return $"({GetFullName()})({value})";
        }
        return $"{GetFullName()}.{strValue}";
    }

    public override void CreateType(StringBuilder sb)
    {
        base.CreateType(sb);

        sb.AppendLine($"public enum {CleanUpName()}");

        sb.AppendLine("{");

        foreach (FieldInfo fieldInfo in m_fieldInfos)
        {
            sb.AppendLine($"{fieldInfo.GetName()} = {fieldInfo.GetEnumValue()},");
        }

        sb.AppendLine("}");
    }
}