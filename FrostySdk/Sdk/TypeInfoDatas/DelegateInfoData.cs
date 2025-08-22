using System.Collections.Generic;
using System.Text;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Sdk.TypeInfos;

namespace Frosty.Sdk.Sdk.TypeInfoDatas;

internal class DelegateInfoData : TypeInfoData
{
    private List<ParameterInfo> m_parameterInfos = new();

    public override void Read(MemoryReader reader)
    {
        base.Read(reader);

        if (ProfilesLibrary.HasStrippedTypeNames && string.IsNullOrEmpty(m_name))
        {
            m_name = $"Delegate_{m_nameHash:x8}";
        }

        long pParameterInfos = reader.ReadLong();

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

        string cleanName = CleanUpName();
        sb.AppendLine($$"""
                        [{{nameof(FunctionAttribute)}}({{arguments}})]
                        public struct {{cleanName}} : {{nameof(IDelegate)}}, IEquatable<{{cleanName}}>
                        {
                            public IType? {{nameof(IDelegate.FunctionType)}} { get; set; }

                            public override bool Equals(object? obj)
                            {
                                if (obj is not {{cleanName}} b)
                                {
                                    return false;
                                }

                                return Equals(b);
                            }

                            public bool Equals({{cleanName}} b)
                            {
                                return {{nameof(IDelegate.FunctionType)}} == b.{{nameof(IDelegate.FunctionType)}};
                            }

                            public static bool operator ==({{cleanName}} a, object b) => a.Equals(b);

                            public static bool operator !=({{cleanName}} a, object b) => !a.Equals(b);

                            public override int GetHashCode()
                            {
                                return {{nameof(IDelegate.FunctionType)}}?.GetHashCode() ?? 0;
                            }
                        }
                        """);
    }
}
