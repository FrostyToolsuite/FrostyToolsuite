using System;

namespace Frosty.Sdk.IO.RiffEbx;

internal class EbxTypeResolver
{
    private readonly Guid[] m_typeGuids;
    private readonly uint[] m_typeSignatures;

    internal EbxTypeResolver(Guid[] inTypeGuids, uint[] inTypeSignatures)
    {
        EbxSharedTypeDescriptors.Initialize();
        m_typeGuids = inTypeGuids;
        m_typeSignatures = inTypeSignatures;
    }

    public EbxTypeDescriptor ResolveType(int index)
    {
        return EbxSharedTypeDescriptors.GetTypeDescriptor(m_typeGuids[index], m_typeSignatures[index]);
    }

    public EbxTypeDescriptor ResolveType(ushort inTypeDescriptorRef)
    {
        return EbxSharedTypeDescriptors.GetTypeDescriptor(inTypeDescriptorRef);
    }

    public EbxFieldDescriptor ResolveField(int index)
    {
        return EbxSharedTypeDescriptors.GetFieldDescriptors(index);
    }
}