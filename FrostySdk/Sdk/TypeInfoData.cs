using System;
using System.Diagnostics;
using System.Text;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.IO;
using Frosty.Sdk.Sdk.TypeInfoDatas;
using Frosty.Sdk.Sdk.TypeInfos;
using Microsoft.Extensions.Logging;

namespace Frosty.Sdk.Sdk;

internal class TypeInfoData
{
    public long TypeInfoPtr { get; set; }

    protected string m_name = string.Empty;
    protected uint m_nameHash;
    protected TypeFlags m_flags;
    protected ushort m_size;
    protected Guid m_guid;
    protected string m_nameSpace = string.Empty;
    protected long p_arrayInfo;
    protected byte m_alignment;
    protected ushort m_fieldCount;
    protected uint m_signature;

    public static TypeInfoData ReadTypeInfoData(MemoryReader reader)
    {
        TypeInfoData retVal;
        string name = string.Empty;
        if (!ProfilesLibrary.HasStrippedTypeNames)
        {
            name = reader.ReadNullTerminatedString();
        }

        uint nameHash = uint.MaxValue;
        if (TypeInfo.Version > 4)
        {
            nameHash = reader.ReadUInt();

            if (ProfilesLibrary.HasStrippedTypeNames && !Strings.HasStrings)
            {
                Strings.TypeHashes!.Add(nameHash);
                Strings.TypeMapping!.Add(nameHash, string.Empty);
            }
        }

        if (!ProfilesLibrary.HasStrippedTypeNames)
        {
            Strings.TypeNames!.Add(name);
        }

        TypeFlags flags = reader.ReadUShort();

        switch (flags.GetTypeEnum())
        {
            case TypeFlags.TypeEnum.Struct:
                retVal = new StructInfoData();
                break;
            case TypeFlags.TypeEnum.Class:
                retVal = new ClassInfoData();
                break;
            case TypeFlags.TypeEnum.Array:
                retVal = new ArrayInfoData();
                break;
            case TypeFlags.TypeEnum.Enum:
                retVal = new EnumInfoData();
                break;
            case TypeFlags.TypeEnum.Function:
                retVal = new FunctionInfoData();
                break;
            case TypeFlags.TypeEnum.Delegate:
                retVal = new DelegateInfoData();
                break;
            case TypeFlags.TypeEnum.Interface:
                retVal = new InterfaceInfoData();
                break;
            case TypeFlags.TypeEnum.Void:
            case TypeFlags.TypeEnum.String:
            case TypeFlags.TypeEnum.CString:
            case TypeFlags.TypeEnum.FileRef:
            case TypeFlags.TypeEnum.Boolean:
            case TypeFlags.TypeEnum.Int8:
            case TypeFlags.TypeEnum.UInt8:
            case TypeFlags.TypeEnum.Int16:
            case TypeFlags.TypeEnum.UInt16:
            case TypeFlags.TypeEnum.Int32:
            case TypeFlags.TypeEnum.UInt32:
            case TypeFlags.TypeEnum.Int64:
            case TypeFlags.TypeEnum.UInt64:
            case TypeFlags.TypeEnum.Float32:
            case TypeFlags.TypeEnum.Float64:
            case TypeFlags.TypeEnum.Guid:
            case TypeFlags.TypeEnum.Sha1:
            case TypeFlags.TypeEnum.ResourceRef:
            case TypeFlags.TypeEnum.TypeRef:
            case TypeFlags.TypeEnum.BoxedValueRef:
                retVal = new PrimitiveInfoData();
                break;

            default:
                retVal = new TypeInfoData();
                FrostyLogger.Logger?.LogWarning($"Not implemented type: {flags.GetTypeEnum()}");
                break;
        }

        if (ProfilesLibrary.HasStrippedTypeNames && Strings.HasStrings && Strings.TypeMapping!.TryGetValue(nameHash, out string? resolvedName))
        {
            name = resolvedName;
        }

        retVal.m_name = name;
        retVal.m_nameHash = nameHash;
        retVal.m_flags = flags;

        retVal.Read(reader);

        return retVal;
    }

    public virtual void Read(MemoryReader reader)
    {
        m_size = reader.ReadUShort();

        if (TypeInfo.Version > 4)
        {
            m_guid = reader.ReadGuid();
        }

        long nameSpaceOffset = reader.ReadLong();
        long curPos = reader.Position;
        reader.Position = nameSpaceOffset;
        m_nameSpace = reader.ReadNullTerminatedString();
        reader.Position = curPos;

        if (TypeInfo.Version > 2)
        {
            p_arrayInfo = reader.ReadLong();
        }

        m_alignment = reader.ReadByte();
        m_fieldCount = TypeInfo.Version > 1 ? reader.ReadUShort() : reader.ReadByte();
        if (TypeInfo.Version > 5)
        {
            m_signature = reader.ReadUInt();
        }
    }

    public void SetGuid(Guid guid) => m_guid = guid;

    public string GetName() => m_name;

    public TypeFlags GetFlags() => m_flags;
    public ushort GetSize() => m_size;

    public void CreateNamespace(StringBuilder sb)
    {
        sb.AppendLine($"namespace Frostbite.{m_nameSpace}");
        sb.AppendLine("{");
    }

    public virtual void CreateType(StringBuilder sb)
    {
        sb.AppendLine($"[{nameof(TypeOffsetAttribute)}({TypeInfoPtr})]");
        sb.AppendLine($"[{nameof(EbxTypeMetaAttribute)}({(ushort)m_flags}, {m_alignment}, {m_size})]");

        sb.AppendLine($"[{nameof(DisplayNameAttribute)}(\"{m_name}\")]");

        if (m_nameHash != uint.MaxValue)
        {
            sb.AppendLine($"[{nameof(NameHashAttribute)}({m_nameHash})]");
        }

        if (!m_guid.Equals(Guid.Empty))
        {
            sb.AppendLine($"[{nameof(GuidAttribute)}(\"{m_guid}\")]");
        }
        if (m_signature != 0)
        {
            sb.AppendLine($"[{nameof(SignatureAttribute)}({m_signature})]");
        }

        if (TypeInfo.TypeInfoMapping!.TryGetValue(p_arrayInfo, out TypeInfo? value))
        {
            ArrayInfo arrayInfo = (value as ArrayInfo)!;
            arrayInfo.CreateType(sb);
        }
    }

    public string CleanUpName() => CleanUpString(m_name);

    public string CleanUpString(string name)
    {
        if (name == "char")
        {
            return "Char";
        }

        if (name.Contains("::"))
        {
            return name[(name.IndexOf("::", StringComparison.Ordinal) + 2)..];
        }

        // delegate/function stuff
        name = name.Replace("(", "_").Replace(" ", "_").Replace(")", string.Empty).Replace("[]", "_Array")
            .Replace(",", "_");
        return name.Replace(':', '_').Replace("<", "_").Replace(">", "_");
    }

    public void SetArrayInfoPtr(long inArrayInfoPtr)
    {
        p_arrayInfo = inArrayInfoPtr;
    }

    public string GetFullName()
    {
        string name = m_name;
        if (name == "char")
        {
            name = "Char";
        }

        if (name.Contains("::"))
        {
            name = name.Replace("::", ".");
        }

        // delegate/function stuff
        name = name.Replace("(", "_").Replace(" ", "_").Replace(")", string.Empty).Replace("[]", "_Array")
            .Replace(",", "_");

        name = name.Replace(':', '_').Replace("<", "_").Replace(">", "_");

        return $"Frostbite.{m_nameSpace}.{name}";
    }

    public virtual void UpdateName()
    {
        if (ProfilesLibrary.HasStrippedTypeNames && Strings.HasStrings && Strings.TypeMapping!.TryGetValue(m_nameHash, out string? resolvedName))
        {
            m_name = resolvedName;
        }
    }
}