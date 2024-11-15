using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.Managers;
using Microsoft.Extensions.Logging;

namespace Frosty.Sdk;

public static class TypeLibrary
{
    public static bool IsInitialized { get; private set; }

    private static readonly Dictionary<string, int> s_nameMapping = new();
    private static readonly Dictionary<uint, int> s_nameHashMapping = new();
    private static readonly Dictionary<Guid, int> s_guidMapping = new();
    private static List<Type> s_types = [];

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
            FrostyLogger.Logger?.LogInformation("Outdated Type Sdk, please regenerate it to avoid issues");
        }

        s_types.AddRange(sdk.GetExportedTypes());

        for (int i = 0; i < s_types.Count; i++)
        {
            Type type = s_types[i];

            NameHashAttribute? nameHashAttribute = type.GetCustomAttribute<NameHashAttribute>();
            if (nameHashAttribute is null)
            {
                // issue described in #25 we are just ignoring these cases
                continue;
            }
            uint nameHash = nameHashAttribute.Hash;
            string name = type.GetName();
            Guid? guid = type.GetCustomAttribute<GuidAttribute>()?.Guid;

            s_nameMapping.Add(name, i);
            s_nameHashMapping.Add(nameHash, i);
            if (guid.HasValue)
            {
                s_guidMapping.Add(guid.Value, i);
            }
            Guid? arrayGuid = type.GetCustomAttribute<ArrayGuidAttribute>()?.Guid;
            if (arrayGuid.HasValue)
            {
                s_guidMapping.Add(arrayGuid.Value, s_types.Count);
                s_types.Add(typeof(ObservableCollection<>).MakeGenericType(type));
            }
        }

        IsInitialized = true;
        return true;
    }

    public static IEnumerable<Type> EnumerateTypes() => s_types;

    public static Type? GetType(string name)
    {
        if (!s_nameMapping.TryGetValue(name, out int index))
        {
            return null;
        }
        return s_types[index];
    }

    public static Type? GetType(uint nameHash)
    {
        if (!s_nameHashMapping.TryGetValue(nameHash, out int index))
        {
            return null;
        }
        return s_types[index];
    }

    public static Type? GetType(Guid guid)
    {
        if (!s_guidMapping.TryGetValue(guid, out int index))
        {
            return null;
        }
        return s_types[index];
    }

    public static object? CreateObject(string name)
    {
        Type? type = GetType(name);
        return type == null ? null : Activator.CreateInstance(type);
    }

    public static object? CreateObject(uint nameHash)
    {
        Type? type = GetType(nameHash);
        return type == null ? null : Activator.CreateInstance(type);
    }

    public static object? CreateObject(Guid guid)
    {
        Type? type = GetType(guid);
        return type == null ? null : Activator.CreateInstance(type);
    }

    public static object? CreateObject(Type type)
    {
        return Activator.CreateInstance(type);
    }

    public static bool IsSubClassOf(object obj, string name)
    {
        Type type = obj.GetType();

        return IsSubClassOf(type, name);
    }

    public static bool IsSubClassOf(Type type, string name)
    {
        Type? checkType = GetType(name);
        if (checkType == null)
        {
            return false;
        }

        return type.IsSubclassOf(checkType) || (type == checkType);
    }

    public static bool IsSubClassOf(string type, string name)
    {
        Type? sourceType = GetType(type);

        return sourceType != null && IsSubClassOf(sourceType, name);
    }

    public static string GetName(this MemberInfo type)
    {
        if (type.Name == "ObservableCollection`1")
        {
            Type elementType = (type as Type)!.GenericTypeArguments[0].Name == "PointerRef" ? GetType("DataContainer")! : (type as Type)!.GenericTypeArguments[0];

            return elementType.GetCustomAttribute<ArrayNameAttribute>()?.Name ?? type.Name + "-Array";
        }
        return type.GetCustomAttribute<DisplayNameAttribute>()?.Name ?? type.Name;
    }

    public static Guid GetGuid(this MemberInfo type)
    {
        if (type.Name == "ObservableCollection`1")
        {
            Type elementType = (type as Type)!.GenericTypeArguments[0].Name == "PointerRef" ? GetType("DataContainer")! : (type as Type)!.GenericTypeArguments[0];

            return elementType.GetCustomAttribute<ArrayGuidAttribute>()?.Guid ??  Guid.Empty;
        }
        return type.GetCustomAttribute<GuidAttribute>()?.Guid ?? Guid.Empty;
    }

    public static uint GetSignature(this MemberInfo type)
    {
        if (type.Name == "ObservableCollection`1")
        {
            Type elementType = (type as Type)!.GenericTypeArguments[0].Name == "PointerRef" ? GetType("DataContainer")! : (type as Type)!.GenericTypeArguments[0];

            return elementType.GetCustomAttribute<SignatureAttribute>()?.Signature ?? uint.MaxValue;
        }
        return type.GetCustomAttribute<SignatureAttribute>()?.Signature ?? uint.MaxValue;
    }

    public static uint GetNameHash(this MemberInfo type)
    {
        if (type.Name == "ObservableCollection`1")
        {
            Type elementType = (type as Type)!.GenericTypeArguments[0].Name == "PointerRef" ? GetType("DataContainer")! : (type as Type)!.GenericTypeArguments[0];

            return elementType.GetCustomAttribute<ArrayHashAttribute>()?.Hash ?? uint.MaxValue;
        }

        return type.GetCustomAttribute<NameHashAttribute>()?.Hash ?? uint.MaxValue;
    }
}