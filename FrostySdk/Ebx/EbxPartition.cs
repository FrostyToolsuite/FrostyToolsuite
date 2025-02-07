using System;
using System.Collections.Generic;
using System.Linq;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;

namespace Frosty.Sdk.Ebx;

public class EbxPartition
{
    public Guid PartitionGuid => partitionGuid;

    public Guid PrimaryInstanceGuid
    {
        get
        {
            AssetClassGuid guid = PrimaryInstance.GetInstanceGuid();
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
    public IEnumerable<IEbxInstance> RootInstances => instances.Where((_, i) => refCounts[i] == 0 || i == 0);

    public IEnumerable<IEbxInstance> Instances
    {
        get
        {
            foreach (IEbxInstance obj in instances)
            {
                yield return obj;
            }
        }
    }
    public IEnumerable<IEbxInstance> ExportedObjects
    {
        get
        {
            for (int i = 0; i < instances.Count; i++)
            {
                IEbxInstance obj = instances[i];
                AssetClassGuid guid = obj.GetInstanceGuid();
                if (guid.IsExported)
                {
                    yield return obj;
                }
            }
        }
    }
    public IEbxInstance PrimaryInstance => instances[0];

    public bool IsValid => instances.Count != 0;

    internal Guid partitionGuid;
    internal List<IEbxInstance> instances = new();
    internal List<int> refCounts = new();
    internal HashSet<Guid> dependencies = new();

    public static EbxPartition Deserialize(DataStream ebxStream)
    {
        BaseEbxReader reader = BaseEbxReader.CreateReader(ebxStream);
        return reader.ReadPartition<EbxPartition>();
    }

    public static void Serialize(DataStream ebxStream, EbxPartition inPartition)
    {
        BaseEbxWriter writer = BaseEbxWriter.CreateWriter(ebxStream);
        writer.WritePartition(inPartition);
    }

    public EbxPartition()
    {
    }

    public EbxPartition(params IEbxInstance[] rootObjects)
    {
        partitionGuid = Guid.NewGuid();

        foreach (IEbxInstance obj in rootObjects)
        {
            obj.SetInstanceGuid(new AssetClassGuid(Guid.NewGuid(), instances.Count));
            instances.Add(obj);
        }
    }

    /// <summary>
    /// Invoked when loading of the ebx asset has completed, to allow for any custom handling
    /// </summary>
    public virtual void OnLoadComplete()
    {
    }

    public IEbxInstance? GetObject(Guid guid)
    {
        foreach (IEbxInstance obj in ExportedObjects)
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
        if (!dependencies.Add(guid))
        {
            return false;
        }

        return true;
    }

    public void SetFileGuid(Guid guid) => partitionGuid = guid;

    public void AddObject(IEbxInstance obj)
    {
        AssetClassGuid guid = obj.GetInstanceGuid();
        if (guid.InternalId == -1)
        {
            // make sure internal id is set before adding
            guid = new AssetClassGuid(guid.ExportedGuid, instances.Count);
            obj.SetInstanceGuid(guid);
        }

        instances.Add(obj);
    }

    public void RemoveObject(IEbxInstance obj)
    {
        int idx = instances.IndexOf(obj);
        if (idx == -1)
        {
            return;
        }

        instances.RemoveAt(idx);
    }
}