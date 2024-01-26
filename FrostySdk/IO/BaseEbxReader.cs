using System;
using System.Collections.Generic;
using Frosty.Sdk.Ebx;

namespace Frosty.Sdk.IO;

public abstract class BaseEbxReader
{
    protected readonly DataStream m_stream;
    protected readonly List<object> m_objects = new();
    protected readonly List<int> m_refCounts = new();

    #region -- PrimitiveTypes --

    protected static readonly Type s_stringType = TypeLibrary.GetType("String")!;
    protected static readonly Type s_sbyteType = TypeLibrary.GetType("Int8")!;
    protected static readonly Type s_byteType = TypeLibrary.GetType("Uint8") ?? TypeLibrary.GetType("UInt8")!;
    protected static readonly Type s_boolType = TypeLibrary.GetType("Boolean")!;
    protected static readonly Type s_ushortType = TypeLibrary.GetType("Uint16") ?? TypeLibrary.GetType("UInt16")!;
    protected static readonly Type s_shortType = TypeLibrary.GetType("Int16")!;
    protected static readonly Type s_uintType = TypeLibrary.GetType("Uint32") ?? TypeLibrary.GetType("UInt32")!;
    protected static readonly Type s_intType = TypeLibrary.GetType("Int32")!;
    protected static readonly Type s_ulongType = TypeLibrary.GetType("Uint64") ?? TypeLibrary.GetType("UInt64")!;
    protected static readonly Type s_longType = TypeLibrary.GetType("Int64")!;
    protected static readonly Type s_floatType = TypeLibrary.GetType("Float32")!;
    protected static readonly Type s_doubleType = TypeLibrary.GetType("Float64")!;
    protected static readonly Type s_pointerType = typeof(PointerRef);
    protected static readonly Type s_guidType = TypeLibrary.GetType("Guid")!;
    protected static readonly Type s_sha1Type = TypeLibrary.GetType("SHA1")!;
    protected static readonly Type s_cStringType = TypeLibrary.GetType("CString")!;
    protected static readonly Type s_resourceRefType = TypeLibrary.GetType("ResourceRef")!;
    protected static readonly Type s_fileRefType = TypeLibrary.GetType("FileRef")!;
    protected static readonly Type? s_typeRefType = TypeLibrary.GetType("TypeRef");
    protected static readonly Type? s_boxedValueRefType = TypeLibrary.GetType("BoxedValueRef");

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

        asset.partitionGuid = GetPartitionGuid();
        asset.objects = m_objects;
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