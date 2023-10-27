using Frosty.ModSupport.ModInfos;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Infos;

namespace Frosty.ModSupport;

public partial class FrostyModExecutor
{
    private void ModManifest2019(SuperBundleInstallChunk inSbIc, SuperBundleModInfo inModInfo)
    {   
        string path = FileSystemManager.ResolvePath(inSbIc.Name);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        
        
    }
}