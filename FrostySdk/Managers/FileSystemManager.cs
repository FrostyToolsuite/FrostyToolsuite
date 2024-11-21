using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using Frosty.Sdk.DbObjectElements;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Utils;
using Microsoft.Extensions.Logging;

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
    private static readonly Dictionary<uint, int> s_persistentIndexMapping = new();
    private static readonly Dictionary<int, uint> s_reversePersistentIndexMapping = new();
    private static readonly Dictionary<Guid, int> s_idMapping = new();
    private static readonly List<InstallChunkInfo> s_installChunks = new();
    private static readonly Dictionary<int, SuperBundleInstallChunk> s_sbIcMapping = new();
    private static readonly Dictionary<int, string> s_casFiles = new();

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
    /// Tries to resolve the relative path for the best source (patch -> dlc -> base)
    /// </summary>
    /// <param name="inPath">The relative path to a file or dictionary.</param>
    /// <param name="resolvedPath">The resolved full path.</param>
    /// <returns>True if it could resolve the path, false if it couldn't resolve it.</returns>
    public static bool TryResolvePath(string inPath, [NotNullWhen(true)] out string? resolvedPath)
    {
        foreach (FileSystemSource source in Sources)
        {
            if (source.TryResolvePath(inPath, out resolvedPath))
            {
                return true;
            }
        }

        resolvedPath = null;
        return false;
    }

    /// <summary>
    /// Resolves the relative path, it doesn't check if the directory or file exists.
    /// </summary>
    /// <param name="isPatch">A Boolean if the path is a patch path or not.</param>
    /// <param name="inPath">The relative path to resolve.</param>
    /// <returns>The resolved full path. This path doesn't have to exist.</returns>
    public static string ResolvePath(bool isPatch, string inPath)
    {
        if (isPatch)
        {
            return FileSystemSource.Patch.ResolvePath(inPath);
        }

        foreach (FileSystemSource source in Sources)
        {
            if (source.Path == FileSystemSource.Patch.Path)
            {
                continue;
            }

            if (source.TryResolvePath(inPath, out string? path))
            {
                return path;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Tries to resolve the relative path.
    /// </summary>
    /// <param name="isPatch">A Boolean if the path is a patch path or not.</param>
    /// <param name="inPath">The relative path to resolve.</param>
    /// <param name="resolvedPath">The resolved full path or null if it couldn't resolve the path.</param>
    /// <returns>True if it could resolve the path, false if it couldn't resolve it.</returns>
    public static bool TryResolvePath(bool isPatch, string inPath, [NotNullWhen(true)] out string? resolvedPath)
    {
        if (isPatch)
        {
            return FileSystemSource.Patch.TryResolvePath(inPath, out resolvedPath);
        }

        foreach (FileSystemSource source in Sources)
        {
            if (source.Path == FileSystemSource.Patch.Path)
            {
                continue;
            }

            if (source.TryResolvePath(inPath, out resolvedPath))
            {
                return true;
            }
        }

        resolvedPath = null;

        return false;
    }

    /// <summary>
    /// Gets the full path to a cas file.
    /// </summary>
    /// <param name="casFileIdentifier">The <see cref="CasFileIdentifier"/> for the cas file.</param>
    /// <returns>The full path to the cas file.</returns>
    public static string GetFilePath(CasFileIdentifier casFileIdentifier)
    {
        InstallChunkInfo installChunkInfo = s_installChunks[s_persistentIndexMapping[casFileIdentifier.InstallChunkIndex]];
        if (casFileIdentifier.IsPatch)
        {
            return FileSystemSource.Patch.ResolvePath(Path.Combine(installChunkInfo.InstallBundle,
                $"cas_{casFileIdentifier.CasIndex:D2}.cas"));
        }

        return FileSystemSource.Base.ResolvePath(Path.Combine(installChunkInfo.InstallBundle,
            $"cas_{casFileIdentifier.CasIndex:D2}.cas"));
    }

    /// <summary>
    /// Gets the full path to a cas file.
    /// </summary>
    /// <param name="casIndex">The index of the cas file in the layout.toc.</param>
    /// <returns>The full path to the cas file.</returns>
    public static string GetFilePath(int casIndex)
    {
        return s_casFiles[casIndex];
    }

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

    public static InstallChunkInfo GetInstallChunkInfo(uint index)
    {
        return s_installChunks[s_persistentIndexMapping[index]];
    }

    public static InstallChunkInfo GetInstallChunkInfo(Guid id)
    {
        return s_installChunks[s_idMapping[id]];
    }

    public static uint GetInstallChunkIndex(InstallChunkInfo info)
    {
        return s_reversePersistentIndexMapping[s_idMapping[info.Id]];
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
            byte[]? encrypted = initFs.AsDict().AsBlob("encrypted", null);
            if (encrypted is null)
            {
                return false;
            }

            if (!KeyManager.HasKey("InitFsKey"))
            {
                return false;
            }

            using (BlockStream stream = new(new Block<byte>(encrypted)))
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
                s_sbIcMapping.Add(sbIc.Id, sbIc);
                sb.InstallChunks.Add(sbIc);
            }

            s_installChunks.Add(ic);
            s_persistentIndexMapping.Add(0, 0);
            s_reversePersistentIndexMapping.Add(0, 0);
            s_idMapping.Add(ic.Id, 0);
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

                uint index = installChunk.AsDict().AsUInt("persistentIndex", (uint)s_installChunks.Count);
                s_persistentIndexMapping.Add(index, s_installChunks.Count);
                s_reversePersistentIndexMapping.Add(s_installChunks.Count, index);
                s_idMapping.Add(ic.Id, s_installChunks.Count);
                s_installChunks.Add(ic);

                foreach (DbObject superBundle in installChunk.AsDict().AsList("superbundles"))
                {
                    string name = superBundle.AsString();
                    ic.SuperBundles.Add(name);

                    SuperBundleInfo sb = s_superBundleMapping[name];
                    SuperBundleInstallChunk sbIc = new(sb, ic, InstallChunkType.Default);
                    s_sbIcMapping.Add(sbIc.Id, sbIc);
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
                        casPath = casPath.Replace("native_data", BasePath);
                        casPath = casPath.Replace("native_data", BasePath);

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
                        s_sbIcMapping.Add(sbIc.Id, sbIc);
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
                        s_sbIcMapping.Add(sbIc.Id, sbIc);
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
                    s_sbIcMapping.Add(sbIc.Id, sbIc);
                    sb.InstallChunks.Add(sbIc);
                }
                else if (!sb.Name.Contains("debug", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Assert(DefaultInstallChunk is not null);

                    SuperBundleInstallChunk sbIc = new(sb, DefaultInstallChunk, InstallChunkType.Default);
                    DefaultInstallChunk.SuperBundles.Add(sb.Name);
                    s_sbIcMapping.Add(sbIc.Id, sbIc);
                    sb.InstallChunks.Add(sbIc);
                }
            }
        }

        return true;
    }
}