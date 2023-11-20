using System;
using System.Collections.Generic;

namespace Frosty.Sdk.Managers.Infos;

public class SuperBundleInstallChunk
{
    public string Name { get; }

    public SuperBundleInfo SuperBundle { get; }

    public InstallChunkInfo InstallChunk { get; }

    public InstallChunkType Type { get; }

    public readonly Dictionary<string, BundleInfo> BundleMapping = new(StringComparer.OrdinalIgnoreCase);

    public SuperBundleInstallChunk(SuperBundleInfo inSuperBundle, InstallChunkInfo inInstallChunk, InstallChunkType inType)
    {
        SuperBundle = inSuperBundle;
        InstallChunk = inInstallChunk;
        Type = inType;

        Name = Type == InstallChunkType.Split
            ? $"{InstallChunk.InstallBundle}{SuperBundle.Name[SuperBundle.Name.IndexOf('/')..]}"
            : SuperBundle.Name;
    }
}