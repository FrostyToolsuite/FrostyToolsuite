using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO.Ebx;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO;

public abstract class BaseEbxWriter
{
    protected readonly DataStream m_stream;
    protected readonly List<object> m_objsSorted = new();

    protected static readonly Type s_pointerType = typeof(PointerRef);
    protected static readonly Type s_valueType = typeof(ValueType);
    protected static readonly Type s_objectType = typeof(object);
    protected static readonly Type s_dataContainerType = TypeLibrary.GetType("DataContainer")!;
    protected static readonly Type? s_typeRefType = TypeLibrary.GetType("TypeRef");
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

    //protected readonly List<Block<byte>> m_arrayData = new();
    protected Block<byte>? m_arrayData;
    protected DataStream? m_arrayWriter;
    protected Block<byte>? m_boxedValueData;
    protected DataStream? m_boxedValueWriter;

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
        List<object> exportedObjs = new(inAsset.objects.Count);
        List<object> otherObjs = new(inAsset.objects.Count);

        foreach (dynamic obj in inAsset.Objects)
        {
            AssetClassGuid guid = obj.GetInstanceGuid();
            if (guid.IsExported)
            {
                exportedObjs.Add(obj);
            }
            else
            {
                otherObjs.Add(obj);
            }
        }

        int exportedInstanceCount = exportedObjs.Count;
        object root = exportedObjs[0];
        exportedObjs.RemoveAt(0);

        exportedObjs.Sort((dynamic a, dynamic b) =>
        {
            AssetClassGuid guidA = a.GetInstanceGuid();
            AssetClassGuid guidB = b.GetInstanceGuid();

            byte[] bA = guidA.ExportedGuid.ToByteArray();
            byte[] bB = guidB.ExportedGuid.ToByteArray();

            uint idA = (uint)(bA[0] << 24 | bA[1] << 16 | bA[2] << 8 | bA[3]);
            uint idB = (uint)(bB[0] << 24 | bB[1] << 16 | bB[2] << 8 | bB[3]);

            return idA.CompareTo(idB);
        });

        otherObjs.Sort((a, b) =>
        {
            byte[] bA = a.GetType().GetGuid().ToByteArray();
            byte[] bB = b.GetType().GetGuid().ToByteArray();

            uint idA = (uint)(bA[0] << 24 | bA[1] << 16 | bA[2] << 8 | bA[3]);
            uint idB = (uint)(bB[0] << 24 | bB[1] << 16 | bB[2] << 8 | bB[3]);

            return idA.CompareTo(idB);
        });

        m_objsSorted.Add(root);
        m_objsSorted.AddRange(exportedObjs);
        m_objsSorted.AddRange(otherObjs);
        foreach (object ebxObj in m_objsSorted)
        {
            ExtractType(ebxObj.GetType(), ebxObj);
        }

        GenerateImportOrder();
        InternalWriteEbx(inAsset.PartitionGuid, exportedInstanceCount);
    }

    protected abstract void InternalWriteEbx(Guid inPartitionGuid, int inExportedInstanceCount);

    protected void ExtractType(Type type, object obj, bool add = true)
    {
        if (typeof(IPrimitive).IsAssignableFrom(type))
        {
            // ignore primitive types
            return;
        }

        if (typeof(IDelegate).IsAssignableFrom(type))
        {
            IDelegate @delegate = (IDelegate)obj;

            if (@delegate.FunctionType is not null && m_typesToProcessSet.Add(@delegate.FunctionType.GetHashCode()))
            {
                m_typesToProcess.Add(@delegate.FunctionType);
            }
        }

        if (type == s_pointerType)
        {
            PointerRef value = (PointerRef)obj;
            if (value.Type == PointerRefType.Internal)
            {
                //ExtractType(value.Internal!.GetType(), value.Internal);
            }
            else if (value.Type == PointerRefType.External)
            {
                m_imports.Add(value.External);
            }
        }

        // arrays (stored as ObservableCollections in the sdk)
        else if (type.Name.Equals(s_collectionName))
        {
            IList list = (IList)obj;

            foreach (object o in list)
            {
                ExtractType(o.GetType(), o);
            }
        }

        // type refs
        else if (type == s_typeRefType)
        {
            TypeRef typeRef = (TypeRef)((IPrimitive)obj).ToActualType();

            if (typeRef.Type is not null && m_typesToProcessSet.Add(typeRef.Type.GetHashCode()))
            {
                m_typesToProcess.Add(typeRef.Type);
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

        // structs
        else if (type.IsValueType)
        {
            if (add && m_typesToProcessSet.Add(type.GetHashCode()))
            {
                m_typesToProcess.Add(type);
            }

            PropertyInfo[] ebxObjFields = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

            for (int i = ebxObjFields.Length - 1; i >= 0 ; i--)
            {
                PropertyInfo ebxField = ebxObjFields[i];
                if (ebxField.GetCustomAttribute<IsTransientAttribute>() is not null)
                {
                    // transient field, do not write
                    continue;
                }

                ExtractType(ebxField.PropertyType, ebxField.GetValue(obj)!, false);
            }
        }

        // structs
        else if (type.IsClass)
        {
            if (add && m_typesToProcessSet.Add(type.GetHashCode()))
            {
                m_typesToProcess.Add(type);
            }

            PropertyInfo[] ebxObjFields = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

            for (int i = ebxObjFields.Length - 1; i >= 0 ; i--)
            {
                PropertyInfo ebxField = ebxObjFields[i];
                if (ebxField.GetCustomAttribute<IsTransientAttribute>() is not null)
                {
                    // transient field, do not write
                    continue;
                }

                ExtractType(ebxField.PropertyType, ebxField.GetValue(obj)!, false);
            }

            if (type.BaseType!.Namespace!.StartsWith(s_ebxNamespace))
            {
                ExtractType(type.BaseType!, obj, false);
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
        // TODO: check if this breaks non riff ebx
        //if (string.IsNullOrEmpty(stringToAdd))
        {
            //return 0xFFFFFFFF;
        }

        uint offset = 0;
        if (m_strings.Contains(stringToAdd))
        {
            foreach (string s in m_strings)
            {
                if (s == stringToAdd)
                {
                    break;
                }
                offset += (uint)(Encoding.UTF8.GetByteCount(s) + 1);
            }
        }
        else
        {
            offset = m_stringsLength;
            m_strings.Add(stringToAdd);
            m_stringsLength += (uint)(Encoding.UTF8.GetByteCount(stringToAdd) + 1);
        }

        return offset;
    }

    protected int FindExistingType(Type inType)
    {
        uint hash;
        if (inType.Name.Equals(s_collectionName))
        {
            Type elementType = inType.GenericTypeArguments[0].Name == "PointerRef" ? s_dataContainerType : inType.GenericTypeArguments[0];

            hash = elementType.GetCustomAttribute<ArrayHashAttribute>()!.Hash;
        }
        else
        {
            hash = inType.GetCustomAttribute<NameHashAttribute>()!.Hash;
        }

        return m_typeToDescriptor.GetValueOrDefault(hash, -1);
    }
}