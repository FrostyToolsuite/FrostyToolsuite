using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Frosty.ModSupport;
using Frosty.ModSupport.Mod;
using Frosty.Sdk.Managers;
using FrostyCli.Project;

namespace FrostyCli;

internal static partial class Program
{
    private static void InteractiveModGame()
    {
        DirectoryInfo modsDirInfo =
            RequestDirectory("Pass in the path to a folder containing the mods that you want to apply");
        if (!modsDirInfo.Exists)
        {
            Logger.LogErrorInternal("Mods folder does not exist.");
            return;
        }

        DirectoryInfo modDataDirInfo =
            RequestDirectory("Pass in the path to a folder where the generated data should get stored in", true);

        ModGame(modsDirInfo, modDataDirInfo);
    }

    private static void ModGame(DirectoryInfo inModsDirInfo, DirectoryInfo inModDataDirInfo)
    {
        IEnumerable<string> mods = Directory.GetFiles(inModsDirInfo.FullName);

        FileInfo modLoadOrderPath = new(Path.Combine(inModsDirInfo.FullName, "load_order.json"));
        if (modLoadOrderPath.Exists)
        {
            using FileStream stream = modLoadOrderPath.OpenRead();
            List<string>? loadOrder = JsonSerializer.Deserialize<List<string>>(stream);
            if (loadOrder is not null)
            {
                mods = loadOrder;
            }
        }
        else
        {
            using FileStream stream = modLoadOrderPath.OpenWrite();
            JsonSerializer.Serialize(stream, mods, new JsonSerializerOptions { WriteIndented = true });
        }

        FrostyModExecutor executor = new();
        executor.GenerateMods(inModDataDirInfo.FullName, mods);
    }

    private static void ModGameCommand(FileInfo inGameFileInfo, DirectoryInfo inModsDirInfo,
        DirectoryInfo? inModDataDirInfo, FileInfo? inKeyFileInfo, int? inPid)
    {
        // load game
        LoadGameCommand(inGameFileInfo, inKeyFileInfo, inPid);

        if (!inModsDirInfo.Exists)
        {
            Logger.LogErrorInternal($"Directory {inModsDirInfo.FullName} doesnt exist.");
            return;
        }

        ModGame(inModsDirInfo,
            inModDataDirInfo ?? new DirectoryInfo(Path.Combine(FileSystemManager.BasePath, "ModData", "Default")));
    }

    private static void InteractiveUpdateMod()
    {
        FileInfo? modFileInfo = RequestFile("Pass in the path to the mod that should get updated");
        if (modFileInfo?.Exists != true)
        {
            Logger.LogErrorInternal("Mod file does not exist.");
            return;
        }

        FileInfo? output = RequestFile("Pass in the path where the updated mod should get saved to", true,
            modFileInfo.FullName);

        if (output is null)
        {
            return;
        }

        ModUpdater.UpdateMod(modFileInfo.FullName, output.FullName);
    }

    private static void UpdateModCommand(FileInfo inGameFileInfo, FileInfo inModFileInfo, string? inOutputPath,
        FileInfo? inKeyFileInfo, int? inPid)
    {
        if (!inModFileInfo.Exists)
        {
            Logger.LogErrorInternal("Mod file does not exist.");
            return;
        }

        // load game
        LoadGameCommand(inGameFileInfo, inKeyFileInfo, inPid);

        FileInfo? output;
        if (!string.IsNullOrEmpty(inOutputPath))
        {
            output = GetFile(inOutputPath, true, inModFileInfo.FullName);
            if (output is null)
            {
                return;
            }
        }
        else
        {
            output = inModFileInfo;
        }

        ModUpdater.UpdateMod(inModFileInfo.FullName, output.FullName);
    }

    private static void InteractiveCreateMod()
    {
        DirectoryInfo projectDirectory = RequestDirectory("Pass in the path to the project directory");
        if (!projectDirectory.Exists)
        {
            Logger.LogErrorInternal("Project directory does not exist.");
            return;
        }

        FileInfo? outputFile = RequestFile("Input the path where the updated mod should get saved to", true,
            $"{projectDirectory.Name}.fbmod");

        if (outputFile is null)
        {
            return;
        }

        CreateMod(projectDirectory, outputFile);
    }

    private static void CreateModCommand(FileInfo inGameFileInfo, DirectoryInfo inProjectDirInfo, string? inOutputPath,
        FileInfo? inKeyFileInfo, int? inPid)
    {
        // load game
        LoadGameCommand(inGameFileInfo, inKeyFileInfo, inPid);

        if (!inProjectDirInfo.Exists)
        {
            Logger.LogErrorInternal("Project directory does not exist.");
            return;
        }

        if (string.IsNullOrEmpty(inOutputPath))
        {
            inOutputPath = inProjectDirInfo.FullName;
        }

        FileInfo? outputFile = GetFile(inOutputPath, true, $"{inProjectDirInfo.Name}.fbmod");

        if (outputFile is null)
        {
            return;
        }

        CreateMod(inProjectDirInfo, outputFile);
    }

    private static void CreateMod(DirectoryInfo inProjectDirInfo, FileInfo inOutputFileInfo)
    {
        FileInfo path = new(Path.Combine(inProjectDirInfo.FullName, "project.json"));
        if (!path.Exists)
        {
            Logger.LogErrorInternal("Project directory does not contain project.json file.");
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
            Logger.LogErrorInternal("Failed to load project.json, maybe not a correct json file.");
            return;
        }

        project.BasePath = path.DirectoryName ?? string.Empty;

        project.CompileToMod(inOutputFileInfo.FullName);
    }
}