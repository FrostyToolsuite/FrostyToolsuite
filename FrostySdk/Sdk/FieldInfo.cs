using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.IO;
using Frosty.Sdk.Sdk.TypeInfos;

namespace Frosty.Sdk.Sdk;

internal class FieldInfo : IComparable
{
    public string GetName() => m_name;
    public TypeInfo GetTypeInfo() => TypeInfo.TypeInfoMapping![p_typeInfo];
    public int GetEnumValue() => (int)p_typeInfo;

    private string m_name = string.Empty;
    private uint m_nameHash;
    private TypeFlags m_flags;
    private uint m_offset;
    private long p_typeInfo;

    private string? m_defaultValue;

    public void Read(MemoryReader reader, uint inTypeHash, string inTypeName)
    {
        if (!ProfilesLibrary.HasStrippedTypeNames)
        {
            m_name = reader.ReadNullTerminatedString();
        }

        if (TypeInfo.Version > 4)
        {
            m_nameHash = reader.ReadUInt();

            if (ProfilesLibrary.HasStrippedTypeNames && !Strings.HasStrings)
            {
                Strings.FieldHashes!.TryAdd(inTypeHash, new HashSet<uint>());
                Strings.FieldHashes[inTypeHash].Add(m_nameHash);

                Strings.FieldMapping!.TryAdd(inTypeHash, new Dictionary<uint, string>());
                Strings.FieldMapping[inTypeHash].Add(m_nameHash, string.Empty);
            }
        }
        else
        {
            m_nameHash = (uint)Utils.Utils.HashString(m_name);
        }

        if (!ProfilesLibrary.HasStrippedTypeNames)
        {
            Strings.FieldNames!.TryAdd(inTypeName, new HashSet<string>());
            Strings.FieldNames[inTypeName].Add(m_name);
        }

        m_flags = reader.ReadUShort();
        if (TypeInfo.Version > 6)
        {
            m_offset = reader.ReadUInt();
        }
        else
        {
            m_offset = reader.ReadUShort();
        }

        p_typeInfo = reader.ReadLong();

        if (ProfilesLibrary.HasStrippedTypeNames)
        {
            if (Strings.HasStrings && Strings.FieldMapping!.TryGetValue(inTypeHash, out Dictionary<uint, string>? dict) &&
                dict.TryGetValue(m_nameHash, out string? resolvedName))
            {
                Debug.Assert(!string.IsNullOrEmpty(resolvedName));
                m_name = resolvedName;
            }
            else
            {
                m_name = $"Field_{m_nameHash:x8}";
            }
        }
    }

    public void ReadDefaultValue(MemoryReader reader, long pInstance)
    {
        if (p_typeInfo == 0)
        {
            return;
        }

        reader.Position = pInstance + m_offset;


        m_defaultValue = GetTypeInfo().ReadDefaultValue(reader);
    }

    public string ReadValue(MemoryReader reader, long pInstance)
    {
        if (p_typeInfo == 0)
        {
            return string.Empty;
        }

        reader.Position = pInstance + m_offset;


        return GetTypeInfo().ReadDefaultValue(reader);
    }

    public void CreateField(StringBuilder sb)
    {
        TypeInfo type = GetTypeInfo();
        string typeName = type.GetFullName();
        TypeFlags flags = type.GetFlags();
        bool isClass = false;

        bool needInit = type is StructInfo or ArrayInfo;

        if (type is ClassInfo)
        {
            typeName = "Frosty.Sdk.Ebx.PointerRef";
            isClass = true;
        }
        else if (type is ArrayInfo arrayInfo)
        {
            type = arrayInfo.GetTypeInfo();
            typeName = type.GetFullName();
            if (type is ClassInfo)
            {
                typeName = "Frosty.Sdk.Ebx.PointerRef";
                isClass = true;
            }
            typeName = $"ObservableCollection<{typeName}>";
        }

        if (string.IsNullOrEmpty(m_defaultValue) && needInit)
        {
            // force default value, for stuff that doesnt have a default instance
            m_defaultValue = "new()";
        }

        sb.AppendLine($"[{nameof(EbxFieldMetaAttribute)}({(ushort)flags}, {m_offset}, {(isClass ? $"typeof({type.GetFullName()})" : "null")})]");
        sb.AppendLine($"[{nameof(NameHashAttribute)}({m_nameHash})]");

        sb.AppendLine($"private {typeName} _{m_name}{(string.IsNullOrEmpty(m_defaultValue) ? string.Empty : $" = {m_defaultValue}")};");
    }

    public int CompareTo(object? obj)
    {
        return m_offset.CompareTo((obj as FieldInfo)!.m_offset);
    }

    public void UpdateName(uint inTypeHash)
    {
        if (ProfilesLibrary.HasStrippedTypeNames && Strings.HasStrings && Strings.FieldMapping!.TryGetValue(inTypeHash, out Dictionary<uint, string>? dict) &&
            dict.TryGetValue(m_nameHash, out string? resolvedName))
        {
            Debug.Assert(!string.IsNullOrEmpty(resolvedName));
            m_name = resolvedName;
        }
    }
}