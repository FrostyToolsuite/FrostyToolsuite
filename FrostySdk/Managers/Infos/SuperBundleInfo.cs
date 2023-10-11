using System;
using System.Collections.Generic;

namespace Frosty.Sdk.Managers.Infos;

public class SuperBundleInfo
{
    [Flags]
    public enum LegacyFlags
    {
        None = 0,
        Base = 1 << 0,
        Same = 1 << 1,
        Delta = 1 << 2
    }
    
    public string Name { get; }
    public LegacyFlags Flags { get; private set; }
    public List<SuperBundleInstallChunk> InstallChunks { get; }

    public SuperBundleInfo(string inName)
    {
        Name = inName;
        InstallChunks = new List<SuperBundleInstallChunk>();

    }

    public void SetLegacyFlags(bool inBase, bool inSame, bool inDelta)
    {
        Flags = (inBase ? LegacyFlags.Base : LegacyFlags.None) | (inSame ? LegacyFlags.Same : LegacyFlags.None) |
               (inDelta ? LegacyFlags.Delta : LegacyFlags.None);
    }
}