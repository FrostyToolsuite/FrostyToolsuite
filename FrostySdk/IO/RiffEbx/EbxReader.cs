using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.Ebx;

namespace Frosty.Sdk.IO.RiffEbx;

public class EbxReader : BaseEbxReader
{
    private RiffStream m_riffStream;
    private uint m_size;

    private long m_payloadOffset;
    private EbxFixup m_fixup;

    public EbxReader(DataStream inStream)
        : base(inStream)
    {
        EbxSharedTypeDescriptors.Initialize();
        m_riffStream = new RiffStream(m_stream);
        m_riffStream.ReadHeader(out FourCC fourCc, out m_size);

        if (fourCc != "EBX\x0" && fourCc != "EBXS")
        {
            throw new InvalidDataException("Not a valid ebx chunk");
        }

        // read EBXD chunk
        m_riffStream.ReadNextChunk(ref m_size, ProcessChunk);
        // read EFIX chunk
        m_riffStream.ReadNextChunk(ref m_size, ProcessChunk);
        // read EBXX chunk
        m_riffStream.ReadNextChunk(ref m_size, ProcessChunk);
    }

    public override Guid GetPartitionGuid() => m_fixup.PartitionGuid;

    public override string GetRootType()
    {
        m_stream.Position = m_payloadOffset + m_fixup.InstanceOffsets[0];
        return TypeLibrary.GetType(m_fixup.TypeGuids[m_stream.ReadUInt16()])?.GetCustomAttribute<DisplayNameAttribute>()?.Name ?? string.Empty;
    }

    public override HashSet<Guid> GetDependencies() => m_fixup.Dependencies;

    private void ProcessChunk(DataStream inStream, FourCC inFourCc, uint inSize)
    {
        switch ((string)inFourCc)
        {
            case "EBXD":
                ReadDataChunk(inStream);
                break;
            case "EFIX":
                ReadFixupChunk(inStream);
                break;
            case "EBXX":
                ReadXChunk(inStream);
                break;
        }
    }

    protected override void InternalReadObjects()
    {
        ushort[] typeRefs = new ushort[m_fixup.InstanceOffsets.Length];
        int j = 0;
        foreach (uint offset in m_fixup.InstanceOffsets)
        {
            m_stream.Position = m_payloadOffset + offset;
            ushort typeRef = m_stream.ReadUInt16();
            typeRefs[j++] = typeRef;

            m_objects.Add(TypeLibrary.CreateObject(m_fixup.TypeGuids[typeRef]) ?? throw new Exception());
            m_refCounts.Add(0);
        }

        m_stream.Position = m_payloadOffset;

        for (int i = 0; i < m_fixup.InstanceOffsets.Length; i++)
        {
            ushort typeRef = typeRefs[i];

            EbxTypeDescriptor typeDescriptor =
                EbxSharedTypeDescriptors.GetTypeDescriptor(m_fixup.TypeGuids[typeRef], m_fixup.TypeSignatures[typeRef]);

            m_stream.Pad(typeDescriptor.Alignment);

            Guid instanceGuid = i < m_fixup.ExportedInstanceCount ? m_stream.ReadGuid(): Guid.Empty;

            if (typeDescriptor.Alignment != 0x04)
            {
                m_stream.Position += 8;
            }

            object obj = m_objects[i];
            ((dynamic)obj).SetInstanceGuid(new AssetClassGuid(instanceGuid, i));
            ReadType(typeDescriptor, obj, m_stream.Position - 8);
        }
    }

    private void ReadFixupChunk(DataStream inStream)
    {
        m_fixup = EbxFixup.ReadFixup(inStream);
    }

    private void ReadDataChunk(DataStream inStream)
    {
        inStream.Pad(16);
        m_payloadOffset = inStream.Position;
    }

    private void ReadXChunk(DataStream inStream)
    {
        // TODO: no idea if we need to do sth here, its stores array and boxedvalueref data iirc
    }

    private void ReadType(EbxTypeDescriptor inTypeDescriptor, object? obj, long inStartOffset)
    {
        throw new NotImplementedException();
    }
}