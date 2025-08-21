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
using Frosty.Sdk.Sdk.TypeInfos;
using FrostyTypeSdkGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Frosty.Sdk.Sdk;

public class TypeSdkGenerator
{
    private long FindTypeInfoOffset(MemoryReader reader)
    {
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
        MemoryReader reader = new(process);
        long typeInfoOffset = FindTypeInfoOffset(reader);
        if (typeInfoOffset == -1)
        {
            FrostyLogger.Logger?.LogError("No offset found for TypeInfo, maybe try a different TypeInfoSignature");
            return false;
        }

        FrostyLogger.Logger?.LogInfo($"Dumping types at offset {typeInfoOffset:X8}");

        string stringsDir = Path.Combine(Utils.Utils.BaseDirectory, "Sdk", "Strings");
        string typeNamesPath = Path.Combine(stringsDir, $"{ProfilesLibrary.InternalName}_types.json");
        string fieldNamesPath = Path.Combine(stringsDir, $"{ProfilesLibrary.InternalName}_fields.json");
        Strings.TypeNames = new HashSet<string>();
        Strings.FieldNames = new Dictionary<string, HashSet<string>>();

        if (ProfilesLibrary.HasStrippedTypeNames)
        {
            Strings.TypeMapping = new Dictionary<uint, string>();
            Strings.FieldMapping = new Dictionary<uint, Dictionary<uint, string>>();
            Strings.TypeHashes = new HashSet<uint>();
            Strings.FieldHashes = new Dictionary<uint, HashSet<uint>>();

            if (File.Exists(typeNamesPath))
            {
                // load previously created string file
                HashSet<string>? typeNames = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(typeNamesPath));
                if (typeNames is null)
                {
                    return false;
                }

                // create our type mapping
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

                // create our field mapping
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
        }

        reader.Position = typeInfoOffset;
        TypeInfo.TypeInfoMapping = new Dictionary<long, TypeInfo>();
        ArrayInfo.Mapping = new Dictionary<long, long>();

        TypeInfo? ti = TypeInfo.ReadTypeInfo(reader);

        do
        {
            ti = ti.GetNextTypeInfo(reader);
        } while (ti is not null);

        Directory.CreateDirectory(stringsDir);

        if (ProfilesLibrary.HasStrippedTypeNames && !Strings.HasStrings)
        {
            // try to resolve type hashes from other games
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

                    // continue if type is not used
                    if (!Strings.TypeHashes.Contains(hash))
                    {
                        continue;
                    }

                    // add type to our mapping if we haven't resolved it already
                    if (!Strings.TypeMapping.TryGetValue(hash, out string? currentName) || string.IsNullOrEmpty(currentName))
                    {
                        Strings.TypeMapping[hash] = name;
                    }
                    else
                    {
                        // a type with this hash was already added, check if its the same (ignore case)
                        if (!currentName.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            FrostyLogger.Logger?.LogInfo($"Type hash {hash:X8} duplicate. Using \"{currentName}\" instead of \"{name}\"");
                        }
                    }
                }
            }

            // try to resolve field hashes from other games
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

                    // only continue if type is used
                    if (!Strings.FieldHashes.TryGetValue(typeHash, out HashSet<uint>? fields))
                    {
                        continue;
                    }

                    // same thing as before
                    if (!Strings.FieldMapping.TryGetValue(typeHash, out Dictionary<uint, string>? dict))
                    {
                        continue;
                    }

                    foreach (string field in type.Value)
                    {
                        uint fieldHash = HashTypeName(field);

                        // only continue if field is used
                        if (!fields.Contains(fieldHash))
                        {
                            continue;
                        }

                        // if we havent already resolved this hash set it
                        if (!dict.TryGetValue(fieldHash, out string? name) || string.IsNullOrEmpty(name))
                        {
                            dict[fieldHash] = field;
                        }
                        else
                        {
                            // a field with this hash was already added, check if its the same (ignore case)
                            if (!name.Equals(name, StringComparison.OrdinalIgnoreCase))
                            {
                                FrostyLogger.Logger?.LogInfo($"Type hash {fieldHash:X8} duplicate. Using \"{name}\" instead of \"{field}\"");
                            }
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

            // update the typeinfo with now hopefully more typenames

            foreach (TypeInfo typeInfo in TypeInfo.TypeInfoMapping.Values)
            {
                typeInfo.UpdateName();
            }
        }
        else
        {
            // write all names to a file so we can resolve most hashes for games that have names stripped
            File.WriteAllText(typeNamesPath, JsonSerializer.Serialize(Strings.TypeNames));
            File.WriteAllText(fieldNamesPath, JsonSerializer.Serialize(Strings.FieldNames));
        }

        Strings.Reset();

        if (TypeInfo.TypeInfoMapping?.Count > 0)
        {
            foreach (TypeInfo type in TypeInfo.TypeInfoMapping.Values)
            {
                if (type is ClassInfo c)
                {
                    c.ReadDefaultValues(reader);
                }
                else if (type is StructInfo s)
                {
                    s.ReadDefaultValues(reader);
                }
            }

            FrostyLogger.Logger?.LogInfo($"Found {TypeInfo.TypeInfoMapping.Count} types in the games memory");
            return true;
        }

        FrostyLogger.Logger?.LogError("No types found in the games memory, maybe the pattern is wrong");

        return false;
    }

    public bool CreateSdk(string filePath)
    {
        FrostyLogger.Logger?.LogInfo("Creating sdk");

        if (TypeInfo.TypeInfoMapping is null)
        {
            FrostyLogger.Logger?.LogError($"No dumped types to create the sdk from. Call {nameof(DumpTypes)} first.");
            return false;
        }

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
            switch (typeInfo)
            {
                case ClassInfo:
                case StructInfo:
                case EnumInfo:
                case InterfaceInfo:
                case DelegateInfo:
                case FunctionInfo:
                case PrimitiveInfo:
                    typeInfo.CreateType(sb);
                    break;
            }
        }

        TypeInfo.TypeInfoMapping = null;
        ArrayInfo.Mapping = null;

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
            out ImmutableArray<Diagnostic> diagnostics);

        using (MemoryStream stream = new())
        {
            EmitResult result = outputCompilation.Emit(stream);
            if (!result.Success)
            {
                File.WriteAllLines("Errors.txt", result.Diagnostics.Select(static d => d.ToString()));
                File.WriteAllLines("Errors_gen.txt", diagnostics.Select(static d => d.ToString()));
                FrostyLogger.Logger?.LogError($"Could not compile sdk, errors written to Errors.txt");

                // write types
                foreach (SyntaxTree tree in outputCompilation.SyntaxTrees)
                {
                    if (string.IsNullOrEmpty(tree.FilePath))
                    {
                        File.WriteAllText("DumpedTypes.cs", tree.GetText().ToString());
#if EBX_TYPE_SDK_DEBUG
                        continue;
#else
                break;
#endif
                    }

                    FileInfo fileInfo = new(tree.FilePath);
                    Directory.CreateDirectory(fileInfo.DirectoryName!);
                    File.WriteAllText(tree.FilePath, tree.GetText().ToString());
                }

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

    public static uint HashTypeName(string inName)
    {
        Span<byte> hash = stackalloc byte[32];
        ReadOnlySpan<byte> str = Encoding.ASCII.GetBytes(inName.ToLower() + ProfilesLibrary.TypeHashSeed);
        SHA256.HashData(str, hash);
        return BinaryPrimitives.ReadUInt32BigEndian(hash[28..]);
    }
}