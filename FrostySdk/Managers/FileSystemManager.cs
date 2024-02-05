using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Frosty.Sdk.DbObjectElements;
using Frosty.Sdk.Deobfuscators;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.Managers;

public static class FileSystemManager
{
    public static bool IsInitialized { get; private set; }

    public static string BasePath { get; private set; } = string.Empty;
    public static string CacheName { get; private set; } = string.Empty;

    public static uint Base { get; private set; }
    public static uint Head { get; private set; }

    public static BundleFormat BundleFormat { get; private set; } = BundleFormat.Dynamic2018;

    public static GamePlatform GamePlatform { get; private set; } = GamePlatform.Invalid;

    public static DbObjectDict? SuperBundleManifest { get; private set; }

    public static readonly List<FileSystemSource> Sources = new(2) { FileSystemSource.Patch, FileSystemSource.Base };

    public static InstallChunkInfo? DefaultInstallChunk;

    private static readonly Dictionary<string, SuperBundleInfo> s_superBundleMapping = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, int> s_persistentIndexMapping = new();
    private static readonly List<InstallChunkInfo> s_installChunks = new();
    private static readonly Dictionary<int, SuperBundleInstallChunk> s_sbIcMapping = new();
    private static readonly Dictionary<int, string> s_casFiles = new();

    private static Type? s_deobfuscator;
    private static readonly Dictionary<string, Block<byte>> s_memoryFs = new();

    public static bool Initialize(string basePath)
    {
        if (IsInitialized)
        {
            return true;
        }

        if (!ProfilesLibrary.IsInitialized)
        {
            FrostyLogger.Logger?.LogError("ProfilesLibrary not initialized yet");
            return false;
        }

        if (!Directory.Exists(basePath))
        {
            FrostyLogger.Logger?.LogError($"No directory \"{basePath}\" exists");
            return false;
        }

        BasePath = Path.GetFullPath(basePath);

        CacheName = Path.Combine(Utils.Utils.BaseDirectory, "Caches", $"{ProfilesLibrary.InternalName}");
        s_deobfuscator = ProfilesLibrary.FrostbiteVersion > "2014.4.11"
            ? typeof(SignatureDeobfuscator)
            : typeof(LegacyDeobfuscator);

        if (Directory.Exists($"{BasePath}/Update"))
        {
            foreach (string dlc in Directory.EnumerateDirectories($"{BasePath}/Update"))
            {
                if (!File.Exists(Path.Combine(dlc, "package.mft")))
                {
                    continue;
                }

                string subPath = Path.GetFileName(dlc);
                if (subPath == "Patch")
                {
                    // do nothing
                }
                else
                {
                    // load order is patch -> dlc -> data
                    Sources.Insert(1, new FileSystemSource($"Update/{subPath}/Data", FileSystemSource.Type.DLC));
                }
            }
        }

        if (!Directory.Exists($"{BasePath}/{FileSystemSource.Patch.Path}"))
        {
            Sources.RemoveAt(0);
        }

        if (!ProcessLayouts())
        {
            return false;
        }

        if (FileSystemSource.Base.TryResolvePath("kelvin.toc", out _))
        {
            BundleFormat = BundleFormat.Kelvin;
        }

        IsInitialized = true;
        return true;
    }

    /// <summary>
    /// Resolves the path of a file inside the games data directories.
    /// </summary>
    /// <param name="filename">
    /// <para>The relative path of the file prefixed with native_data or native_patch.</para>
    /// If there is no prefix it will look through all data directories starting with the patch ones.
    /// </param>
    /// <returns>The full path to the file or an empty string if the file doesnt exist.</returns>
    public static string ResolvePath(string filename)
    {
        if (filename.StartsWith("native_data/"))
        {
            return FileSystemSource.Base.ResolvePath(filename[12..]);
        }

        string s = filename.StartsWith("native_patch/") ? filename[13..] : filename;
        foreach (FileSystemSource source in Sources)
        {
            if (source.TryResolvePath(s, out string? path))
            {
                return path;
            }
        }

        return string.Empty;
    }

    public static bool TryResolvePath(string filename, [NotNullWhen(true)] out string? resolvedPath)
    {
        if (filename.StartsWith("native_data/"))
        {
            return FileSystemSource.Base.TryResolvePath(filename[12..], out resolvedPath);
        }

        string s = filename.StartsWith("native_patch/") ? filename[13..] : filename;
        foreach (FileSystemSource source in Sources)
        {
            if (source.TryResolvePath(s, out resolvedPath))
            {

                return true;
            }
        }

        resolvedPath = null;
        return false;
    }

    public static string ResolvePath(bool isPatch, string filename)
    {
        if (isPatch)
        {
            return FileSystemSource.Patch.ResolvePath(filename);
        }

        foreach (FileSystemSource source in Sources)
        {
            if (source.Path == FileSystemSource.Patch.Path)
            {
                continue;
            }

            if (source.TryResolvePath(filename, out string? path))
            {
                if (source.IsDLC())
                {

                }

                return path;
            }
        }

        return string.Empty;
    }

    public static bool TryResolvePath(bool isPatch, string filename, [NotNullWhen(true)] out string? resolvedPath)
    {
        if (isPatch)
        {
            return FileSystemSource.Patch.TryResolvePath(filename, out resolvedPath);
        }

        foreach (FileSystemSource source in Sources)
        {
            if (source.Path == FileSystemSource.Patch.Path)
            {
                continue;
            }

            if (source.TryResolvePath(filename, out resolvedPath))
            {
                return true;
            }
        }

        resolvedPath = null;

        return false;
    }

    public static string GetFilePath(CasFileIdentifier casFileIdentifier)
    {
        InstallChunkInfo installChunkInfo = s_installChunks[s_persistentIndexMapping[casFileIdentifier.InstallChunkIndex]];
        if (casFileIdentifier.IsPatch)
        {
            return FileSystemSource.Patch.ResolvePath(Path.Combine(installChunkInfo.InstallBundle,
                $"cas_{casFileIdentifier.CasIndex:D2}.cas"));
        }

        return ResolvePath(false, Path.Combine(installChunkInfo.InstallBundle,
            $"cas_{casFileIdentifier.CasIndex:D2}.cas"));
    }

    public static string GetFilePath(int casIndex)
    {
        return s_casFiles[casIndex];
    }

    public static IDeobfuscator? CreateDeobfuscator() => s_deobfuscator != null ? (IDeobfuscator?)Activator.CreateInstance(s_deobfuscator) : null;

    public static IEnumerable<SuperBundleInfo> EnumerateSuperBundles()
    {
        foreach (SuperBundleInfo sbInfo in s_superBundleMapping.Values)
        {
            yield return sbInfo;
        }
    }

    public static IEnumerable<InstallChunkInfo> EnumerateInstallChunks()
    {
        foreach (InstallChunkInfo installChunkInfo in s_installChunks)
        {
            yield return installChunkInfo;
        }
    }

    public static InstallChunkInfo GetInstallChunkInfo(int index)
    {
        return s_installChunks[s_persistentIndexMapping[index]];
    }

    public static InstallChunkInfo GetInstallChunkInfo(Guid id)
    {
        return s_installChunks.FirstOrDefault(ic => ic.Id == id) ?? throw new KeyNotFoundException();
    }

    public static int GetInstallChunkIndex(InstallChunkInfo info)
    {
        // TODO: works for now, since we only call this for the cat which doesnt have a persistent index, but we should create a dict for the reverse thing
        return s_installChunks.IndexOf(info);
    }

    public static SuperBundleInstallChunk GetSuperBundleInstallChunk(string sbIcName)
    {
        return s_sbIcMapping[Utils.Utils.HashString(sbIcName, true)];
    }

    public static SuperBundleInstallChunk GetSuperBundleInstallChunk(int hash)
    {
        return s_sbIcMapping[hash];
    }

    public static SuperBundleInfo GetSuperBundle(string inName)
    {
        return s_superBundleMapping[inName];
    }

    public static bool TryGetSuperBundle(string inName, [NotNullWhen(true)] out SuperBundleInfo? superBundleInfo)
    {
        return s_superBundleMapping.TryGetValue(inName, out superBundleInfo);
    }

    public static bool HasFileInMemoryFs(string name) => s_memoryFs.ContainsKey(name);
    public static Block<byte> GetFileFromMemoryFs(string name) => s_memoryFs[name];

    private static bool LoadInitFs(string name, bool loadBase = false)
    {
        ParseGamePlatform(name.Remove(0, 7));

        if (loadBase || !FileSystemSource.Patch.TryResolvePath(name, out string? path))
        {
            if (!FileSystemSource.Base.TryResolvePath(name, out path))
            {
                return false;
            }
        }

        DbObject? initFs = DbObject.Deserialize(path);

        if (initFs is null)
        {
            return false;
        }

        if (initFs.IsDict())
        {
            if (!initFs.AsDict().ContainsKey("encrypted"))
            {
                return false;
            }

            if (!KeyManager.HasKey("InitFsKey"))
            {
                return false;
            }

            using (BlockStream stream = new(new Block<byte>(initFs.AsDict().AsBlob("encrypted"))))
            {
                stream.Decrypt(KeyManager.GetKey("InitFsKey"), PaddingMode.PKCS7);

                initFs = DbObject.Deserialize(stream);

                if (initFs is null)
                {
                    return false;
                }
            }
        }

        foreach (DbObject fileStub in initFs.AsList())
        {
            DbObjectDict file = fileStub.AsDict().AsDict("$file");
            string fileName = file.AsString("name");


            if (fileName == "__fsinternal__")
            {
                LoadInitFs(name, true);
            }
            s_memoryFs.TryAdd(fileName, new Block<byte>(file.AsBlob("payload")));
        }

        return true;
    }

    private static void ParseGamePlatform(string platform)
    {
        if (GamePlatform != GamePlatform.Invalid)
        {
            return;
        }
        switch (platform)
        {
            case "Win32":
                GamePlatform = GamePlatform.Win32;
                break;
            case "Linux":
                GamePlatform = GamePlatform.Linux;
                break;
            case "Xenon":
                GamePlatform = GamePlatform.Xenon;
                break;
            case "Gen4a":
                GamePlatform = GamePlatform.Gen4a;
                break;
            case "Ps3":
                GamePlatform = GamePlatform.Ps3;
                break;
            case "Gen4b":
                GamePlatform = GamePlatform.Gen4b;
                break;
            case "Nx":
                GamePlatform = GamePlatform.Nx;
                break;
            default:
                throw new NotImplementedException($"GamePlatform not implemented: {platform}");
        }
    }

    private static bool ProcessLayouts()
    {
        int index = 0;
        bool processed = false;
        foreach (FileSystemSource source in Sources)
        {
            if (source.IsDLC())
            {
                continue;
            }

            if (source.TryResolvePath("layout.toc", out string? path))
            {
                if (!ProcessLayout(path, index++))
                {
                    return false;
                }

                processed = true;
            }
        }

        return processed;
    }

    private static bool ProcessLayout(string inPath, int inIndex)
    {
        // Process patch layout.toc
        DbObjectDict? layout = DbObject.Deserialize(inPath)?.AsDict();

        if (layout is null)
        {
            return false;
        }
        uint @base = layout.AsUInt("base");
        uint head = layout.AsUInt("head");

        if (inIndex == 0)
        {
            foreach (DbObject superBundle in layout.AsList("superBundles"))
            {
                // load superbundles
                string name = superBundle.AsDict().AsString("name");
                s_superBundleMapping.TryAdd(name, new SuperBundleInfo(name));
                s_superBundleMapping[name].SetLegacyFlags(superBundle.AsDict().AsBoolean("base"), superBundle.AsDict().AsBoolean("same"), superBundle.AsDict().AsBoolean("delta"));
            }

            Head = head;
            Base = @base;

            if (!ProcessInstallChunks(layout.AsDict("installManifest", null)))
            {
                return false;
            }

            SuperBundleManifest = layout.AsDict("manifest", null);
            if (SuperBundleManifest is not null)
            {
                BundleFormat = BundleFormat.SuperBundleManifest;
            }

            if (!LoadInitFs(layout.AsList("fs")[0].AsString()))
            {
                return false;
            }
        }
        else
        {
            // for newer games the base of the patch layout is the head of the base one
            // Debug.Assert(Base == head);
        }

        return true;
    }

    private static bool ProcessInstallChunks(DbObjectDict? installManifest)
    {
        // Only if an install manifest exists
        if (installManifest is null)
        {
            // Older games dont have an InstallManifest, they have one InstallChunk in the data/patch folder
            InstallChunkInfo ic = new();
            foreach (SuperBundleInfo sb in s_superBundleMapping.Values)
            {
                ic.SuperBundles.Add(sb.Name);

                SuperBundleInstallChunk sbIc = new(sb, ic, InstallChunkType.Default);
                s_sbIcMapping.Add(Utils.Utils.HashString(sbIc.Name, true), sbIc);
                sb.InstallChunks.Add(sbIc);
            }

            s_installChunks.Add(ic);
            s_persistentIndexMapping.Add(0, 0);
        }
        else
        {
            string platform = installManifest.AsString("platform");
            if (!string.IsNullOrEmpty(platform))
            {
                ParseGamePlatform(platform);
            }

            // check for platform, else we get it from the initFs
            foreach (DbObject installChunk in installManifest.AsList("installChunks"))
            {
                if (installChunk.AsDict().AsBoolean("testDLC"))
                {
                    continue;
                }

                InstallChunkInfo ic = new()
                {
                    Id = installChunk.AsDict().AsGuid("id"),
                    Name = installChunk.AsDict().AsString("name"),
                    InstallBundle = installChunk.AsDict().AsString("installBundle"),
                    AlwaysInstalled = installChunk.AsDict().AsBoolean("alwaysInstalled"),
                    OptionalDlc = installChunk.AsDict().AsBoolean("optionalDLC")
                };

                if (!string.IsNullOrEmpty(ic.InstallBundle))
                {
                    DefaultInstallChunk ??= ic;
                }

                int index = installChunk.AsDict().AsInt("persistentIndex", s_installChunks.Count);
                s_persistentIndexMapping.Add(index, s_installChunks.Count);
                s_installChunks.Add(ic);

                foreach (DbObject superBundle in installChunk.AsDict().AsList("superbundles"))
                {
                    string name = superBundle.AsString();
                    ic.SuperBundles.Add(name);

                    SuperBundleInfo sb = s_superBundleMapping[name];
                    SuperBundleInstallChunk sbIc = new(sb, ic, InstallChunkType.Default);
                    s_sbIcMapping.Add(Utils.Utils.HashString(sbIc.Name, true), sbIc);
                    sb.InstallChunks.Add(sbIc);
                }

                foreach (DbObject requiredChunk in installChunk.AsDict().AsList("requiredChunks"))
                {
                    ic.RequiredCatalogs.Add(requiredChunk.AsGuid());
                }

                if (installChunk.AsDict().ContainsKey("files"))
                {
                    foreach (DbObject fileObj in installChunk.AsDict().AsList("files"))
                    {
                        int casId = fileObj.AsDict().AsInt("id");

                        string casPath = fileObj.AsDict().AsString("path").Trim('/');
                        casPath = casPath.Replace("native_data/Data", "native_data");
                        casPath = casPath.Replace("native_data/Patch", "native_patch");

                        s_casFiles.Add(casId, casPath);
                    }
                }

                if (installChunk.AsDict().ContainsKey("splitSuperbundles"))
                {
                    foreach (DbObject superBundleContainer in installChunk.AsDict().AsList("splitSuperbundles"))
                    {
                        string name = superBundleContainer.AsDict().AsString("superbundle");
                        ic.SplitSuperBundles.Add(name);

                        SuperBundleInfo sb = s_superBundleMapping[name];
                        SuperBundleInstallChunk sbIc = new(sb, ic, InstallChunkType.Split);
                        s_sbIcMapping.Add(Utils.Utils.HashString(sbIc.Name, true), sbIc);
                        sb.InstallChunks.Add(sbIc);
                    }
                }

                if (installChunk.AsDict().ContainsKey("splitTocs"))
                {
                    foreach (DbObject superBundleContainer in installChunk.AsDict().AsList("splitTocs"))
                    {
                        string name = superBundleContainer.AsDict().AsString("superbundle");
                        ic.SplitSuperBundles.Add(name);

                        SuperBundleInfo sb = s_superBundleMapping[name];
                        SuperBundleInstallChunk sbIc = new(sb, ic, InstallChunkType.Split);
                        s_sbIcMapping.Add(Utils.Utils.HashString(sbIc.Name, true), sbIc);
                        sb.InstallChunks.Add(sbIc);
                    }
                }
            }

            if (installManifest.ContainsKey("settings"))
            {
                BundleFormat = (BundleFormat)installManifest.AsDict("settings").AsLong("bundleFormat", (long)BundleFormat.Dynamic2018);
            }

            foreach (SuperBundleInfo sb in s_superBundleMapping.Values)
            {
                if (sb.InstallChunks.Count > 0)
                {
                    continue;
                }

                if (sb.Name.Contains("loc/", StringComparison.OrdinalIgnoreCase))
                {
                    // TODO: assign correct languageInstallChunk, for now just use DefaultInstallChunk
                    Debug.Assert(DefaultInstallChunk is not null);

                    SuperBundleInstallChunk sbIc = new(sb, DefaultInstallChunk, InstallChunkType.Default);
                    DefaultInstallChunk.SuperBundles.Add(sb.Name);
                    s_sbIcMapping.Add(Utils.Utils.HashString(sbIc.Name, true), sbIc);
                    sb.InstallChunks.Add(sbIc);
                }
                else if (!sb.Name.Contains("debug", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Assert(DefaultInstallChunk is not null);

                    SuperBundleInstallChunk sbIc = new(sb, DefaultInstallChunk, InstallChunkType.Default);
                    DefaultInstallChunk.SuperBundles.Add(sb.Name);
                    s_sbIcMapping.Add(Utils.Utils.HashString(sbIc.Name, true), sbIc);
                    sb.InstallChunks.Add(sbIc);
                }
            }
        }

        return true;
    }
}