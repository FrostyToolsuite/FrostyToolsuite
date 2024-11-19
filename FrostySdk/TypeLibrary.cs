using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;

namespace Frosty.Sdk;

public static class TypeLibrary
{
    public static bool IsInitialized { get; private set; }

    private static readonly Dictionary<string, int> s_nameMapping = new();
    private static readonly Dictionary<uint, int> s_nameHashMapping = new();
    private static readonly Dictionary<Guid, int> s_guidMapping = new();
    private static readonly List<SdkType> s_types = [];
    private static readonly List<TypeInfoAsset> s_typeInfoAssets = [];

    public static bool Initialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        FileInfo fileInfo = new(ProfilesLibrary.SdkPath);
        if (!fileInfo.Exists)
        {
            return false;
        }

        Assembly sdk = Assembly.LoadFrom(fileInfo.FullName);

        if ((sdk.GetCustomAttribute<SdkVersionAttribute>()?.Head ?? 0) != FileSystemManager.Head)
        {
            FrostyLogger.Logger?.LogInfo("Outdated Type Sdk, please regenerate it to avoid issues");
        }

        Type[] types = sdk.GetExportedTypes();

        foreach (Type type in types)
        {
            NameHashAttribute? nameHashAttribute = type.GetCustomAttribute<NameHashAttribute>();
            if (nameHashAttribute is null)
            {
                // issue described in #25 we are just ignoring these cases
                continue;
            }
            uint nameHash = nameHashAttribute.Hash;
            string name = type.GetName();
            Guid? guid = type.GetCustomAttribute<GuidAttribute>()?.Guid;

            s_nameMapping.Add(name, s_types.Count);
            s_nameHashMapping.Add(nameHash, s_types.Count);
            if (guid.HasValue)
            {
                s_guidMapping.Add(guid.Value, s_types.Count);
            }
            s_types.Add(new SdkType(type));
            Guid? arrayGuid = type.GetCustomAttribute<ArrayGuidAttribute>()?.Guid;
            if (arrayGuid.HasValue)
            {
                s_guidMapping.Add(arrayGuid.Value, s_types.Count);
                s_types.Add(new SdkType(typeof(ObservableCollection<>).MakeGenericType(type)));
            }
        }

        IsInitialized = true;
        return true;
    }

    public static void AddTypeInfoAsset(Guid inGuid, object inTypeInfoAsset)
    {
        TypeInfoAsset type = new(inGuid, inTypeInfoAsset);
        const int flag = 1 << 31;
        int index = s_typeInfoAssets.Count | flag;
        s_nameMapping.Add(type.Name, index);
        s_guidMapping.Add(type.Guid, index);
        s_nameHashMapping.Add(type.NameHash, index);
        s_typeInfoAssets.Add(type);
    }

    public static IEnumerable<IType> EnumerateTypes() => s_types;

    public static IType? GetType(string name)
    {
        if (!s_nameMapping.TryGetValue(name, out int index))
        {
            return null;
        }

        const int flag = 1 << 31;

        return (index & flag) != 0 ? s_typeInfoAssets[index & ~flag] : s_types[index];
    }

    public static IType? GetType(uint nameHash)
    {
        if (!s_nameHashMapping.TryGetValue(nameHash, out int index))
        {
            return null;
        }

        const int flag = 1 << 31;

        return (index & flag) != 0 ? s_typeInfoAssets[index & ~flag] : s_types[index];
    }

    public static IType? GetType(Guid guid)
    {
        if (!s_guidMapping.TryGetValue(guid, out int index))
        {
            return null;
        }

        const int flag = 1 << 31;

        return (index & flag) != 0 ? s_typeInfoAssets[index & ~flag] : s_types[index];
    }

    public static object? CreateObject(string name)
    {
        IType? type = GetType(name);
        return type is null ? null : Activator.CreateInstance(type.Type);
    }

    public static object? CreateObject(uint nameHash)
    {
        IType? type = GetType(nameHash);
        return type is null ? null : Activator.CreateInstance(type.Type);
    }

    public static object? CreateObject(Guid guid)
    {
        IType? type = GetType(guid);
        return type is null ? null : Activator.CreateInstance(type.Type);
    }

    public static bool IsSubClassOf(IType type, string inB)
    {
        IType? checkType = GetType(inB);
        if (checkType is null)
        {
            return false;
        }

        return type.IsSubClassOf(checkType) || checkType.Name == type.Name;
    }

    /// <summary>
    /// Determines if type a derives from type b
    /// </summary>
    /// <param name="inA"></param>
    /// <param name="inB"></param>
    /// <returns></returns>
    public static bool IsSubClassOf(string inA, string inB)
    {
        IType? sourceType = GetType(inA);
        IType? type = GetType(inB);

        return sourceType is not null && type is not null &&
               (sourceType.IsSubClassOf(type) || sourceType.Name == type.Name);
    }

    public static string GetName(this MemberInfo type)
    {
        if (type.Name == "ObservableCollection`1")
        {
            Type elementType = (type as Type)!.GenericTypeArguments[0].Name == "PointerRef" ? GetType("DataContainer")!.Type : (type as Type)!.GenericTypeArguments[0];

            return elementType.GetCustomAttribute<ArrayNameAttribute>()?.Name ?? type.Name + "-Array";
        }
        return type.GetCustomAttribute<DisplayNameAttribute>()?.Name ?? type.Name;
    }

    public static Guid GetGuid(this MemberInfo type)
    {
        if (type.Name == "ObservableCollection`1")
        {
            Type elementType = (type as Type)!.GenericTypeArguments[0].Name == "PointerRef" ? GetType("DataContainer")!.Type : (type as Type)!.GenericTypeArguments[0];

            return elementType.GetCustomAttribute<ArrayGuidAttribute>()?.Guid ??  Guid.Empty;
        }
        return type.GetCustomAttribute<GuidAttribute>()?.Guid ?? Guid.Empty;
    }

    public static uint GetSignature(this MemberInfo type)
    {
        if (type.Name == "ObservableCollection`1")
        {
            Type elementType = (type as Type)!.GenericTypeArguments[0].Name == "PointerRef" ? GetType("DataContainer")!.Type : (type as Type)!.GenericTypeArguments[0];

            return elementType.GetCustomAttribute<SignatureAttribute>()?.Signature ?? uint.MaxValue;
        }
        return type.GetCustomAttribute<SignatureAttribute>()?.Signature ?? uint.MaxValue;
    }

    public static uint GetNameHash(this MemberInfo type)
    {
        if (type.Name == "ObservableCollection`1")
        {
            Type elementType = (type as Type)!.GenericTypeArguments[0].Name == "PointerRef" ? GetType("DataContainer")!.Type : (type as Type)!.GenericTypeArguments[0];

            return elementType.GetCustomAttribute<ArrayHashAttribute>()?.Hash ?? uint.MaxValue;
        }

        return type.GetCustomAttribute<NameHashAttribute>()?.Hash ?? uint.MaxValue;
    }

    internal static void ReadCache(DataStream inStream)
    {
        int count = inStream.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            TypeInfoAsset type = new(inStream.ReadNullTerminatedString(), inStream.ReadUInt32(), inStream.ReadGuid());
            const int flag = 1 << 31;
            int index = s_typeInfoAssets.Count | flag;
            s_nameMapping.Add(type.Name, index);
            s_guidMapping.Add(type.Guid, index);
            s_nameHashMapping.Add(type.NameHash, index);
            s_typeInfoAssets.Add(type);
        }
    }

    internal static void WriteCache(DataStream inStream)
    {
        long pos = inStream.Position;
        inStream.WriteUInt32(0xdeadbeef);

        int count = 0;
        foreach (TypeInfoAsset type in s_typeInfoAssets)
        {

            count++;

            inStream.WriteNullTerminatedString(type.Name);
            inStream.WriteUInt32(type.NameHash);
            inStream.WriteGuid(type.Guid);
        }
        inStream.StepIn(pos);
        inStream.WriteInt32(count);
        inStream.StepOut();
    }
}