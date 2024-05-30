using System;
using System.Text;
using Frosty.Sdk.Interfaces;

namespace Frosty.Sdk.Sdk.TypeInfoDatas;

internal class PrimitiveInfoData : TypeInfoData
{
    public override void CreateType(StringBuilder sb)
    {
        base.CreateType(sb);

        string actualType = string.Empty;
        bool isString = false;

        switch (m_flags.GetTypeEnum())
        {
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
                        public struct {{m_name}} : IPrimitive
                        {
                            private {{actualType}} m_value{{(isString ? " = string.Empty" : string.Empty)}};

                            public {{m_name}}()
                            {
                            }

                            public object ToActualType() => m_value;

                            public void FromActualType(object value)
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
                                    return m_value.Equals(a.m_value);
                                }

                                if (obj is {{actualType}} b)
                                {
                                    return m_value.Equals(b);
                                }

                                return false;
                            }

                            public override int GetHashCode()
                            {
                                return m_value.GetHashCode();
                            }

                            public static bool operator ==({{m_name}} a, object? b) => a.Equals(b);

                            public static bool operator !=({{m_name}} a, object? b) => !a.Equals(b);

                            public static implicit operator {{actualType}}({{m_name}} value) => value.m_value;

                            public static implicit operator {{m_name}}({{actualType}} value) => new() { m_value = value };
                        }
                        """);
    }
}