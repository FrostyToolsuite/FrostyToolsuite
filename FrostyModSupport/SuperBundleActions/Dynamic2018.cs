using System.Diagnostics;
using Frosty.ModSupport.Archive;
using Frosty.ModSupport.ModEntries;
using Frosty.ModSupport.ModInfos;
using Frosty.Sdk;
using Frosty.Sdk.DbObjectElements;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Utils;

namespace Frosty.ModSupport;

internal class Dynamic2018 : IDisposable
{
    public Block<byte>? TocData { get; private set; }
    public Block<byte>? SbData { get; private set; }

    private readonly Dictionary<string, EbxModEntry> m_modifiedEbx;
    private readonly Dictionary<string, ResModEntry> m_modifiedRes;
    private readonly Dictionary<Guid, ChunkModEntry> m_modifiedChunks;

    public Dynamic2018(Dictionary<string, EbxModEntry> inModifiedEbx,
        Dictionary<string, ResModEntry> inModifiedRes, Dictionary<Guid, ChunkModEntry> inModifiedChunks)
    {
        m_modifiedEbx = inModifiedEbx;
        m_modifiedRes = inModifiedRes;
        m_modifiedChunks = inModifiedChunks;
    }

    public void ModSuperBundle(string inPath, bool inCreateNewPatch, SuperBundleInstallChunk inSbIc,
        SuperBundleModInfo inModInfo, InstallChunkWriter inInstallChunkWriter)
    {

    }

    private void ProcessBundles(string inPath, bool inOnlyUseModifiedBundles, SuperBundleInstallChunk inSbIc,
    SuperBundleModInfo inModInfo, InstallChunkWriter inInstallChunkWriter)
    {
        var toc = DbObject.Deserialize(inPath)?.AsDict();
        if (toc is null)
        {
            Debug.Assert(false, "We should not be here");
            return;
        }

        // check for format flags
        bool isCas = toc.AsBoolean("cas");
        bool isDas = toc.AsBoolean("das");

        // path to sb file
        string sbPath = inPath.Replace(".toc", ".sb");

        // load bundles
        if (toc.ContainsKey("bundles"))
        {
            if (isDas)
            {
                throw new NotImplementedException();
            }
            else
            {
                foreach (DbObject obj in toc.AsList("bundles"))
                {
                    DbObjectDict bundleObj = obj.AsDict();

                    string id = bundleObj.AsString("id");

                    long offset = bundleObj.AsLong("offset");
                    long size = bundleObj.AsLong("size");

                    bool isDelta = bundleObj.AsBoolean("delta");
                    bool isBase = bundleObj.AsBoolean("base");
                }
            }
        }
    }

    public void Dispose()
    {
        TocData?.Dispose();
        SbData?.Dispose();
    }
}

public partial class FrostyModExecutor
{
    private void ModDynamic2018(SuperBundleInstallChunk inSbIc, SuperBundleModInfo inModInfo, InstallChunkWriter inInstallChunkWriter)
    {
        bool createNewPatch = false;
        string? tocPath = Path.Combine(m_gamePatchPath, $"{inSbIc.Name}.toc");
        if (!File.Exists(tocPath))
        {
            if (!FileSystemManager.TryResolvePath(false, $"{inSbIc.Name}.toc", out tocPath))
            {
                FrostyLogger.Logger?.LogError("Trying to mod SuperBundle that doesnt exist");
                return;
            }

            createNewPatch = true;
        }

        using (Dynamic2018 action = new(m_modifiedEbx, m_modifiedRes, m_modifiedChunks))
        {
            action.ModSuperBundle(tocPath, createNewPatch, inSbIc, inModInfo, inInstallChunkWriter);

            FileInfo modifiedToc = new(Path.Combine(m_modDataPath, $"{inSbIc.Name}.toc"));
            Directory.CreateDirectory(modifiedToc.DirectoryName!);

            using (FileStream stream = new(modifiedToc.FullName, FileMode.Create, FileAccess.Write))
            {
                ObfuscationHeader.Write(stream);
                stream.Write(action.TocData!);
                action.TocData!.Dispose();
            }

            if (action.SbData is not null)
            {
                using (FileStream stream = new(modifiedToc.FullName.Replace(".toc", ".sb"), FileMode.Create, FileAccess.Write))
                {
                    stream.Write(action.SbData);
                    action.SbData.Dispose();
                }
            }
            else
            {
                // if the sb exists, but we just didnt modify it, create a symbolic link for it
                string sbPath = tocPath.Replace(".toc", ".sb");
                if (File.Exists(sbPath))
                {
                    File.CreateSymbolicLink(modifiedToc.FullName.Replace(".toc", ".sb"), sbPath);
                }
            }
        }
    }
}