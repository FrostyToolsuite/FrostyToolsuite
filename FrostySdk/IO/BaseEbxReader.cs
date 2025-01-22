using System;
using System.Collections.Generic;
using Frosty.Sdk.Ebx;

namespace Frosty.Sdk.IO;

public abstract class BaseEbxReader
{
    protected readonly DataStream m_stream;
    protected readonly List<object?> m_objects = new();
    protected readonly List<int> m_refCounts = new();

    #region -- PrimitiveTypes --

    protected static readonly Type s_voidType = TypeLibrary.GetType("Void")!.Type;
    protected static readonly Type s_stringType = TypeLibrary.GetType("String")!.Type;
    protected static readonly Type s_sbyteType = TypeLibrary.GetType("Int8")!.Type;
    protected static readonly Type s_byteType = TypeLibrary.GetType("Uint8")?.Type ?? TypeLibrary.GetType("UInt8")!.Type;
    protected static readonly Type s_boolType = TypeLibrary.GetType("Boolean")!.Type;
    protected static readonly Type s_ushortType = TypeLibrary.GetType("Uint16")?.Type ?? TypeLibrary.GetType("UInt16")!.Type;
    protected static readonly Type s_shortType = TypeLibrary.GetType("Int16")!.Type;
    protected static readonly Type s_uintType = TypeLibrary.GetType("Uint32")?.Type ?? TypeLibrary.GetType("UInt32")!.Type;
    protected static readonly Type s_intType = TypeLibrary.GetType("Int32")!.Type;
    protected static readonly Type s_ulongType = TypeLibrary.GetType("Uint64")?.Type ?? TypeLibrary.GetType("UInt64")!.Type;
    protected static readonly Type s_longType = TypeLibrary.GetType("Int64")!.Type;
    protected static readonly Type s_floatType = TypeLibrary.GetType("Float32")!.Type;
    protected static readonly Type s_doubleType = TypeLibrary.GetType("Float64")!.Type;
    protected static readonly Type s_pointerType = typeof(PointerRef);
    protected static readonly Type s_guidType = TypeLibrary.GetType("Guid")!.Type;
    protected static readonly Type s_sha1Type = TypeLibrary.GetType("SHA1")!.Type;
    protected static readonly Type s_cStringType = TypeLibrary.GetType("CString")!.Type;
    protected static readonly Type s_resourceRefType = TypeLibrary.GetType("ResourceRef")!.Type;
    protected static readonly Type s_fileRefType = TypeLibrary.GetType("FileRef")!.Type;
    protected static readonly Type? s_typeRefType = TypeLibrary.GetType("TypeRef")?.Type;
    protected static readonly Type? s_boxedValueRefType = TypeLibrary.GetType("BoxedValueRef")?.Type;

    #endregion

    protected BaseEbxReader(DataStream inStream)
    {
        m_stream = inStream;
    }

    public static BaseEbxReader CreateReader(DataStream inStream)
    {
        return ProfilesLibrary.EbxVersion == 6 ? new RiffEbx.EbxReader(inStream) : new PartitionEbx.EbxReader(inStream);
    }

    public T ReadAsset<T>() where T : EbxAsset, new()
    {
        T asset = new();
        InternalReadObjects();

        for (int i = m_objects.Count - 1; i >= 0 ; i--)
        {
            if (m_objects[i] is not null)
            {
                continue;
            }

            // instance type is not in type info
            m_objects.RemoveAt(i);
            m_refCounts.RemoveAt(i);
        }

        asset.partitionGuid = GetPartitionGuid();
        asset.objects = m_objects!;
        asset.refCounts = m_refCounts;
        asset.dependencies = GetDependencies();
        asset.OnLoadComplete();

        return asset;
    }

    public abstract Guid GetPartitionGuid();

    public abstract string GetRootType();

    public abstract HashSet<Guid> GetDependencies();

    protected abstract void InternalReadObjects();
}