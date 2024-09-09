﻿using System.Collections.Generic;
using System.Text;
using Frosty.Sdk.IO;
using Frosty.Sdk.Sdk.TypeInfoDatas;
using Frosty.Sdk.Sdk.TypeInfos;

namespace Frosty.Sdk.Sdk;

internal class TypeInfo
{
    public static int Version
    {
        get
        {
            if (ProfilesLibrary.FrostbiteVersion <= "2014.1")
            {
                // 2013.2, 2014.1
                return 1;
            }
            if (ProfilesLibrary.FrostbiteVersion <= "2014.4.18")
            {
                // ushort FieldCount
                // 2014.4.11, 2014.4.17, 2014.4.18
                return 2;
            }
            if (ProfilesLibrary.FrostbiteVersion <= "2015.4.6")
            {
                // ArrayInfo in TypeInfoData
                // 2015.4, 2015.4.6
                return 3;
            }
            if (ProfilesLibrary.FrostbiteVersion <= "2016.4.7")
            {
                // Signature Guid in TypeInfo
                // 2016.4.1, 2016.4.4, 2016.4.7, (2018.0 (BfV), 2019-PR5 (SWBfII) -> changed to 2016.4.4)
                return 4;
            }
            if (ProfilesLibrary.FrostbiteVersion <= "2018.2")
            {
                // Guid and NameHash in TypeInfoData
                // 2017.3, 2017.7, 2018.0, 2018.2
                return 5;
            }
            if (ProfilesLibrary.FrostbiteVersion <= "2021.2.3")
            {
                // Prev TypeInfo and Signature as uint in TypeInfoData
                // 2019.0, 2020.0, 2021.1.1, 2021.2.0, 2021.2.3
                return 6;
            }

            // Field offset as uint
            // madden 25 doesnt have buildinfo
            // 2022.2.1
            return 7;
        }
    }

    public static Dictionary<long, TypeInfo>? TypeInfoMapping;

    protected long p_this;
    protected TypeInfoData m_data;
    protected long p_prev;
    protected long p_next;
    protected ushort m_id;
    protected ushort m_flags;

    public TypeInfo(TypeInfoData data)
    {
        m_data = data;
    }

    public static TypeInfo ReadTypeInfo(MemoryReader reader)
    {
        long startPos = reader.Position;

        long typeInfoDataOffset = reader.ReadLong();

        long curPos = reader.Position;

        reader.Position = typeInfoDataOffset;

        TypeInfoData data = TypeInfoData.ReadTypeInfoData(reader);

        reader.Position = curPos;

        TypeInfo retVal = CreateTypeInfo(data);

        retVal.p_this = startPos;
        retVal.Read(reader);

        TypeInfoMapping!.Add(startPos, retVal);

        return retVal;
    }

    public static TypeInfo CreateTypeInfo(TypeInfoData data)
    {
        if (data is StructInfoData structData)
        {
            return new StructInfo(structData);
        }
        if (data is ClassInfoData classData)
        {
            return new ClassInfo(classData);
        }
        if (data is ArrayInfoData arrayData)
        {
            return new ArrayInfo(arrayData);
        }
        if (data is EnumInfoData enumData)
        {
            return new EnumInfo(enumData);
        }
        if (data is FunctionInfoData functionData)
        {
            return new FunctionInfo(functionData);
        }
        if (data is DelegateInfoData delegateData)
        {
            return new DelegateInfo(delegateData);
        }
        if (data is InterfaceInfoData interfaceData)
        {
            return new InterfaceInfo(interfaceData);
        }
        if (data is PrimitiveInfoData primitiveData)
        {
            return new PrimitiveInfo(primitiveData);
        }
        return new TypeInfo(data);
    }

    public virtual void Read(MemoryReader reader)
    {
        if (Version > 5)
        {
            p_prev = reader.ReadLong();
        }

        p_next = reader.ReadLong();

        if (Version == 4)
        {
            m_data.SetGuid(reader.ReadGuid());
        }
        if (Version == 4 || Version == 5)
        {
            // signature
            reader.ReadGuid();
        }

        m_id = reader.ReadUShort();
        m_flags = reader.ReadUShort();
    }

    public virtual string ReadDefaultValue(MemoryReader reader)
    {
        return string.Empty;
    }

    public TypeInfo? GetNextTypeInfo(MemoryReader reader)
    {
        if (p_next == 0)
        {
            return null;
        }
        reader.Position = p_next;
        return ReadTypeInfo(reader);
    }

    public string GetName() => m_data.CleanUpName();

    public string GetFullName() => m_data.GetFullName();

    public TypeFlags GetFlags() => m_data.GetFlags();

    public void CreateType(StringBuilder sb)
    {
        m_data.CreateNamespace(sb);

        if (Version < 3 && ArrayInfo.Mapping!.TryGetValue(p_this, out long arrayInfoPtr))
        {
            m_data.SetArrayInfoPtr(arrayInfoPtr);
        }

        m_data.TypeInfoPtr = p_this;
        m_data.CreateType(sb);

        sb.AppendLine("}");
    }

    public void UpdateName()
    {
        m_data.UpdateName();
    }
}