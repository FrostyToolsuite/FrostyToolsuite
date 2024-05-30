using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        MemoryReader reader = new(process);

        nint offset = nint.Zero;

        if (!string.IsNullOrEmpty(ProfilesLibrary.TypeInfoSignature))
        {
            offset = reader.ScanPatter(ProfilesLibrary.TypeInfoSignature);
        }
        else
        {
            // TODO: remove this once all games have their correct pattern
            string[] patterns =
            {
                "48 8b 05 ?? ?? ?? ?? 48 89 41 08 48 89 0d ?? ?? ?? ?? C3",
                "48 8b 05 ?? ?? ?? ?? 48 89 41 08 48 89 0d ?? ?? ?? ??",
                "48 8b 05 ?? ?? ?? ?? 48 89 41 08 48 89 0d ?? ?? ?? ?? 48 ?? ?? C3",
                "48 8b 05 ?? ?? ?? ?? 48 89 05 ?? ?? ?? ?? 48 8d 05 ?? ?? ?? ?? 48 89 05 ?? ?? ?? ?? E9",
                "48 39 1D ?? ?? ?? ?? ?? ?? 48 8b 43 10", // new games
            };
            foreach (string sig in patterns)
            {
                offset = reader.ScanPatter(sig);
                if (offset != nint.Zero)
                {
                    FrostyLogger.Logger?.LogInfo($"No TypeInfoSig set, found offset for \"{sig}\"");
                    break;
                }
            }
        }

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
            FrostyLogger.Logger?.LogError("No offset found for TypeInfo, maybe try a different TypeInfoSignature");
            return false;
        }

        FrostyLogger.Logger?.LogInfo($"Dumping types at offset {typeInfoOffset:X8}");

        string stringsDir = Path.Combine(Utils.Utils.BaseDirectory, "Sdk", "Strings");
        string typeNamesPath = Path.Combine(stringsDir, $"{ProfilesLibrary.InternalName}_types.json");
        string fieldNamesPath = Path.Combine(stringsDir, $"{ProfilesLibrary.InternalName}_fields.json");
        if (ProfilesLibrary.HasStrippedTypeNames && File.Exists(typeNamesPath))
        {
            // load strings files
            HashSet<string>? typeNames = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(typeNamesPath));
            if (typeNames is null)
            {
                return false;
            }

            foreach (string name in typeNames)
            {
                Strings.TypeMapping.Add(HashTypeName(name), name);
            }

            Dictionary<string, HashSet<string>>? fieldNames =
                JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(File.ReadAllText(fieldNamesPath));
            if (fieldNames is null)
            {
                return false;
            }

            foreach (KeyValuePair<string, HashSet<string>> kv in fieldNames)
            {
                Dictionary<uint, string> dict = new();
                foreach (string name in kv.Value)
                {
                    dict.Add(HashTypeName(name), name);
                }

                Strings.FieldMapping.Add(HashTypeName(kv.Key), dict);
            }

            Strings.HasStrings = true;
        }

        MemoryReader reader = new(process) { Position = typeInfoOffset };
        TypeInfo.TypeInfoMapping.Clear();

        TypeInfo? ti = TypeInfo.ReadTypeInfo(reader);

        do
        {
            ti = ti.GetNextTypeInfo(reader);
        } while (ti is not null);

        Directory.CreateDirectory(stringsDir);

        if (ProfilesLibrary.HasStrippedTypeNames && !Strings.HasStrings)
        {
            // try to resolve hashes from other games
            foreach (string file in Directory.EnumerateFiles(stringsDir, "*_types.json"))
            {
                HashSet<string>? types = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(file));
                if (types is null)
                {
                    continue;
                }

                foreach (string name in types)
                {
                    uint hash = HashTypeName(name);

                    if (!Strings.TypeHashes.Contains(hash))
                    {
                        continue;
                    }

                    if (!Strings.TypeMapping.TryGetValue(hash, out string? currentName) || string.IsNullOrEmpty(currentName))
                    {
                        Strings.TypeMapping[hash] = name;
                    }
                    else
                    {
                        Debug.Assert(currentName.Equals(name, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }

            foreach (string file in Directory.EnumerateFiles(stringsDir, "*_fields.json"))
            {
                Dictionary<string, HashSet<string>>? mapping = JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(File.ReadAllText(file));
                if (mapping is null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, HashSet<string>> type in mapping)
                {
                    uint typeHash = HashTypeName(type.Key);

                    if (!Strings.FieldHashes.TryGetValue(typeHash, out HashSet<uint>? fields))
                    {
                        continue;
                    }

                    if (!Strings.FieldMapping.TryGetValue(typeHash, out Dictionary<uint, string>? dict))
                    {
                        continue;
                    }

                    foreach (string field in type.Value)
                    {
                        uint fieldHash = HashTypeName(field);

                        if (!fields.Contains(fieldHash))
                        {
                            continue;
                        }

                        if (!dict.TryGetValue(fieldHash, out string? name) || string.IsNullOrEmpty(name))
                        {
                            dict[fieldHash] = field;
                        }
                        else
                        {
                            Debug.Assert(name.Equals(field, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                }
            }

            Strings.HasStrings = true;

            HashSet<uint> toRemove = new();
            foreach (KeyValuePair<uint, string> kv in Strings.TypeMapping)
            {
                if (string.IsNullOrEmpty(kv.Value))
                {
                    toRemove.Add(kv.Key);
                }
            }

            FrostyLogger.Logger?.LogInfo($"Resolved {Strings.TypeMapping.Count - toRemove.Count} type names");
            FrostyLogger.Logger?.LogInfo($"{toRemove.Count} unresolved type names left");

            foreach (uint key in toRemove)
            {
                // remove entry from dict
                Strings.TypeMapping.Remove(key);
                Strings.FieldMapping.Remove(key);
            }

            int totalFieldNames = 0;
            int unresolvedFieldNames = 0;

            foreach (Dictionary<uint,string> mapping in Strings.FieldMapping.Values)
            {
                toRemove.Clear();

                foreach (KeyValuePair<uint, string> kv in mapping)
                {
                    totalFieldNames++;
                    if (string.IsNullOrEmpty(kv.Value))
                    {
                        unresolvedFieldNames++;
                        toRemove.Add(kv.Key);
                    }
                }

                foreach (uint key in toRemove)
                {
                    // remove entry from dict
                    mapping.Remove(key);
                }
            }

            FrostyLogger.Logger?.LogInfo($"Resolved {totalFieldNames - unresolvedFieldNames} field names");
            FrostyLogger.Logger?.LogInfo($"{unresolvedFieldNames} unresolved field names left");

            Strings.TypeNames.UnionWith(Strings.TypeMapping.Values);
            foreach (KeyValuePair<uint,Dictionary<uint,string>> pair in Strings.FieldMapping)
            {
                HashSet<string> fields = new();
                foreach (string name in pair.Value.Values)
                {
                    fields.Add(name);
                }
                Strings.FieldNames.Add(Strings.TypeMapping[pair.Key], fields);
            }

            // save file and reload TypeInfo
            File.WriteAllText(typeNamesPath, JsonSerializer.Serialize(Strings.TypeNames));
            File.WriteAllText(fieldNamesPath, JsonSerializer.Serialize(Strings.FieldNames));

            // reparse the typeinfo with now hopefully more typenames
            TypeInfo.TypeInfoMapping.Clear();

            reader.Position = typeInfoOffset;
            ti = TypeInfo.ReadTypeInfo(reader);

            do
            {
                ti = ti.GetNextTypeInfo(reader);
            } while (ti is not null);
        }
        else
        {
            // write all names to a file so we can resolve most hashes for games that have names stripped
            File.WriteAllText(typeNamesPath, JsonSerializer.Serialize(Strings.TypeNames));
            File.WriteAllText(fieldNamesPath, JsonSerializer.Serialize(Strings.FieldNames));
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
        FrostyLogger.Logger?.LogInfo("Creating sdk");

        StringBuilder sb = new();

        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.ObjectModel;");
        sb.AppendLine("using System.Reflection;");
        sb.AppendLine("using Frosty.Sdk.Attributes;");
        sb.AppendLine("using Frosty.Sdk.Interfaces;");
        sb.AppendLine("using Frosty.Sdk.Managers;");
        sb.AppendLine("using Frosty.Sdk;");
        sb.AppendLine();
        sb.AppendLine($"[assembly: SdkVersion({FileSystemManager.Head})]");
        sb.AppendLine();

        foreach (TypeInfo typeInfo in TypeInfo.TypeInfoMapping.Values)
        {
            switch (typeInfo.GetFlags().GetTypeEnum())
            {
                case TypeFlags.TypeEnum.Struct:
                case TypeFlags.TypeEnum.Class:
                case TypeFlags.TypeEnum.Enum:
                case TypeFlags.TypeEnum.Delegate:
                case TypeFlags.TypeEnum.Interface:
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

        IEnumerable<MetadataReference> references = GetMetadataReferences();

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

    private IEnumerable<MetadataReference> GetMetadataReferences()
    {
        // use observable collection here, so System.ObjectModel gets loaded
        ObservableCollection<MetadataReference> metadataReferenceList = new();

        Assembly[] domainAssemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (Assembly assembly in domainAssemblies)
        {
            unsafe
            {
                assembly.TryGetRawMetadata(out byte* blob, out int length);
                ModuleMetadata moduleMetadata = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
                AssemblyMetadata assemblyMetadata = AssemblyMetadata.Create(moduleMetadata);
                PortableExecutableReference metadataReference = assemblyMetadata.GetReference();
                metadataReferenceList.Add(metadataReference);
            }
        }

        return metadataReferenceList;
    }

    private static uint HashTypeName(string inName)
    {
        Span<byte> hash = stackalloc byte[32];
        ReadOnlySpan<byte> str = Encoding.ASCII.GetBytes(inName.ToLower() + ProfilesLibrary.TypeHashSeed);
        SHA256.HashData(str, hash);
        return BinaryPrimitives.ReadUInt32BigEndian(hash[28..]);
    }
}