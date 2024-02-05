namespace Frosty.Sdk.IO.PartitionEbx;

internal class EbxTypeResolver
{
    private readonly EbxTypeDescriptor[] m_typeDescriptors;
    private readonly EbxFieldDescriptor[] m_fieldDescriptors;

    internal EbxTypeResolver(EbxTypeDescriptor[] inTypeDescriptors, EbxFieldDescriptor[] inFieldDescriptors)
    {
        EbxSharedTypeDescriptors.Initialize();
        m_typeDescriptors = inTypeDescriptors;
        m_fieldDescriptors = inFieldDescriptors;
    }

    public EbxTypeDescriptor ResolveType(int index)
    {
        EbxTypeDescriptor typeDescriptor = m_typeDescriptors[index];
        if (typeDescriptor.IsSharedTypeDescriptorKey())
        {
            return EbxSharedTypeDescriptors.GetTypeDescriptor(typeDescriptor.ToKey());
        }

        return typeDescriptor;
    }

    public EbxTypeDescriptor ResolveType(EbxTypeDescriptor typeDescriptor, int index)
    {
        if (typeDescriptor.Index != -1)
        {
            return EbxSharedTypeDescriptors.GetTypeDescriptor((short)(index + typeDescriptor.Index));
        }

        return m_typeDescriptors[index];
    }

    public EbxFieldDescriptor ResolveField(int index)
    {
        if (m_fieldDescriptors.Length == 0)
        {
            return EbxSharedTypeDescriptors.GetFieldDescriptor(index);
        }

        return m_fieldDescriptors[index];
    }
}