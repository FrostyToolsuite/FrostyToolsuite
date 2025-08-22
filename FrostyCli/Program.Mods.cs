using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Frosty.ModSupport;
using Frosty.ModSupport.Mod;
using Frosty.Sdk;
using Frosty.Sdk.Managers;
using FrostyCli.Project;
using Microsoft.Extensions.Logging;

namespace FrostyCli;

internal static partial class Program
{
    private static void ModGame(FileInfo? inGameFileInfo = null, int? inPid = null,
        FileInfo? inInitFsKeyFileInfo = null, FileInfo? inBundleKeyFileInfo = null, FileInfo? inCasKeyFileInfo = null,
        DirectoryInfo? inModsDirInfo = null, DirectoryInfo? inModDataDirInfo = null)
    {
        if (!s_isInteractive)
        {
            if (!LoadGame(inGameFileInfo, inPid, inInitFsKeyFileInfo, inBundleKeyFileInfo, inCasKeyFileInfo))
            {
                return;
            }
        }

        inModsDirInfo ??= RequestDirectory("Pass in the path to a folder containing the mods that you want to apply");
        inModDataDirInfo ??= s_isInteractive
            ? RequestDirectory("Pass in the path to a folder where the generated data should get stored in", true)
            : new DirectoryInfo(Path.Combine(FileSystemManager.BasePath, "ModData", "Default"));

        IEnumerable<string> mods = Directory.GetFiles(inModsDirInfo.FullName);

        FileInfo modLoadOrderPath = new(Path.Combine(inModsDirInfo.FullName, "load_order.json"));
        if (modLoadOrderPath.Exists)
        {
            using FileStream stream = modLoadOrderPath.OpenRead();
            List<string>? loadOrder = JsonSerializer.Deserialize<List<string>>(stream);
            if (loadOrder is not null)
            {
                if (!loadOrder.All(File.Exists))
                {
                    FrostyLogger.Logger?.LogError("load_order.json contains invalid paths, ignoring the load order");
                }
                else
                {
                    mods = loadOrder;
                }
            }
        }

        FrostyModExecutor executor = new();
        Errors error;
        if ((error = executor.GenerateMods(inModDataDirInfo.FullName, mods)) != Errors.Success)
        {
            FrostyLogger.Logger?.LogError("Failed to generate mod data: {}", error);
        }
    }

    private static void UpdateMod(FileInfo? inGameFileInfo = null, int? inPid = null,
        FileInfo? inInitFsKeyFileInfo = null, FileInfo? inBundleKeyFileInfo = null, FileInfo? inCasKeyFileInfo = null,
        FileInfo? inModFileInfo = null, string? inOutputPath = null)
    {
        if (!s_isInteractive)
        {
            LoadGame(inGameFileInfo, inPid, inInitFsKeyFileInfo, inBundleKeyFileInfo, inCasKeyFileInfo);
        }

        inModFileInfo ??= RequestFile("Pass in the path to the mod that should get updated");
        if (inModFileInfo?.Exists != true)
        {
            FrostyLogger.Logger?.LogError("Mod file does not exist.");
            return;
        }

        FileInfo? output;
        if (string.IsNullOrEmpty(inOutputPath))
        {
            output = s_isInteractive
                ? RequestFile("Pass in the path where the updated mod should get saved to", true,
                    inModFileInfo.Name)
                : inModFileInfo;
        }
        else
        {
            output = GetFile(inOutputPath, true, inModFileInfo.FullName);
        }

        if (output is null)
        {
            return;
        }

        ModUpdater.UpdateMod(inModFileInfo.FullName, output.FullName);
    }

    private static void CreateMod(FileInfo? inGameFileInfo = null, int? inPid = null,
        FileInfo? inInitFsKeyFileInfo = null, FileInfo? inBundleKeyFileInfo = null, FileInfo? inCasKeyFileInfo = null,
        DirectoryInfo? inProjectDirInfo = null, string? inOutputPath = null)
    {
        if (!s_isInteractive)
        {
            LoadGame(inGameFileInfo, inPid, inInitFsKeyFileInfo, inBundleKeyFileInfo, inCasKeyFileInfo);
        }

        inProjectDirInfo ??= RequestDirectory("Pass in the path to the project directory");

        FileInfo path = new(Path.Combine(inProjectDirInfo.FullName, "project.json"));
        if (!path.Exists)
        {
            FrostyLogger.Logger?.LogError("Project directory does not contain project.json file.");
            return;
        }

        FrostyProject? project;
        using (FileStream stream = path.OpenRead())
        {
            try
            {
                project = JsonSerializer.Deserialize<FrostyProject>(stream);
            }
            catch
            {
                project = null;
            }
        }

        if (project is null)
        {
            FrostyLogger.Logger?.LogError("Failed to load project.json, maybe not a correct json file.");
            return;
        }

        project.BasePath = path.DirectoryName ?? string.Empty;

        FileInfo? output;
        if (string.IsNullOrEmpty(inOutputPath))
        {
            output = s_isInteractive
                ? RequestFile("Input the path where the updated mod should get saved to", true,
                    $"{inProjectDirInfo.Name}.fbmod")
                : new FileInfo($"{inProjectDirInfo.FullName}.fbmod");
        }
        else
        {
            output = GetFile(inOutputPath, true, $"{inProjectDirInfo.Name}.fbmod");
        }

        if (output is null)
        {
            return;
        }

        project.CompileToMod(output.FullName);
    }
}