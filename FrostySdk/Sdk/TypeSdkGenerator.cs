﻿using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Basic.Reference.Assemblies;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using FrostyTypeSdkGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Frosty.Sdk.Sdk;

public class TypeSdkGenerator
{
    private long FindTypeInfoOffset(Process process)
    {
        // TODO: remove this once all games have their correct pattern
        // string[] patterns =
        // {
        //     "48 8b 05 ?? ?? ?? ?? 48 89 41 08 48 89 0d ?? ?? ?? ?? 48 ?? ?? C3",
        //     "48 8b 05 ?? ?? ?? ?? 48 89 41 08 48 89 0d ?? ?? ?? ?? C3",
        //     "48 8b 05 ?? ?? ?? ?? 48 89 41 08 48 89 0d ?? ?? ?? ??",
        //     "48 8b 05 ?? ?? ?? ?? 48 89 05 ?? ?? ?? ?? 48 8d 05 ?? ?? ?? ?? 48 89 05 ?? ?? ?? ?? E9",
        //     "48 39 1D ?? ?? ?? ?? ?? ?? 48 8b 43 10", // new games
        // };

        MemoryReader reader = new(process);
        nint offset = reader.ScanPatter(ProfilesLibrary.TypeInfoSignature);

        if (offset == nint.Zero)
        {
            return -1;
        }

        reader.Position = offset + 3;
        int newValue = reader.ReadInt(false);
        reader.Position = offset + 3 + newValue + 4;
        return reader.ReadLong(false);
    }

    public bool DumpTypes(Process process)
    {
        long typeInfoOffset = FindTypeInfoOffset(process);
        if (typeInfoOffset == -1)
        {
            return false;
        }

        string stringsPath = $"Sdk/Strings/{ProfilesLibrary.InternalName}.json";
        if (ProfilesLibrary.HasStrippedTypeNames && File.Exists(stringsPath))
        {
            // load strings file
            Dictionary<uint, string>? mapping = JsonSerializer.Deserialize<Dictionary<uint, string>>(File.ReadAllText(stringsPath));
            if (mapping is null)
            {
                return false;
            }

            Strings.Mapping = mapping;
            Strings.HasStrings = true;
        }

        MemoryReader reader = new(process) { Position = typeInfoOffset };
        TypeInfo.TypeInfoMapping.Clear();

        TypeInfo? ti = TypeInfo.ReadTypeInfo(reader);

        do
        {
            ti = ti.GetNextTypeInfo(reader);
        } while (ti is not null);

        Directory.CreateDirectory("Sdk/Strings");

        if (ProfilesLibrary.HasStrippedTypeNames && !Strings.HasStrings)
        {
            // try to resolve hashes from other games
            foreach (string file in Directory.EnumerateFiles("Sdk/Strings/*.json"))
            {
                Dictionary<uint, string>? mapping = JsonSerializer.Deserialize<Dictionary<uint, string>>(File.ReadAllText(file));
                if (mapping is null)
                {
                    continue;
                }

                foreach (string name in mapping.Values)
                {
                    uint hash = HashTypeName(name);
                    if (!Strings.Mapping.TryGetValue(hash, out string? currentName))
                    {
                        Strings.Mapping[hash] = name;
                    }
                    else
                    {
                        // They changed some capitalization stuff like Uint32 and UInt32, but shouldn't really matter that much
                        Debug.Assert(currentName.Equals(name, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }

            Strings.HasStrings = true;

            HashSet<uint> toRemove = new();
            foreach (KeyValuePair<uint, string> kv in Strings.Mapping)
            {
                if (string.IsNullOrEmpty(kv.Value))
                {
                    toRemove.Add(kv.Key);
                }
            }

            foreach (uint key in toRemove)
            {
                // remove entry from dict
                Strings.Mapping.Remove(key);
            }

            // save file and reload TypeInfo
            File.WriteAllText(stringsPath, JsonSerializer.Serialize(Strings.Mapping));

            // reparse the typeinfo with now hopefully more typenames
            TypeInfo.TypeInfoMapping.Clear();

            ti = TypeInfo.ReadTypeInfo(reader);

            do
            {
                ti = ti.GetNextTypeInfo(reader);
            } while (ti is not null);
        }
        else
        {
            // write all names to a file so we can resolve most hashes for games that have names stripped
            File.WriteAllText(stringsPath, JsonSerializer.Serialize(Strings.Mapping));
        }

        if (TypeInfo.TypeInfoMapping.Count > 0)
        {
            FrostyLogger.Logger?.LogInfo($"Found {TypeInfo.TypeInfoMapping.Count} types in the games memory");
            return true;
        }

        FrostyLogger.Logger?.LogError("No types found in the games memory, maybe the pattern is wrong");

        return false;
    }

    public bool CreateSdk(string filePath)
    {
        StringBuilder sb = new();

        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.ObjectModel;");
        sb.AppendLine("using System.Reflection;");
        sb.AppendLine("using Frosty.Sdk.Attributes;");
        sb.AppendLine("using Frosty.Sdk.Interfaces;");
        sb.AppendLine("using Frosty.Sdk.Managers;");
        sb.AppendLine("using Frosty.Sdk;");
        sb.AppendLine();
        sb.AppendLine("[assembly: SdkVersion(" + FileSystemManager.Head + ")]");
        sb.AppendLine();

        foreach (TypeInfo typeInfo in TypeInfo.TypeInfoMapping.Values)
        {
            switch (typeInfo.GetFlags().GetTypeEnum())
            {
                case TypeFlags.TypeEnum.Struct:
                case TypeFlags.TypeEnum.Class:
                case TypeFlags.TypeEnum.Enum:
                case TypeFlags.TypeEnum.Delegate:
                    typeInfo.CreateType(sb);
                    break;

                // primitive types
                case TypeFlags.TypeEnum.String:
                case TypeFlags.TypeEnum.CString:
                case TypeFlags.TypeEnum.FileRef:
                case TypeFlags.TypeEnum.Boolean:
                case TypeFlags.TypeEnum.Int8:
                case TypeFlags.TypeEnum.UInt8:
                case TypeFlags.TypeEnum.Int16:
                case TypeFlags.TypeEnum.UInt16:
                case TypeFlags.TypeEnum.Int32:
                case TypeFlags.TypeEnum.UInt32:
                case TypeFlags.TypeEnum.Int64:
                case TypeFlags.TypeEnum.UInt64:
                case TypeFlags.TypeEnum.Float32:
                case TypeFlags.TypeEnum.Float64:
                case TypeFlags.TypeEnum.Guid:
                case TypeFlags.TypeEnum.Sha1:
                case TypeFlags.TypeEnum.ResourceRef:
                case TypeFlags.TypeEnum.TypeRef:
                case TypeFlags.TypeEnum.BoxedValueRef:
                    typeInfo.CreateType(sb);
                    break;
            }
        }

        string source = sb.ToString();

        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);

        List<MetadataReference> references =
            new() { MetadataReference.CreateFromFile(typeof(TypeLibrary).Assembly.Location) };

        references.AddRange(Net70.References.All);


#if EBX_TYPE_SDK_DEBUG
        OptimizationLevel level = OptimizationLevel.Debug;
#else
        OptimizationLevel level = OptimizationLevel.Release;
#endif

        CSharpCompilation compilation = CSharpCompilation.Create("EbxTypes", new[] { syntaxTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, optimizationLevel: level));

        List<AdditionalText> meta = new();

        if (Directory.Exists("Meta"))
        {
            foreach (string additionalTextPath in Directory.EnumerateFiles("Meta"))
            {
                meta.Add(new CustomAdditionalText(additionalTextPath));
            }
        }

        GeneratorDriver driver = CSharpGeneratorDriver
            .Create(new SourceGenerator())
            .AddAdditionalTexts(ImmutableArray.CreateRange(meta));

        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out _);

#if EBX_TYPE_SDK_DEBUG
        foreach (SyntaxTree tree in outputCompilation.SyntaxTrees)
        {
            if (string.IsNullOrEmpty(tree.FilePath))
            {
                File.WriteAllText("DumpedTypes.cs", tree.GetText().ToString());
                continue;
            }

            FileInfo fileInfo = new(tree.FilePath);
            Directory.CreateDirectory(fileInfo.DirectoryName!);
            File.WriteAllText(tree.FilePath, tree.GetText().ToString());
        }
#endif

        using (MemoryStream stream = new())
        {
            EmitResult result = outputCompilation.Emit(stream);
            if (!result.Success)
            {
#if EBX_TYPE_SDK_DEBUG
                File.WriteAllLines("Errors.txt", result.Diagnostics.Select(static d => d.ToString()));
                FrostyLogger.Logger?.LogError($"Could not compile sdk, errors written to Errors.txt");
#else
                FrostyLogger.Logger?.LogError($"Could not compile sdk");
#endif
                return false;
            }
            File.WriteAllBytes(filePath, stream.ToArray());
            FrostyLogger.Logger?.LogInfo("Successfully compiled sdk");
        }

        return true;
    }

    private static uint HashTypeName(string inName)
    {
        Span<byte> hash = stackalloc byte[32];
        ReadOnlySpan<byte> str = Encoding.ASCII.GetBytes(inName.ToLower() + ProfilesLibrary.TypeHashSeed);
        SHA256.HashData(str, hash);
        return BinaryPrimitives.ReadUInt32BigEndian(hash[28..]);
    }
}