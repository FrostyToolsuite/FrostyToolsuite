using Frosty.Sdk.IO;
using Frosty.Sdk.Sdk.TypeInfoDatas;

namespace Frosty.Sdk.Sdk.TypeInfos;

internal class ClassInfo : TypeInfo
{
    public ClassInfo GetSuperClassInfo() => (TypeInfoMapping![p_superClass] as ClassInfo)!;

    private long p_superClass;
    private long p_defaultInstance;

    public ClassInfo(ClassInfoData data)
        : base(data)
    {
    }

    public override void Read(MemoryReader reader)
    {
        base.Read(reader);

        p_superClass = reader.ReadLong();
        p_defaultInstance = reader.ReadLong();
    }

    public override string ReadDefaultValue(MemoryReader reader)
    {
        long p = reader.ReadInt();
        if (p != 0)
        {
            FrostyLogger.Logger?.LogWarning("Ignored default value for class in another struct/class");
        }

        return string.Empty;
    }

    public void ReadDefaultValues(MemoryReader reader)
    {
        if (p_defaultInstance == 0)
        {
            return;
        }

        reader.Position = p_defaultInstance;
        (m_data as ClassInfoData)?.ReadDefaultValues(reader);
    }

    public int GetFieldCount()
    {
        int fieldCount = (m_data as ClassInfoData)?.GetFieldCount() ?? 0;
        ClassInfo superClass = GetSuperClassInfo();
        if (superClass != this)
        {
            fieldCount += superClass.GetFieldCount();
        }
        return fieldCount;
    }
}