using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.CatResources;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Managers.Infos.FileInfos;

namespace Frosty.Sdk.Managers;

public static class ResourceManager
{
    public static bool IsInitialized { get; private set; }

    private static readonly Dictionary<Sha1, CasFileInfo> s_resourceEntries = new();

    private static readonly List<CatPatchEntry> s_patchEntries = new();

    private static readonly Dictionary<Sha1, uint> s_sizeMap = new();

    public static void LoadInstallChunks()
    {
        foreach (InstallChunkInfo installChunkInfo in FileSystemManager.EnumerateInstallChunks())
        {
            LoadInstallChunk(installChunkInfo);
        }
    }

    public static void CLearInstallChunks()
    {
        s_resourceEntries.Clear();
        s_patchEntries.Clear();
    }

    public static bool Initialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        if (!FileSystemManager.IsInitialized)
        {
            FrostyLogger.Logger?.LogError("FileSystemManager not initialized yet");
            return false;
        }

        if (FileSystemManager.HasFileInMemoryFs("Scripts/CasEncrypt.yaml"))
        {
            // load CasEncrypt.yaml from memoryFs (used for decrypting data in cas files)
            using (TextReader stream = new StreamReader(FileSystemManager.GetFileFromMemoryFs("Scripts/CasEncrypt.yaml").ToStream()))
            {
                byte[]? key = null;
                while (stream.Peek() != -1)
                {
                    string line = stream.ReadLine()!;
                    if (line.Contains("keyid:"))
                    {
                        string[] arr = line.Split(':');
                        KeyManager.AddKey(arr[1].Trim(), key!);
                    }
                    else if (line.Contains("key:"))
                    {
                        string[] arr = line.Split(':');
                        string keyStr = arr[1].Trim();

                        key = new byte[keyStr.Length / 2];
                        for(int i = 0; i < keyStr.Length / 2; i++)
                        {
                            key[i] = Convert.ToByte(keyStr.Substring(i * 2, 2), 16);
                        }
                    }
                }
            }
        }

        foreach (string libOodle in Directory.EnumerateFiles(FileSystemManager.BasePath, "oo2core_*"))
        {
            string thirdPartyPath = Path.Combine(Utils.Utils.BaseDirectory, "ThirdParty");
            Directory.CreateDirectory(thirdPartyPath);

            string ext = Path.GetExtension(libOodle);
            string path = Path.Combine(thirdPartyPath, $"oo2core{ext}");
            File.Delete(path);
            File.CreateSymbolicLink(path, libOodle);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && ext == ".dll")
            {
                string oodleHack = Path.Combine(thirdPartyPath, "oo2core.so");
                File.Delete(oodleHack);
                File.CreateSymbolicLink(oodleHack, Path.Combine(thirdPartyPath, "liblinoodle.so"));
            }

            break;
        }

        IsInitialized = true;
        return true;
    }

    public static long GetSize(Sha1 sha1)
    {
        return s_sizeMap.TryGetValue(sha1, out uint size) ? size : -1;
    }

    public static CasFileInfo? GetPatchFileInfo(Sha1 sha1, Sha1 deltaSha1, Sha1 baseSha1)
    {
        if (s_resourceEntries.TryGetValue(sha1, out CasFileInfo? fileInfo))
        {
            return fileInfo;
        }

        CasFileInfo? baseInfo = s_resourceEntries.GetValueOrDefault(baseSha1);
        CasFileInfo? deltaInfo = s_resourceEntries.GetValueOrDefault(deltaSha1);

        if (baseInfo is null || deltaInfo is null)
        {
            return null;
        }

        fileInfo = new CasFileInfo(baseInfo.GetBase(), deltaInfo.GetBase());

        s_resourceEntries.TryAdd(sha1, fileInfo);

        return fileInfo;
    }

    public static CasFileInfo? GetFileInfo(Sha1 sha1)
    {
        return s_resourceEntries.GetValueOrDefault(sha1);
    }

    private static void LoadInstallChunk(InstallChunkInfo info)
    {
        Dictionary<Sha1, CasFileInfo> infos = new();
        foreach (FileSystemSource source in FileSystemManager.Sources)
        {
            LoadEntries(info, source, infos);
        }

        foreach (CatPatchEntry entry in s_patchEntries)
        {
            infos.TryGetValue(entry.BaseSha1, out CasFileInfo? baseFileInfo);

            infos.TryGetValue(entry.DeltaSha1, out CasFileInfo? deltaFileInfo);

            Debug.Assert(deltaFileInfo is not null, "No delta entry!");

            CasFileInfo fileInfo = new(baseFileInfo?.GetBase(), deltaFileInfo?.GetBase());
            s_resourceEntries.TryAdd(entry.Sha1, fileInfo);
        }
        s_patchEntries.Clear();
    }

    private static void LoadEntries(InstallChunkInfo info, FileSystemSource inSource,
        Dictionary<Sha1, CasFileInfo> retVal)
    {
        if (!inSource.TryResolvePath(Path.Combine(info.InstallBundle, "cas.cat"), out string? filePath))
        {
            return;
        }

        int installChunkIndex = FileSystemManager.GetInstallChunkIndex(info);
        bool patch = inSource.Path == FileSystemSource.Patch.Path;

        using (CatStream stream = new(filePath))
        {
            for (int i = 0; i < stream.ResourceCount; i++)
            {
                CatResourceEntry entry = stream.ReadResourceEntry();
                CasFileIdentifier casFileIdentifier = new(patch, installChunkIndex, entry.ArchiveIndex);

                CasFileInfo fileInfo = new(casFileIdentifier, entry.Offset, entry.Size, entry.LogicalOffset);

                if (!s_resourceEntries.TryAdd(entry.Sha1, fileInfo) && fileInfo.IsComplete() && !s_resourceEntries[entry.Sha1].IsComplete())
                {
                    s_resourceEntries[entry.Sha1] = fileInfo;
                }

                if (!s_sizeMap.TryAdd(entry.Sha1, entry.Size))
                {
                    s_sizeMap[entry.Sha1] = Math.Max(s_sizeMap[entry.Sha1], entry.Size);
                }

                retVal.TryAdd(entry.Sha1, fileInfo);
            }

            for (int i = 0; i < stream.EncryptedCount; i++)
            {
                CatResourceEntry entry = stream.ReadEncryptedEntry();
                CasFileIdentifier casFileIdentifier = new(patch, installChunkIndex, entry.ArchiveIndex);

                CasFileInfo fileInfo = new(casFileIdentifier, entry.Offset, entry.Size, entry.LogicalOffset,
                    entry.KeyId);

                if (!s_resourceEntries.TryAdd(entry.Sha1, fileInfo) && fileInfo.IsComplete() && !s_resourceEntries[entry.Sha1].IsComplete())
                {
                    s_resourceEntries[entry.Sha1] = fileInfo;
                }

                if (!s_sizeMap.TryAdd(entry.Sha1, entry.Size))
                {
                    s_sizeMap[entry.Sha1] = Math.Max(s_sizeMap[entry.Sha1], entry.Size);
                }

                retVal.TryAdd(entry.Sha1, fileInfo);
            }

            for (int i = 0; i < stream.PatchCount; i++)
            {
                CatPatchEntry entry = stream.ReadPatchEntry();
                s_patchEntries.Add(entry);
            }
        }
    }
}