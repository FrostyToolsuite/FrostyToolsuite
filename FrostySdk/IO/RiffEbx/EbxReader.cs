using System;
using System.Collections.Generic;

namespace Frosty.Sdk.IO.RiffEbx;

public class EbxReader : BaseEbxReader
{
    public EbxReader(DataStream inStream)
        : base(inStream)
    {
    }

    public override Guid GetPartitionGuid()
    {
        throw new NotImplementedException();
    }

    public override string GetRootType()
    {
        throw new NotImplementedException();
    }

    public override HashSet<Guid> GetDependencies()
    {
        throw new NotImplementedException();
    }

    protected override void InternalReadObjects()
    {
        throw new NotImplementedException();
    }
}