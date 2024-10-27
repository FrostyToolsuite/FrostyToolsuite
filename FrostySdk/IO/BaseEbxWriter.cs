using Frosty.Sdk.Attributes;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO.Ebx;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Frosty.Sdk.IO;

public abstract class BaseEbxWriter
{
    protected readonly DataStream m_stream;
    protected readonly List<object> m_objs = new();
    protected readonly List<object> m_objsSorted = new();

    protected static readonly Type s_pointerType = typeof(PointerRef);
    protected static readonly Type s_valueType = typeof(ValueType);
    protected static readonly Type s_objectType = typeof(object);
    protected static readonly Type s_dataContainerType = TypeLibrary.GetType("DataContainer")!;
    protected static readonly Type? s_boxedValueRefType = TypeLibrary.GetType("BoxedValueRef");

    protected static readonly string s_ebxNamespace = "Frostbite";
    protected static readonly string s_collectionName = "ObservableCollection`1";

    protected uint m_stringsLength = 0;
    protected List<string> m_strings = new();

    protected HashSet<int> m_typesToProcessSet = new();
    protected List<Type> m_typesToProcess = new();
    protected HashSet<object> m_processedObjects = new();
    protected Dictionary<uint, int> m_typeToDescriptor = new();

    protected HashSet<EbxImportReference> m_imports = new();
    protected Dictionary<EbxImportReference, int> m_importOrderFw = new();
    protected Dictionary<int, EbxImportReference> m_importOrderBw = new();

    protected BaseEbxWriter(DataStream inStream)
    {
        m_stream = inStream;
    }

    public static BaseEbxWriter CreateWriter(DataStream inStream)
    {
        return ProfilesLibrary.EbxVersion == 6 ? new RiffEbx.EbxWriter(inStream) : new PartitionEbx.EbxWriter(inStream);
    }

    public void WriteAsset(EbxAsset inAsset)
    {
        foreach (object ebxObj in inAsset.Objects)
        {
            ExtractType(ebxObj.GetType(), ebxObj);
            m_objs.Insert(0, ebxObj);
        }

        GenerateImportOrder();
        InternalWriteEbx(inAsset.PartitionGuid);
    }

    protected abstract void InternalWriteEbx(Guid inPartitionGuid);

    private void ExtractType(Type type, object obj, bool add = true)
    {
        if (typeof(IPrimitive).IsAssignableFrom(type))
        {
            // ignore primitive types
            return;
        }

        if (add && !m_processedObjects.Add(obj))
        {
            // dont get caught in a infinite loop
            return;
        }

        if (type.BaseType != s_objectType && type.BaseType != s_valueType)
        {
            ExtractType(type.BaseType!, obj, false);
        }

        if (add)
        {
            int hash = type.GetHashCode();
            if (!m_typesToProcessSet.Contains(hash))
            {
                m_typesToProcess.Add(type);
                m_typesToProcessSet.Add(hash);
            }
        }

        PropertyInfo[] ebxObjFields = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        foreach (PropertyInfo ebxField in ebxObjFields)
        {
            if (ebxField.GetCustomAttribute<IsTransientAttribute>() is not null)
            {
                // transient field, do not write
                continue;
            }

            ExtractField(ebxField.PropertyType, ebxField.GetValue(obj)!);
        }
    }

    private void ExtractField(Type type, object obj)
    {
        // pointerRefs
        if (type == s_pointerType)
        {
            PointerRef value = (PointerRef)obj;
            if (value.Type == PointerRefType.Internal)
            {
                ExtractType(value.Internal!.GetType(), value.Internal);
            }
            else if (value.Type == PointerRefType.External)
            {
                m_imports.Add(value.External);
            }
        }

        // structs
        else if (type.BaseType == s_valueType && type.Namespace!.StartsWith(s_ebxNamespace))
        {
            object structObj = obj;
            ExtractType(structObj.GetType(), structObj);
        }

        // arrays (stored as ObservableCollections in the sdk)
        else if (type.Name.Equals(s_collectionName))
        {
            Type arrayType = type;
            int count = (int)arrayType.GetMethod("get_Count")!.Invoke(obj, null)!;

            for (int arrayIter = 0; arrayIter < count; arrayIter++)
            {
                ExtractField(arrayType.GenericTypeArguments[0], arrayType.GetMethod("get_Item")!.Invoke(obj, new object[] { arrayIter })!);
            }
        }

        // boxed value refs
        else if (type == s_boxedValueRefType)
        {
            BoxedValueRef boxedValueRef = (BoxedValueRef)((IPrimitive)obj).ToActualType();

            if (boxedValueRef.Value is not null)
            {
                ExtractType(boxedValueRef.Value!.GetType(), boxedValueRef.Value);
            }
        }
    }

    private void GenerateImportOrder()
    {
        int iter = 0;
        foreach (EbxImportReference import in m_imports)
        {
            m_importOrderFw[import] = iter;
            m_importOrderBw[iter] = import;
            iter++;
        }
    }

    protected uint AddString(string stringToAdd)
    {
        if (stringToAdd == "")
        {
            return 0xFFFFFFFF;
        }

        uint offset = 0;
        if (m_strings.Contains(stringToAdd))
        {
            for (int i = 0; i < m_strings.Count; i++)
            {
                if (m_strings[i] == stringToAdd)
                {
                    break;
                }

                offset += (uint)(m_strings[i].Length + 1);
            }
        }
        else
        {
            offset = m_stringsLength;
            m_strings.Add(stringToAdd);
            m_stringsLength += (uint)(stringToAdd.Length + 1);
        }

        return offset;
    }
}