using System.Collections.Generic;
using System.Text;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.IO;
using Frosty.Sdk.Sdk.TypeInfos;

namespace Frosty.Sdk.Sdk.TypeInfoDatas;

internal class FunctionInfoData : TypeInfoData
{
    private enum Modifier
    {
        None,
        Virtual,
        Override
    }

    private List<ParameterInfo> m_parameterInfos = new();
    private Modifier m_modifier;

    public override void Read(MemoryReader reader)
    {
        base.Read(reader);

        if (ProfilesLibrary.HasStrippedTypeNames && string.IsNullOrEmpty(m_name))
        {
            m_name = $"Function_{m_nameHash:x8}";
        }

        reader.ReadPtr();
        reader.ReadPtr();
        reader.ReadPtr();
        if (TypeInfo.Version > 3)
        {
            reader.ReadPtr();
            reader.ReadPtr();
        }

        long pParameterInfos = reader.ReadPtr();
        m_modifier = (Modifier)reader.ReadInt();

        reader.Position = pParameterInfos;
        for (int i = 0; i < m_fieldCount; i++)
        {
            m_parameterInfos.Add(new ParameterInfo());
            m_parameterInfos[i].Read(reader);
        }
    }

    public override void CreateType(StringBuilder sb)
    {
        base.CreateType(sb);

        StringBuilder argumentTypes = new();

        foreach (ParameterInfo parameterInfo in m_parameterInfos)
        {
            TypeInfo type = parameterInfo.GetTypeInfo();

            string typeName = type.GetName();

            if (type is ArrayInfo array)
            {
                typeName = $"ObservableCollection<{array.GetTypeInfo().GetName()}>";
            }

            switch (parameterInfo.GetParameterType())
            {
                case 0:
                case 1:
                    argumentTypes.Append($", \"{typeName}\"");
                    break;
                case 2:
                case 3:
                    argumentTypes.Append($", \"{typeName}*\"");
                    break;
            }
        }

        string arguments = argumentTypes.ToString();
        if (arguments.Length > 0)
        {
            arguments = arguments.Remove(0, 2);
        }

        sb.AppendLine($"[{nameof(FunctionAttribute)}({arguments})]");
        sb.AppendLine($"public readonly struct {CleanUpName()}");
        sb.AppendLine("{}");
    }
}