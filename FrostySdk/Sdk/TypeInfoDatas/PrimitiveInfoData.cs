using System.Globalization;
using System.Text;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;

namespace Frosty.Sdk.Sdk.TypeInfoDatas;

internal class PrimitiveInfoData : TypeInfoData
{
    public string ReadDefaultValue(MemoryReader reader)
    {
        switch (m_flags.GetTypeEnum())
        {
            case TypeFlags.TypeEnum.Void:
                break;
            case TypeFlags.TypeEnum.String:
                break;
            case TypeFlags.TypeEnum.CString:
                return $"@\"{reader.ReadNullTerminatedString()}\"";
            case TypeFlags.TypeEnum.FileRef:
                return $"new Frosty.Sdk.Ebx.FileRef(\"{reader.ReadNullTerminatedString()}\")"; // TODO: not sure about this
            case TypeFlags.TypeEnum.Boolean:
                return (reader.ReadByte() != 0) ? "true" : "false";
            case TypeFlags.TypeEnum.Int8:
                return ((sbyte)reader.ReadByte()).ToString();
            case TypeFlags.TypeEnum.UInt8:
                return reader.ReadByte().ToString();
            case TypeFlags.TypeEnum.Int16:
                return reader.ReadShort().ToString();
            case TypeFlags.TypeEnum.UInt16:
                return reader.ReadUShort().ToString();
            case TypeFlags.TypeEnum.Int32:
                return reader.ReadInt().ToString();
            case TypeFlags.TypeEnum.UInt32:
                return reader.ReadUInt().ToString();
            case TypeFlags.TypeEnum.Int64:
                return reader.ReadLong().ToString();
            case TypeFlags.TypeEnum.UInt64:
                return reader.ReadULong().ToString();
            case TypeFlags.TypeEnum.Float32:
                return $"{reader.ReadSingle().ToString(CultureInfo.InvariantCulture)}f";
            case TypeFlags.TypeEnum.Float64:
                return reader.ReadDouble().ToString(CultureInfo.InvariantCulture);
            case TypeFlags.TypeEnum.Guid:
                return $"System.Guid.Parse(\"{reader.ReadGuid().ToString()}\")";
            case TypeFlags.TypeEnum.Sha1:
                return $"new Frosty.Sdk.Sha1(\"{reader.ReadSha1().ToString()}\")";
            case TypeFlags.TypeEnum.ResourceRef:
                return $"new Frosty.Sdk.Ebx.ResourceRef({reader.ReadULong().ToString()})";
            case TypeFlags.TypeEnum.TypeRef:
                long ptr = reader.ReadLong();
                if (!TypeInfo.TypeInfoMapping!.TryGetValue(ptr, out TypeInfo? type))
                {
                    return "default";
                }

                return $"new Frosty.Sdk.Ebx.TypeRef(new SdkType(typeof({type.GetFullName()})))";
            case TypeFlags.TypeEnum.BoxedValueRef:
                ptr = reader.ReadLong();
                long valuePtr = reader.ReadLong();
                if (!TypeInfo.TypeInfoMapping!.TryGetValue(ptr, out type))
                {
                    return "default";
                }

                reader.Position = valuePtr;
                string defaultValue = type.ReadDefaultValue(reader);

                return $"new Frosty.Sdk.Ebx.BoxedValueRef()"; // TODO:
        }

        return string.Empty;
    }

    public override void CreateType(StringBuilder sb)
    {
        base.CreateType(sb);

        string actualType = string.Empty;
        bool isString = false;

        switch (m_flags.GetTypeEnum())
        {
            case TypeFlags.TypeEnum.Void:
                sb.AppendLine($$"""
                        public struct {{m_name}}
                        {
                        }
                        """);
                return;
            case TypeFlags.TypeEnum.String:
            case TypeFlags.TypeEnum.CString:
                actualType = "System.String";
                isString = true;
                break;
            case TypeFlags.TypeEnum.FileRef:
                actualType = "Frosty.Sdk.Ebx.FileRef";
                break;
            case TypeFlags.TypeEnum.Boolean:
                actualType = "System.Boolean";
                break;
            case TypeFlags.TypeEnum.Int8:
                actualType = "System.SByte";
                break;
            case TypeFlags.TypeEnum.UInt8:
                actualType = "System.Byte";
                break;
            case TypeFlags.TypeEnum.Int16:
                actualType = "System.Int16";
                break;
            case TypeFlags.TypeEnum.UInt16:
                actualType = "System.UInt16";
                break;
            case TypeFlags.TypeEnum.Int32:
                actualType = "System.Int32";
                break;
            case TypeFlags.TypeEnum.UInt32:
                actualType = "System.UInt32";
                break;
            case TypeFlags.TypeEnum.Int64:
                actualType = "System.Int64";
                break;
            case TypeFlags.TypeEnum.UInt64:
                actualType = "System.UInt64";
                break;
            case TypeFlags.TypeEnum.Float32:
                actualType = "System.Single";
                break;
            case TypeFlags.TypeEnum.Float64:
                actualType = "System.Double";
                break;
            case TypeFlags.TypeEnum.Guid:
                actualType = "System.Guid";
                break;
            case TypeFlags.TypeEnum.Sha1:
                actualType = "Frosty.Sdk.Sha1";
                break;
            case TypeFlags.TypeEnum.ResourceRef:
                actualType = "Frosty.Sdk.Ebx.ResourceRef";
                break;
            case TypeFlags.TypeEnum.TypeRef:
                actualType = "Frosty.Sdk.Ebx.TypeRef";
                break;
            case TypeFlags.TypeEnum.BoxedValueRef:
                actualType = "Frosty.Sdk.Ebx.BoxedValueRef";
                break;
        }

        sb.AppendLine($$"""
                        public struct {{m_name}} : {{nameof(IPrimitive)}}
                        {
                            private {{actualType}}{{(isString ? "?" : string.Empty)}} m_value;

                            public {{m_name}}()
                            {
                            }

                            public object {{nameof(IPrimitive.ToActualType)}}() => m_value{{(isString ? " ?? string.Empty" : string.Empty)}};

                            public void {{nameof(IPrimitive.FromActualType)}}(object value)
                            {
                                if (value is not {{actualType}})
                                {
                                    throw new ArgumentException("Parameter needs to be of type {{actualType}}", nameof(value));
                                }

                                m_value = ({{actualType}})value;
                            }

                            public override bool Equals(object? obj)
                            {
                                if (obj is {{m_name}} a)
                                {
                                    return m_value == a.m_value;
                                }

                                if (obj is {{actualType}} b)
                                {
                                    return m_value == b;
                                }

                                return false;
                            }

                            public override int GetHashCode()
                            {
                                return m_value{{(isString ? "?" : string.Empty)}}.GetHashCode(){{(isString ? " ?? 0" : string.Empty)}};
                            }

                            public static bool operator ==({{m_name}} a, object? b) => a.Equals(b);

                            public static bool operator !=({{m_name}} a, object? b) => !a.Equals(b);

                            public static implicit operator {{actualType}}({{m_name}} value) => value.m_value{{(isString ? " ?? string.Empty" : string.Empty)}};

                            public static implicit operator {{m_name}}({{actualType}} value) => new() { m_value = value };
                        }
                        """);
    }
}