using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Frosty.Sdk.IO.RiffEbx;

internal class EbxTypeResolver
{
    private readonly IList<Guid> m_typeGuids;
    private readonly IList<uint> m_typeSignatures;

    internal EbxTypeResolver(IList<Guid> inTypeGuids, IList<uint> inTypeSignatures)
    {
        EbxSharedTypeDescriptors.Initialize();
        m_typeGuids = inTypeGuids;
        m_typeSignatures = inTypeSignatures;
    }

    public EbxTypeDescriptor ResolveType(int index)
    {
        return EbxSharedTypeDescriptors.GetTypeDescriptor(m_typeGuids[index], m_typeSignatures[index]);
    }

    public EbxTypeDescriptor ResolveTypeFromField(ushort inTypeDescriptorRef)
    {
        return EbxSharedTypeDescriptors.GetTypeDescriptor(inTypeDescriptorRef);
    }

    public EbxFieldDescriptor ResolveField(int index)
    {
        return EbxSharedTypeDescriptors.GetFieldDescriptors(index);
    }
}