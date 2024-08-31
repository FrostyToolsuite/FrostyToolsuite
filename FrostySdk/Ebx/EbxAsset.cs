using System;
using System.Collections.Generic;
using System.Reflection;
using Frosty.Sdk.IO;

namespace Frosty.Sdk.Ebx;

public class EbxAsset
{
    public Guid PartitionGuid => partitionGuid;
    public Guid RootInstanceGuid
    {
        get
        {
            AssetClassGuid guid = ((dynamic)RootObject).GetInstanceGuid();
            return guid.ExportedGuid;
        }
    }

    public IEnumerable<Guid> Dependencies
    {
        get
        {
            foreach (Guid dependency in dependencies)
            {
                yield return dependency;
            }
        }
    }
    public IEnumerable<object> RootObjects
    {
        get
        {
            for (int i = 0; i < objects.Count; i++)
            {
                if (refCounts[i] == 0 || i == 0)
                {
                    yield return objects[i];
                }
            }
        }
    }
    public IEnumerable<object> Objects
    {
        get
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                yield return objects[i];
            }
        }
    }
    public IEnumerable<object> ExportedObjects
    {
        get
        {
            for (int i = 0; i < objects.Count; i++)
            {
                dynamic obj = objects[i];
                AssetClassGuid guid = obj.GetInstanceGuid();
                if (guid.IsExported)
                {
                    yield return obj;
                }
            }
        }
    }
    public object RootObject => objects[0];
    public bool IsValid => objects.Count != 0;
    public bool TransientEdit { get; set; }

    internal Guid partitionGuid;
    internal List<object> objects = new();
    internal List<int> refCounts = new();
    internal HashSet<Guid> dependencies = new();

    public static EbxAsset Deserialize(DataStream ebxStream)
    {
        BaseEbxReader reader = BaseEbxReader.CreateReader(ebxStream);
        return reader.ReadAsset<EbxAsset>();
    }

    public static void Serialize(DataStream ebxStream, EbxAsset asset)
    {
        BaseEbxWriter writer = BaseEbxWriter.CreateWriter(ebxStream);
        writer.WriteAsset(asset);
    }

    public EbxAsset()
    {
    }

    public EbxAsset(params object[] rootObjects)
    {
        partitionGuid = Guid.NewGuid();

        foreach (dynamic obj in rootObjects)
        {
            obj.SetInstanceGuid(new AssetClassGuid(Guid.NewGuid(), objects.Count));
            objects.Add(obj);
        }
    }

    /// <summary>
    /// Invoked when loading of the ebx asset has completed, to allow for any custom handling
    /// </summary>
    public virtual void OnLoadComplete()
    {
    }

    public dynamic? GetObject(Guid guid)
    {
        foreach (dynamic obj in ExportedObjects)
        {
            if (obj.GetInstanceGuid() == guid)
            {
                return obj;
            }
        }
        return null;
    }

    public bool AddDependency(Guid guid)
    {
        if (dependencies.Contains(guid))
        {
            return false;
        }

        dependencies.Add(guid);
        return true;
    }

    public void SetFileGuid(Guid guid) => partitionGuid = guid;

    public void AddObject(dynamic obj, bool root = false)
    {
        AssetClassGuid guid = obj.GetInstanceGuid();
        if (guid.InternalId == -1)
        {
            // make sure internal id is set before adding
            guid = new AssetClassGuid(guid.ExportedGuid, objects.Count);
            obj.SetInstanceGuid(guid);
        }

        objects.Add(obj);
    }

    public void RemoveObject(object obj)
    {
        int idx = objects.IndexOf(obj);
        if (idx == -1)
        {
            return;
        }

        objects.RemoveAt(idx);
    }

    public void Update()
    {
        dependencies.Clear();

        List<Tuple<PropertyInfo, object>> refProps = new();
        List<Tuple<object, Guid>> externalProps = new();
        List<object> objsToProcess = new();

        // count refs for all pointers
        objsToProcess.AddRange(objects);

        while (objsToProcess.Count > 0)
        {
            CountRefs(objsToProcess[0], objsToProcess[0], ref refProps, ref externalProps);
            objsToProcess.RemoveAt(0);
        }

        foreach (var externalProp in externalProps)
        {
            if (objects.Contains(externalProp.Item1))
            {
                dependencies.Add(externalProp.Item2);
            }
        }

        // check for invalid references, and clear them
        foreach (Tuple<PropertyInfo, object> refProp in refProps)
        {
            Type pType = refProp.Item1.PropertyType;
            if (pType == s_pointerType)
            {
                PointerRef pr = (PointerRef)refProp.Item1.GetValue(refProp.Item2)!;
                if (!objects.Contains(pr.Internal!))
                {
                    refProp.Item1.SetValue(refProp.Item2, new PointerRef());
                }
            }
            else
            {
                System.Collections.IList list = (System.Collections.IList)refProp.Item1.GetValue(refProp.Item2)!;
                int count = list.Count;
                bool requiresChange = false;

                for (int i = 0; i < count; i++)
                {
                    PointerRef pr = (PointerRef)list[i]!;
                    if (pr.Type == PointerRefType.Internal)
                    {
                        if (!objects.Contains(pr.Internal!))
                        {
                            list[i] = new PointerRef();
                            requiresChange = true;
                        }
                    }
                }

                if (requiresChange)
                {
                    refProp.Item1.SetValue(refProp.Item2, list);
                }
            }
        }
    }

    private static readonly Type s_pointerType = typeof(PointerRef);
    private static readonly Type s_valueType = typeof(ValueType);
    private static readonly Type s_boxedValueType = typeof(BoxedValueRef);

    private void CountRefs(object obj, object classObj, ref List<Tuple<PropertyInfo, object>> refProps, ref List<Tuple<object, Guid>> externalProps)
    {
        PropertyInfo[] pis = obj.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (PropertyInfo pi in pis)
        {
            if (pi.PropertyType.IsPrimitive)
            {
                continue;
            }

            if (pi.PropertyType.IsEnum)
            {
                continue;
            }

            Type pType = pi.PropertyType;

            // Pointers
            if (pType == s_pointerType)
            {
                PointerRef pr = (PointerRef)pi.GetValue(obj)!;
                if (pr.Type == PointerRefType.Internal)
                {
                    // collect reference for later checking
                    refProps.Add(new Tuple<PropertyInfo, object>(pi, obj));
                }
                else if (pr.Type == PointerRefType.External)
                {
                    externalProps.Add(new Tuple<object, Guid>(classObj, pr.External.FileGuid));
                }
            }

            // Arrays
            else if (pType.GenericTypeArguments.Length != 0)
            {
                Type arrayType = pType.GenericTypeArguments[0];

                System.Collections.IList list = (System.Collections.IList)pi.GetValue(obj)!;
                int count = list.Count;

                if (count > 0)
                {
                    // Pointer Array
                    if (arrayType == s_pointerType)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            List<PointerRef> plist = (List<PointerRef>)list;
                            PointerRef pr = plist[i];

                            if (pr.Type == PointerRefType.Internal)
                            {
                                refProps.Add(new Tuple<PropertyInfo, object>(pi, obj));
                            }
                            else if (pr.Type == PointerRefType.External)
                            {
                                externalProps.Add(new Tuple<object, Guid>(classObj, pr.External.FileGuid));
                            }
                        }
                    }

                    // Structure Array
                    else if (arrayType != s_valueType)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            CountRefs(list[i]!, classObj, ref refProps, ref externalProps);
                        }
                    }
                }
            }

            else if (pType == s_boxedValueType)
            {
                BoxedValueRef boxedValue = (BoxedValueRef)pi.GetValue(obj)!;
                if (boxedValue.Value is not null)
                {
                    CountRefs(boxedValue.Value, classObj, ref refProps, ref externalProps);
                }
            }

            // Structures
            else if (pType != s_valueType)
            {
                CountRefs(pi.GetValue(obj)!, classObj, ref refProps, ref externalProps);
            }
        }
    }
}