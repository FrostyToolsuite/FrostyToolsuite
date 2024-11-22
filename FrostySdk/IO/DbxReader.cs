using Frosty.Sdk.Attributes;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO.Ebx;
using static Frosty.Sdk.Sdk.TypeFlags;
using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Xml;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Frosty.Sdk.IO;

public sealed class DbxReader
{
    private static readonly Type s_pointerType = typeof(PointerRef);
    private static readonly Type s_collectionType = typeof(ObservableCollection<>);
    private static readonly Type? s_boxedValueRefType = TypeLibrary.GetType("BoxedValueRef")?.Type;
    private static readonly Type? s_typeRefType = TypeLibrary.GetType("TypeRef")?.Type;

    // we only want to write out properties with these flags
    private static readonly BindingFlags s_propertyBindingFlags = BindingFlags.Public | BindingFlags.Instance;

    // the loaded dbx file
    private readonly XmlDocument m_xml = new();
    // key - instance guid, value - instance and its xml representation
    private readonly Dictionary<Guid, (object ebxInstance, XmlNode dbxInstance)> m_guidToObjAndXmlNode = new();
    // used to keep track of number of refs pointing to an instance
    private readonly Dictionary<Guid, int> m_guidToRefCount = new();

    private EbxAsset? m_ebx;
    private Guid m_primaryInstGuid;
    // incremented when an internal id is requested
    private int m_internalId = -1;
    private string m_filepath = string.Empty;

    public DbxReader(string filepath)
    {
        m_filepath = filepath;
        m_xml.Load(filepath);
        m_guidToObjAndXmlNode.Clear();
        m_guidToRefCount.Clear();
    }

    public DbxReader(Stream inStream)
    {
        m_xml.Load(inStream);
        m_guidToObjAndXmlNode.Clear();
        m_guidToRefCount.Clear();
    }

    public EbxAsset ReadAsset()
    {
        m_ebx = new EbxAsset();
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

    private static void SetProperty(object obj, Type objType, string propName, object? propValue)
    {
        PropertyInfo? property = objType.GetProperty(propName, s_propertyBindingFlags);
        if (property is not null && property.CanWrite)
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
        if (property is not null && property.CanWrite)
        {
            object? value = GetValueFromString(property.PropertyType, propValue, property.GetCustomAttribute<EbxFieldMetaAttribute>()?.Flags.GetTypeEnum());
            if (value is null)
            {
                Console.WriteLine($"Null value {propName}");
                return;
            }
            property.SetValue(obj, value);
        }
    }

    private static object GetValueFromString(Type propType, string propValue, TypeEnum? frostbiteType = null)
    {
        if (propType.IsPrimitive)
        {
            return Convert.ChangeType(propValue, propType);
        }

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
                return Enum.Parse(propType, propValue);
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
            case TypeEnum.CString:
            {
                return ValueToPrimitive(propValue, propType);
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
            ParseInstance(kvp.Value.dbxInstance, kvp.Value.ebxInstance, kvp.Key);
        }

        m_ebx!.refCounts = m_guidToRefCount.Values.ToList();
    }

    private void CreateInstance(XmlNode node)
    {
        Type? ebxType = TypeLibrary.GetType(GetAttributeValue(node, "type")!.Split('.')[^1])?.Type;
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
        m_guidToRefCount.Add(instGuid, 0);
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
            Guid extGuid = Guid.Parse(refGuid.Split('\\')[1]);
            Guid ebxFileGuid = Guid.Parse(refEbxGuid);
            EbxImportReference import = new() { InstanceGuid = extGuid, PartitionGuid = ebxFileGuid };
            m_ebx!.AddDependency(ebxFileGuid);
            return new PointerRef(import);
        }
        // internal
        else
        {
            Guid instGuid = Guid.Parse(refGuid);
            PointerRef internalRef = new PointerRef(m_guidToObjAndXmlNode[instGuid].ebxInstance);
            m_guidToRefCount[instGuid]++;
            return internalRef;
        }
    }

    private void ReadInstanceFields(XmlNode node, object obj, Type objType)
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
                    SetPropertyFromStringValue(obj, objType, GetAttributeValue(node, "name")!, node.InnerText);
                }
                break;
            }
            case "item":
            {
                if (isRef)
                {
                    ((IList?)obj)?.Add(ParseRef(node, GetAttributeValue(node, "ref")!));
                }
                else
                {
                    ((IList?)obj)?.Add(GetValueFromString(arrayElementType!, node.InnerText, arrayElementTypeEnum));
                }
                break;
            }
            case "array":
            {
                string arrayFieldName = GetAttributeValue(node, "name")!;
                object array = ReadArray(node);
                SetProperty(obj, objType, arrayFieldName, array);
                break;
            }
            case "complex":
            {
                string structFieldName = GetAttributeValue(node, "name")!;
                object structObj = ReadStruct(arrayElementType, node);
                if (isArray)
                {
                    ((IList?)obj)?.Add(structObj);
                }
                else
                {
                    SetProperty(obj, objType, structFieldName, structObj);
                }
                break;
            }
            case "boxed":
            {
                BoxedValueRef boxed;

                XmlNode? child = node.FirstChild;
                if (child is not null)
                {
                    object value;

                    string typeName = GetAttributeValue(node, "typeName")!;
                    IType? valueType = typeName == "PointerRef" ? new SdkType(typeof(PointerRef)) : TypeLibrary.GetType(typeName);
                    if (valueType is not SdkType)
                    {
                        break;
                    }

                    string? refGuid = GetAttributeValue(node, "ref");
                    if (refGuid is not null)
                    {
                        value = ParseRef(node, refGuid);
                    }
                    else
                    {
                        switch (child.Name)
                        {
                            case "complex":
                                value = ReadStruct(arrayElementType, child);
                                break;
                            case "array":
                                value = ReadArray(child);
                                break;
                            default:
                                value = GetValueFromString(valueType.Type, node.InnerText, valueType.GetFlags().GetTypeEnum());
                                break;
                        }
                    }

                    boxed = new BoxedValueRef(value, valueType.GetFlags());
                }
                else
                {
                    boxed = new BoxedValueRef();
                }
                SetProperty(obj, objType, GetAttributeValue(node, "name")!, ValueToPrimitive(boxed, s_boxedValueRefType!));
                break;
            }
            case "typeref":
            {
                string? typeName = GetAttributeValue(node, "typeName");
                TypeRef typeRef;

                if (typeName is not null)
                {
                    typeRef = new TypeRef(TypeLibrary.GetType(typeName));
                }
                else
                {
                    typeRef = new TypeRef();
                }

                SetProperty(obj, objType, GetAttributeValue(node, "name")!, ValueToPrimitive(typeRef, s_typeRefType!));
                break;
            }
            case "delegate":
            {
                string? typeName = GetAttributeValue(node, "typeName");
                IType? type = typeName is null ? null : TypeLibrary.GetType(typeName);
                PropertyInfo? property = objType.GetProperty(GetAttributeValue(node, "name")!, s_propertyBindingFlags);
                if (property is not null && property.CanWrite)
                {
                    IDelegate del = (IDelegate)Activator.CreateInstance(property.PropertyType)!;
                    del.FunctionType = type;
                }
                break;
            }
        }
    }

    private object ReadArray(XmlNode node)
    {
        string arrayTypeStr = GetAttributeValue(node, "type")!;
        bool isRef = arrayTypeStr.StartsWith("ref(");

        Type arrayElementType = (isRef ? s_pointerType : TypeLibrary.GetType(arrayTypeStr)?.Type)
                                ?? throw new ArgumentException($"array element type doesn't exist? {arrayTypeStr}");

        EbxTypeMetaAttribute? arrayTypeMeta = arrayElementType.GetCustomAttribute<EbxTypeMetaAttribute>();
        Type arrayType = s_collectionType.MakeGenericType(arrayElementType);

        object array = Activator.CreateInstance(arrayType)
                       ?? throw new ArgumentException($"failed to create array with element type {arrayElementType.Name}");

        if (node.ChildNodes.Count > 0)
        {
            foreach(XmlNode child in node.ChildNodes)
            {
                ReadField(ref array, child, arrayType, true, isRef, arrayElementType, arrayTypeMeta?.Flags.GetTypeEnum());
            }
        }

        return array;
    }

    private object ReadStruct(Type? structType, XmlNode node)
    {
        Type type = (structType ?? TypeLibrary.GetType(GetAttributeValue(node, "type")!)?.Type)
                    ?? throw new ArgumentException($"struct type doesn't exist?");

        object obj = Activator.CreateInstance(type)
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
