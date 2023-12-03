using Frosty.Sdk.Attributes;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.Interfaces;
using static Frosty.Sdk.Sdk.TypeFlags;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Xml;
using System.Collections.ObjectModel;
using System.Collections;
using System.Collections.Generic;
using Frosty.Sdk.IO.Ebx;
using System.Runtime.CompilerServices;
using System.Data;

namespace Frosty.Sdk.IO;
public sealed class DbxReader
{
    private static readonly Type s_pointerType = typeof(PointerRef);
    private static readonly Type s_boxedType = typeof(BoxedValueRef);
    private static readonly Type s_collectionType = typeof(ObservableCollection<>);

    private static readonly BindingFlags s_propertyBindingFlags = BindingFlags.Public | BindingFlags.Instance;

    // the loaded dbx file
    private readonly XmlDocument m_xml = new();
    // key - instance guid, value - instance and its xml representation
    private readonly Dictionary<Guid, Tuple<object, XmlNode>> m_guidToObjAndXmlNode = new();

    private EbxAsset? m_ebx = null;
    private Guid m_primaryInstGuid;
    // incremented when an internal id is requested
    private int m_internalId = -1;
    private string m_filepath = string.Empty;

    public DbxReader() { }

    public EbxAsset Read(string filepath)
    {
        m_filepath = filepath;
        m_xml.Load(filepath);
        m_ebx = new();
        m_guidToObjAndXmlNode.Clear();

        return ReadDbx();
    }

    private static void SetProperty(object obj, Type objType, string propName, object propValue)
    {
        PropertyInfo? property = objType.GetProperty(propName, s_propertyBindingFlags);
        if (property != null && property.CanWrite)
        {
            if (propValue is null)
            {
                Console.WriteLine($"Null property {propName}");
                return;
            }
            property.SetValue(obj, propValue);
        }
    }

    private static void SetPropertyFromStringValue(object obj, Type objType, string propName, string propValue)
    {
        PropertyInfo? property = objType.GetProperty(propName, s_propertyBindingFlags);
        if (property != null && property.CanWrite)
        {
            object value = DbxReader.GetValueFromString(property.PropertyType, propValue, property.GetCustomAttribute<EbxFieldMetaAttribute>()?.Flags.GetTypeEnum());
            if (value is null)
            {
                Console.WriteLine($"Null value {propName}");
                return;
            }
            property.SetValue(obj, value!);
        }
    }

    private static dynamic GetValueFromString(Type propType, string propValue, TypeEnum? frostbiteType = null)
    {
        if (propType.IsPrimitive)
        {
            return Convert.ChangeType(propValue, propType);
        }
        else
        {
            switch (frostbiteType)
            {
                case TypeEnum.Boolean:
                {
                    return ValueToPrimitive(bool.Parse(propValue), propType);
                }
                case TypeEnum.Float32:
                {
                    return ValueToPrimitive(float.Parse(propValue), propType);
                }
                case TypeEnum.Float64:
                {
                    return ValueToPrimitive(double.Parse(propValue), propType);
                }
                case TypeEnum.Int8:
                {
                    return ValueToPrimitive(sbyte.Parse(propValue), propType);
                }
                case TypeEnum.Int16:
                {
                    return ValueToPrimitive(short.Parse(propValue), propType);
                }
                case TypeEnum.Int32:
                {
                    return ValueToPrimitive(int.Parse(propValue), propType);
                }
                case TypeEnum.Int64:
                {
                    return ValueToPrimitive(long.Parse(propValue), propType);
                }
                case TypeEnum.UInt8:
                {
                    return ValueToPrimitive(byte.Parse(propValue), propType);
                }
                case TypeEnum.UInt16:
                {
                    return ValueToPrimitive(ushort.Parse(propValue), propType);
                }
                case TypeEnum.UInt32:
                {
                    return ValueToPrimitive(uint.Parse(propValue), propType);
                }
                case TypeEnum.UInt64:
                {
                    return ValueToPrimitive(ulong.Parse(propValue), propType);
                }
                case TypeEnum.Enum:
                {
                    // @todo: some enum fields have no value? sdk issue?
                    return string.IsNullOrEmpty(propValue) ? null : Enum.Parse(propType, propValue);
                }
                case TypeEnum.Guid:
                {
                    return ValueToPrimitive(Guid.Parse(propValue), propType);
                }
                case TypeEnum.FileRef:
                {
                    return ValueToPrimitive(new FileRef(propValue), propType);
                }
                case TypeEnum.String:
                {
                    return ValueToPrimitive(propValue, propType);
                }
                case TypeEnum.CString:
                {
                    return ValueToPrimitive((CString)propValue, propType);
                }
                case TypeEnum.Class:
                {
                    return new PointerRef();
                }
                case TypeEnum.ResourceRef:
                {
                    return ValueToPrimitive(new ResourceRef(ulong.Parse(propValue, System.Globalization.NumberStyles.HexNumber)), propType);
                }
                default:
                    throw new NotImplementedException("Unimplemented property type: " + frostbiteType);
            }
        }
    }

    private static string? GetAttributeValue(XmlNode node, string name)
    {
        return node.Attributes?.GetNamedItem(name)?.Value;
    }

    // Converts the given value to the given frostbite primitive type (an int will be converted to Frostbite.Core.Int32)
    private static object ValueToPrimitive(object value, Type valueType)
    {
        IPrimitive primitive = (IPrimitive)Activator.CreateInstance(valueType)!;
        primitive.FromActualType(value);
        return primitive;
    }

    private EbxAsset ReadDbx()
    {
#if FROSTY_DEVELOPER
        Stopwatch w = new();
        w.Start();
#endif
        XmlNode? rootNode = m_xml.DocumentElement;

        Debug.Assert(rootNode != null && rootNode.Name == "partition", "Invalid DBX (root element is not a partition)");

        ReadPartition(rootNode);
#if FROSTY_DEVELOPER
        w.Stop();
        Console.WriteLine($"Dbx {m_filepath} read in {w.ElapsedMilliseconds} ms");
#endif
        return m_ebx!;
    }

    private void ReadPartition(XmlNode partitionNode)
    {
        Guid partitionGuid = Guid.Parse(GetAttributeValue(partitionNode, "guid")!);
        m_primaryInstGuid = Guid.Parse(GetAttributeValue(partitionNode, "primaryInstance")!);

        m_ebx!.partitionGuid = partitionGuid;

        foreach(XmlNode child in partitionNode.ChildNodes)
        {
            CreateInstance(child);
        }

        // because of pointers, instances must be initialized before being parsed
        foreach(var kvp in m_guidToObjAndXmlNode)
        {
            ParseInstance(kvp.Value.Item2, kvp.Value.Item1, kvp.Key);
        }
    }

    private void CreateInstance(XmlNode node)
    {
        Type? ebxType = TypeLibrary.GetType(GetAttributeValue(node, "id")!);
        if(ebxType is null)
        {
            return;
        }

        dynamic? obj = Activator.CreateInstance(ebxType);
        if(obj is null)
        {
            return;
        }

        bool isExported = bool.Parse(GetAttributeValue(node, "exported")!);
        Guid instGuid = Guid.Parse(GetAttributeValue(node, "guid")!);

        AssetClassGuid assetGuid = new
            (isExported ? instGuid : Guid.Empty,
            ++m_internalId);

        obj.SetInstanceGuid(assetGuid);
        m_guidToObjAndXmlNode.Add(instGuid, new(obj, node));
    }

    private void ParseInstance(XmlNode node, object obj, Guid instGuid)
    {
        ReadInstanceFields(node, obj, obj.GetType());
        m_ebx!.AddObject(obj, instGuid == m_primaryInstGuid);
    }

    private PointerRef ParseRef(XmlNode node, string refGuid)
    {
        if (refGuid == "null")
        {
            return new PointerRef();
        }

        string? refEbxGuid = GetAttributeValue(node, "partitionGuid");
        // external
        if (refEbxGuid != null)
        {
            string extGuid = refGuid.Split('\\')[1];
            EbxImportReference import = new() { ClassGuid = Guid.Parse(extGuid), FileGuid = Guid.Parse(refEbxGuid) };
            return new PointerRef(import);
        }
        // internal
        else
        {
            return new PointerRef(m_guidToObjAndXmlNode[Guid.Parse(refGuid)].Item1);
        }
    }

    private void ReadInstanceFields(XmlNode node, dynamic obj, Type objType)
    {
        foreach(XmlNode child in node.ChildNodes)
        {
            ReadField(ref obj, child, objType);
        }
    }

    private void ReadField(ref object obj,
        XmlNode node,
        Type objType,
        bool isArray = false,
        bool isRef = false,
        Type? arrayElementType = null,
        TypeEnum? arrayElementTypeEnum = null)
    {
        switch (node.Name)
        {
            case "field":
            {
                string? refGuid = GetAttributeValue(node, "ref");
                if (refGuid is not null)
                {
                    SetProperty(obj, objType, GetAttributeValue(node, "name")!, ParseRef(node, refGuid));
                }
                else
                {
                    SetPropertyFromStringValue(obj, objType, GetAttributeValue(node, "name")!, node.InnerText!);
                }
                break;
            }
            case "item":
            {
                if (isRef)
                {
                    ((dynamic)obj).Add(ParseRef(node, GetAttributeValue(node, "ref")!));
                }
                else
                {
                    ((dynamic)obj).Add(DbxReader.GetValueFromString(arrayElementType!, node.InnerText!, arrayElementTypeEnum));
                }
                break;
            }
            case "array":
            {
                string arrayFieldName = GetAttributeValue(node, "name")!;
                dynamic array = ReadArray(node);
                SetProperty(obj, objType, arrayFieldName, array);
                break;
            }
            case "complex":
            {
                string? structFieldName = GetAttributeValue(node, "name")!;
                dynamic structObj = ReadStruct(arrayElementType, node);
                if (isArray)
                {
                    ((dynamic)obj).Add(structObj);
                }
                else
                {
                    SetProperty(obj, objType, structFieldName!, structObj);
                }
                break;
            }
            case "boxed":
            {
                if (node.InnerText == "null")
                {
                    break;
                }

                Type? valueType = TypeLibrary.GetType(GetAttributeValue(node, "type")!);
                if (valueType is null)
                {
                    break;
                }

                EbxTypeMetaAttribute? typeMeta = valueType.GetCustomAttribute<EbxTypeMetaAttribute>();

                object value = DbxReader.GetValueFromString(valueType, node.InnerText, typeMeta!.Flags.GetTypeEnum());
                BoxedValueRef boxed = new(value, typeMeta!.Flags.GetTypeEnum());
                SetProperty(obj, objType, GetAttributeValue(node, "name")!, ValueToPrimitive(boxed, s_boxedType));
                break;
            }
            case "typeref":
            {
                throw new NotImplementedException("reading typerefs from DBX");
            }
        }
    }

    private dynamic ReadArray(XmlNode node)
    {
        string arrayTypeStr = GetAttributeValue(node, "type")!;
        bool isRef = arrayTypeStr.StartsWith("ref(");

        Type? arrayElementType = (isRef ? s_pointerType : TypeLibrary.GetType(GetAttributeValue(node, "type")!))
            ?? throw new ArgumentException($"array element type doesn't exist? {GetAttributeValue(node, "type"!)}");

        EbxTypeMetaAttribute? arrayTypeMeta = arrayElementType.GetCustomAttribute<EbxTypeMetaAttribute>();
        Type arrayType = s_collectionType.MakeGenericType(arrayElementType);

        object? array = Activator.CreateInstance(arrayType)
            ?? throw new ArgumentException($"failed to create array with element type {arrayElementType.Name}");

        if (node.ChildNodes.Count > 0)
        {
            foreach(XmlNode child in node.ChildNodes)
            {
                ReadField(ref array, child, arrayType, true, isRef, arrayElementType!, arrayTypeMeta?.Flags.GetTypeEnum());
            }
        }

        return array;
    }

    private dynamic ReadStruct(Type? structType, XmlNode node)
    {
        Type? type = (structType ?? TypeLibrary.GetType(GetAttributeValue(node, "type")!))
            ?? throw new ArgumentException($"struct type doesn't exist?");

        dynamic? obj = Activator.CreateInstance(type)
            ?? throw new ArgumentException($"failed to create struct of type {type.Name}");

        if (node.ChildNodes.Count > 0)
        {
            foreach(XmlNode child in node.ChildNodes)
            {
                ReadField(ref obj, child, type);
            }
        }

        return obj;
    }
}
