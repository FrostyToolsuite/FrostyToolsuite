using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO.Ebx;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO;

public abstract class BaseEbxWriter
{
    protected readonly DataStream m_stream;
    protected readonly List<object> m_objsSorted = new();

    protected static readonly Type s_pointerType = typeof(PointerRef);
    protected static readonly Type s_dataContainerType = TypeLibrary.GetType("DataContainer")!.Type;
    protected static readonly Type? s_boxedValueRefType = TypeLibrary.GetType("BoxedValueRef")?.Type;

    protected static readonly string s_ebxNamespace = "Frostbite";
    protected static readonly string s_collectionName = "ObservableCollection`1";

    protected uint m_stringsLength;
    protected readonly List<string> m_strings = new();

    protected readonly Dictionary<uint, int> m_typeToDescriptor = new();

    protected readonly HashSet<EbxImportReference> m_imports = new();
    protected readonly Dictionary<EbxImportReference, int> m_importOrderFw = new();

    protected readonly List<Block<byte>> m_arrayData = new();
    protected readonly List<Block<byte>> m_boxedValueData = new();

    protected readonly bool m_useSharedTypeDescriptors;

    private readonly HashSet<object> m_processedObjects = new();

    protected BaseEbxWriter(DataStream inStream)
    {
        m_stream = inStream;
        m_useSharedTypeDescriptors = FileSystemManager.HasFileInMemoryFs("SharedTypeDescriptors.ebx");
    }

    public static BaseEbxWriter CreateWriter(DataStream inStream)
    {
        return ProfilesLibrary.EbxVersion == 6 ? new RiffEbx.EbxWriter(inStream) : new PartitionEbx.EbxWriter(inStream);
    }

    public void WriteAsset(EbxAsset inAsset)
    {
        List<object> exportedObjs = new(inAsset.objects.Count);
        List<object> otherObjs = new(inAsset.objects.Count);
        List<object> rootObjects = new(1);

        int i = 0;
        foreach (dynamic obj in inAsset.objects)
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

            if (i++ != 0 && inAsset.refCounts[i - 1] == 0)
            {
                rootObjects.Add(obj);
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

        rootObjects.Sort((dynamic a, dynamic b) =>
        {
            AssetClassGuid guidA = a.GetInstanceGuid();
            AssetClassGuid guidB = b.GetInstanceGuid();

            byte[] bA = guidA.ExportedGuid.ToByteArray();
            byte[] bB = guidB.ExportedGuid.ToByteArray();

            uint idA = (uint)(bA[0] << 24 | bA[1] << 16 | bA[2] << 8 | bA[3]);
            uint idB = (uint)(bB[0] << 24 | bB[1] << 16 | bB[2] << 8 | bB[3]);

            return idA.CompareTo(idB);
        });

        otherObjs.Sort(CompareObjects);

        m_objsSorted.Add(root);
        m_objsSorted.AddRange(exportedObjs);
        m_objsSorted.AddRange(otherObjs);

        rootObjects.Insert(0, root);

        foreach (object ebxObj in rootObjects)
        {
            ProcessType(ebxObj.GetType(), ebxObj);
        }

        GenerateImportOrder();
        InternalWriteEbx(inAsset.PartitionGuid, exportedInstanceCount);
    }

    protected abstract void InternalWriteEbx(Guid inPartitionGuid, int inExportedInstanceCount);

    protected abstract int CompareObjects(object inA, object inB);

    protected abstract int AddType(Type inType);

    protected void ProcessType(Type inType, object inObj)
    {
        // make sure we dont add the same type multiple times
        bool addType = FindExistingType(inType) == -1;
        if (!m_processedObjects.Add(inObj))
        {
            return;
        }

        if (inType == s_pointerType)
        {
            PointerRef value = (PointerRef)inObj;

            if (value.Type == PointerRefType.External)
            {
                m_imports.Add(value.External);
            }
            else if (value.Type == PointerRefType.Internal && value.Internal is not null)
            {
                ProcessType(value.Internal.GetType(), value.Internal);
            }
        }
        else if (inType == s_boxedValueRefType)
        {
            BoxedValueRef boxedValueRef = (BoxedValueRef)((IPrimitive)inObj).ToActualType();

            if (boxedValueRef.Value is not null)
            {
                ProcessType(boxedValueRef.Value!.GetType(), boxedValueRef.Value);
            }
        }
        else if (inType.IsAssignableTo(typeof(IPrimitive)))
        {
            // skip other primitive types
        }
        else if (inType.IsEnum)
        {
            if (addType)
            {
                AddType(inType);
            }
        }
        else if (inType.Name.Equals(s_collectionName))
        {
            IList list = (IList)inObj;

            foreach (object o in list)
            {
                ProcessType(o.GetType(), o);
            }

            if (addType)
            {
                AddType(inType);
            }
        }
        else if (inType.IsClass)
        {
            if (addType)
            {
                AddType(inType);
            }

            PropertyInfo[] allProps = inType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo pi in allProps)
            {
                // ignore transients if saving to project
                if (pi.GetCustomAttribute<IsTransientAttribute>() is not null)
                {
                    continue;
                }

                ProcessType(pi.PropertyType, pi.GetValue(inObj)!);
            }
        }
        else if (inType.IsValueType)
        {
            if (addType)
            {
                AddType(inType);
            }

            PropertyInfo[] allProps = inType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (PropertyInfo pi in allProps)
            {
                // ignore transients if saving to project
                if (pi.GetCustomAttribute<IsTransientAttribute>() is not null)
                {
                    continue;
                }

                ProcessType(pi.PropertyType, pi.GetValue(inObj)!);
            }
        }
    }

    private void GenerateImportOrder()
    {
        int iter = 0;
        foreach (EbxImportReference import in m_imports)
        {
            m_importOrderFw[import] = iter;
            iter++;
        }
    }

    protected uint AddString(string stringToAdd)
    {
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
        uint hash = m_useSharedTypeDescriptors ? inType.GetNameHash() : (uint)Utils.Utils.HashString(inType.GetName());

        return m_typeToDescriptor.GetValueOrDefault(hash, -1);
    }

    protected int FindExistingType(IType inType)
    {
        uint hash = m_useSharedTypeDescriptors ? inType.NameHash : (uint)Utils.Utils.HashString(inType.Name);

        return m_typeToDescriptor.GetValueOrDefault(hash, -1);
    }
}