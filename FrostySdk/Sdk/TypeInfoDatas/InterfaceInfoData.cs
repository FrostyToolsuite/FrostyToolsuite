using System.Text;
using Frosty.Sdk.IO;

namespace Frosty.Sdk.Sdk.TypeInfoDatas;

internal class InterfaceInfoData : TypeInfoData
{
    public override void Read(MemoryReader reader)
    {
        base.Read(reader);

        if (ProfilesLibrary.HasStrippedTypeNames && string.IsNullOrEmpty(m_name))
        {
            m_name = $"Interface_{m_nameHash:x8}";
        }
    }

    public override void CreateType(StringBuilder sb)
    {
        base.CreateType(sb);

        sb.Append($"public interface {CleanUpName()} {{}}");
    }
}