using Frosty.Sdk.Attributes;
using Frosty.Sdk.IO;
using System;
using System.Collections.Generic;
using System.Text;

namespace Frosty.Sdk.Sdk.TypeInfoDatas;

internal class StructInfoData : TypeInfoData
{
    private List<FieldInfo> m_fieldInfos = new();
    private long p_defaultValue;

    public override void Read(MemoryReader reader)
    {
        base.Read(reader);

        if (ProfilesLibrary.HasStrippedTypeNames && string.IsNullOrEmpty(m_name))
        {
            m_name = $"Struct_{m_nameHash:x8}";
        }

        if (TypeInfo.Version > 2)
        {
            reader.ReadLong();
            reader.ReadLong();
            if (TypeInfo.Version > 3)
            {
                reader.ReadLong();
                reader.ReadLong();
                if (TypeInfo.Version > 4)
                {
                    reader.ReadLong();
                }
            }
        }

        p_defaultValue = reader.ReadLong();
        long pFieldInfos = reader.ReadLong();

        reader.Position = pFieldInfos;
        for (int i = 0; i < m_fieldCount; i++)
        {
            m_fieldInfos.Add(new FieldInfo());
            m_fieldInfos[i].Read(reader, m_nameHash, m_name);
        }
    }

    public void ReadDefaultValues(MemoryReader reader)
    {
        if (p_defaultValue == 0)
        {
            return;
        }

        foreach (FieldInfo fieldInfo in m_fieldInfos)
        {
            fieldInfo.ReadDefaultValue(reader, p_defaultValue);
        }
    }

    public string ReadDefaultValue(MemoryReader inReader)
    {
        StringBuilder sb = new();
        sb.AppendLine("new()\n{");

        long curPos = inReader.Position;
        foreach (FieldInfo fieldInfo in m_fieldInfos)
        {
            string defaultValue = fieldInfo.ReadValue(inReader, curPos);
            if (!string.IsNullOrEmpty(defaultValue))
            {
                sb.AppendLine($"{fieldInfo.GetName()} = {defaultValue},");
            }
        }

        sb.Append('}');

        return sb.ToString();
    }

    public override void CreateType(StringBuilder sb)
    {
        if (m_name.Contains("::"))
        {
            // nested type
            sb.AppendLine($"public partial struct {m_name[..m_name.IndexOf("::", StringComparison.Ordinal)]}");
            sb.AppendLine("{");
        }

        base.CreateType(sb);

        string name = CleanUpName();

        sb.AppendLine($"public partial struct {name}");

        sb.AppendLine("{");

        m_fieldInfos.Sort();
        for (int i = 0; i < m_fieldInfos.Count; i++)
        {
            sb.AppendLine($"[{nameof(FieldIndexAttribute)}({i})]");
            m_fieldInfos[i].CreateField(sb);
        }

        sb.AppendLine($"public {name}() {{}}");

        sb.AppendLine("}");

        if (m_name.Contains("::"))
        {
            sb.AppendLine("}");
        }
    }

    public override void UpdateName()
    {
        base.UpdateName();

        foreach (FieldInfo fieldInfo in m_fieldInfos)
        {
            fieldInfo.UpdateName(m_nameHash);
        }
    }
}