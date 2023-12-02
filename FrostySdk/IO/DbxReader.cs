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

namespace Frosty.Sdk.IO;
public class DbxReader : IDisposable
{
    private static Type s_pointerType = typeof(PointerRef);
    private static Type s_collectionType = typeof(ObservableCollection<>);

    private XmlReader m_xmlReader;
    private string m_filepath;
    private EbxAsset m_ebx = new();
    private Guid m_primaryInstGuid;
    // incremented when an internal id is requested
    private int m_internalId = -1;

    public DbxReader(string inFilePath)
    {
        m_filepath = inFilePath;
        XmlReaderSettings settings = new() { IgnoreWhitespace = true };
        m_xmlReader = XmlReader.Create(inFilePath, settings);
    }
    public EbxAsset Read()
    {
        return ReadDbx();
    }

    public void Dispose()
    {
        m_xmlReader.Dispose();
    }

    private EbxAsset ReadDbx()
    {
#if FROSTY_DEVELOPER
        Stopwatch w = new();
        w.Start();
#endif
        m_xmlReader.Read();
        // skip xml declaration
        m_xmlReader.Read();

        Debug.Assert(m_xmlReader.NodeType == XmlNodeType.Element && m_xmlReader.Name == "partition", "Invalid DBX (root element is not a partition)");

        ReadPartition();
#if FROSTY_DEVELOPER
        w.Stop();
        Console.WriteLine($"Dbx {m_filepath} read in {w.ElapsedMilliseconds} ms");
#endif
        return m_ebx;
    }

    private void ReadPartition()
    {
        Guid partitionGuid = Guid.Parse(m_xmlReader.GetAttribute("guid")!);
        m_primaryInstGuid = Guid.Parse(m_xmlReader.GetAttribute("primaryInstance")!);

        m_ebx.partitionGuid = partitionGuid;

        while(m_xmlReader.Read())
        {
            if(m_xmlReader.NodeType == XmlNodeType.EndElement)
            {
                break;
            }
            switch (m_xmlReader.Name)
            {
                case "instance":
                {
                    ReadInstance();
                    break;
                }
                default:
                {
                    Console.WriteLine($"Child element of partition isn't an instance? {m_xmlReader.Name}");
                    break;
                }
            }
        }
    }

    private void ReadInstance()
    {
        Type? ebxType = TypeLibrary.GetType(m_xmlReader.GetAttribute("id")!);
        if(ebxType is null)
        {
            return;
        }

        dynamic? obj = Activator.CreateInstance(ebxType);
        if(obj == null)
        {
            return;
        }

        bool isExported = bool.Parse(m_xmlReader.GetAttribute("exported")!);
        Guid instGuid = Guid.Parse(m_xmlReader.GetAttribute("guid")!);

        AssetClassGuid assetGuid = new
            (isExported ? instGuid : Guid.Empty,
            ++m_internalId);

        obj.SetInstanceGuid(assetGuid);

        ReadInstanceFields(obj, ebxType);
        m_ebx.AddObject(obj, instGuid == m_primaryInstGuid);
    }

    private void ReadInstanceFields(dynamic obj, Type objType)
    {
        m_xmlReader.Read();
        while(!(m_xmlReader.NodeType == XmlNodeType.EndElement && m_xmlReader.Name == "instance"))
        {
            ReadField(ref obj, objType);
        }
    }

    private void ReadField(ref object obj, Type objType, bool isArray = false, bool isRef = false, Type? arrayElementType = null, TypeEnum? arrayElementTypeEnum = null)
    {
        switch (m_xmlReader.NodeType)
        {
            case XmlNodeType.Element:
            {
                switch (m_xmlReader.Name)
                {
                    case "field":
                        string? refGuid = m_xmlReader.GetAttribute("ref");
                        if(refGuid != null)
                        {
                            // @todo: pointers
                            //SetPropertyFromRefValue(objType, m_xmlReader.GetAttribute("name")!, refGuid!, m_xmlReader.GetAttribute("partitionGuid"));
                            m_xmlReader.Read();
                        }
                        else
                        {
                            SetPropertyFromStringValue(obj, objType, m_xmlReader.GetAttribute("name")!, m_xmlReader.ReadElementContentAsString());
                        }
                        break;
                    case "item":
                        if(isRef)
                        {
                            ((dynamic)obj).Add(new PointerRef());
                            m_xmlReader.Read();
                        }
                        else
                        {
                            ((dynamic)obj).Add(GetValueFromString(arrayElementType!, m_xmlReader.ReadElementContentAsString(), arrayElementTypeEnum));
                        }
                        break;
                    case "array":
                        string arrayFieldName = m_xmlReader.GetAttribute("name")!;
                        dynamic? array = ReadArray();
                        SetProperty(obj, objType, arrayFieldName, array);
                        break;
                    case "complex":
                        string? structFieldName = m_xmlReader.GetAttribute("name");
                        dynamic? structObj = ReadStruct(arrayElementType);
                        if(isArray)
                        {
                            ((dynamic)obj).Add(structObj);
                        }
                        else
                        {
                            SetProperty(obj, objType, structFieldName!, structObj);
                        }
                        break;
                }
                break;
            }
            case XmlNodeType.EndElement:
            {
                m_xmlReader.Read();
                break;
            }
        }
    }

    private dynamic? ReadArray()
    {
        string arrayTypeStr = m_xmlReader.GetAttribute("type")!;
        bool isRef = arrayTypeStr.StartsWith("ref(");

        Type? arrayElementType = isRef ? s_pointerType : TypeLibrary.GetType(m_xmlReader.GetAttribute("type")!);
        if(arrayElementType == null)
        {
            return null;
        }

        EbxTypeMetaAttribute? arrayTypeMeta = arrayElementType.GetCustomAttribute<EbxTypeMetaAttribute>();
        Type arrayType = s_collectionType.MakeGenericType(arrayElementType);

        object? array = Activator.CreateInstance(arrayType);
        if(array == null)
        {
            return null;
        }

        if(!m_xmlReader.IsEmptyElement)
        {
            m_xmlReader.Read();
            while (!(m_xmlReader.NodeType == XmlNodeType.EndElement && m_xmlReader.Name == "array"))
            {
                ReadField(ref array, arrayType, true, isRef, arrayElementType!, arrayTypeMeta?.Flags.GetTypeEnum());
            }
        }
        else
        {
            m_xmlReader.Read();
        }

        return array;
    }

    private dynamic? ReadStruct(Type? structType)
    {
        Type? type = structType != null ? structType : TypeLibrary.GetType(m_xmlReader.GetAttribute("type")!);
        if(type is null)
        {
            return null;
        }

        dynamic? obj = Activator.CreateInstance(type);
        if(obj is null)
        {
            return null;
        }

        if(!m_xmlReader.IsEmptyElement)
        {
            m_xmlReader.Read();
            while(!(m_xmlReader.NodeType == XmlNodeType.EndElement && m_xmlReader.Name == "complex"))
            {
                ReadField(ref obj, type);
            }
            m_xmlReader.Read();
        }
        else
        {
            m_xmlReader.Read();
        }

        return obj;
    }

    private void SetProperty(object obj, Type objType, string propName, object propValue)
    {
        PropertyInfo? property = objType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
        if(property != null && property.CanWrite)
        {
            if(propValue is null)
            {
                Console.WriteLine($"Null property {propName}");
                return;
            }
            property.SetValue(obj, propValue);
        }
    }

    private void SetPropertyFromStringValue(object obj, Type objType, string propName, string propValue)
    {
        PropertyInfo? property = objType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
        if (property != null && property.CanWrite)
        {
            object value = GetValueFromString(property.PropertyType, propValue, property.GetCustomAttribute<EbxFieldMetaAttribute>()?.Flags.GetTypeEnum());
            if (value is null)
            {
                Console.WriteLine($"Null value {propName}");
                return;
            }
            property.SetValue(obj, value!);
        }
    }

    private dynamic GetValueFromString(Type propType, string propValue, TypeEnum? frostbiteType = null)
    {
        if (propType.IsPrimitive)
        {
            return Convert.ChangeType(propValue, propType);
        }
        else
        {
            switch (frostbiteType)
            {
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
                default:
                    throw new NotImplementedException("Unimplemented property type: " + frostbiteType);
            }
        }
    }

    // Converts the given value to a frostbite primitive type (an int will be converted to Frostbite.Core.Int32)
    private object ValueToPrimitive(object value, Type valueType)
    {
        IPrimitive primitive = (IPrimitive)Activator.CreateInstance(valueType)!;
        primitive.FromActualType(value);
        return primitive;
    }

    //private int GetNextInternalId()
    //{
    //    return ++m_internalId;
    //}

    //private int GetInternalIdFromGuid(Guid guid)
    //{
    //    int id = guid.ToByteArray()[15];
    //    m_internalId = id;
    //    return id;
    //}
}
