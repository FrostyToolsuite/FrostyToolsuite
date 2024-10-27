using Frosty.ModSupport.Archive;
using Frosty.ModSupport.ModInfos;
using Frosty.Sdk.Managers.Infos;
using System;

namespace Frosty.ModSupport;

internal class SuperBundleManifest : IDisposable
{
    public void Dispose()
    {
        // TODO release managed resources here
    }
}

public partial class FrostyModExecutor
{
    private void ModSuperBundleManifest(SuperBundleInstallChunk inSbIc, SuperBundleModInfo inModInfo,
        InstallChunkWriter inInstallChunkWriter)
    {
    }
}