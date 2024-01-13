using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Frosty.Sdk;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Sdk;
using Frosty.Sdk.Utils;

namespace FrostyCli;

internal static class Program
{
    private static int Main(string[] args)
    {
        RootCommand rootCommand = new("CLI app to load and mod games made with the Frostbite Engine.");

        AddLoadCommand(rootCommand);

        AddModCommand(rootCommand);

        rootCommand.SetHandler(InteractiveMode);

        return rootCommand.InvokeAsync(args).Result;
    }

    private static void InteractiveMode()
    {
        Logger.LogErrorInternal("Interactive mode not implemented yet");
    }

    private static void AddLoadCommand(RootCommand rootCommand)
    {
        Argument<FileInfo> gameOption = new(
            name: "game-path",
            description: "The path to the game.");

        Option<FileInfo?> keyOption = new(
            name: "--initfs-key",
            description: "The path to a file containing a key for the initfs if needed.");

        Option<int?> sdkOption = new(
            name: "--sdk",
            description: "The pid of the game if a sdk should get generated for the game.");

        Command loadCommand = new("load", "Load a games data from the cache or create it.")
        {
            gameOption,
            keyOption,
            sdkOption
        };
        rootCommand.AddCommand(loadCommand);

        loadCommand.SetHandler(LoadGame, gameOption, keyOption, sdkOption);
    }

    private static void LoadGame(FileInfo inGameFileInfo, FileInfo? inKeyFileInfo, int? inPid)
    {
        if (!inGameFileInfo.Exists)
        {
            Logger.LogErrorInternal("No game exists at that path");
            return;
        }

        // set logger
        FrostyLogger.Logger = new Logger();

        // set base directory to the directory containing the executable
        Utils.BaseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;

        // init profile
        if (!ProfilesLibrary.Initialize(Path.GetFileNameWithoutExtension(inGameFileInfo.Name)))
        {
            return;
        }

        if (ProfilesLibrary.RequiresKey)
        {
            if (inKeyFileInfo is null || !inKeyFileInfo.Exists)
            {
                Logger.LogErrorInternal("Pass in the path to a initfs key file using --initfs-key");
                return;
            }

            KeyManager.AddKey("InitFsKey", File.ReadAllBytes(inKeyFileInfo.FullName));
        }

        if (inGameFileInfo.DirectoryName is null)
        {
            Logger.LogErrorInternal("The game needs to be in a directory containing the games data");
            return;
        }

        // init filesystem manager, this parses the layout.toc file
        if (!FileSystemManager.Initialize(inGameFileInfo.DirectoryName))
        {
            return;
        }

        // generate sdk if needed
        if (!File.Exists(ProfilesLibrary.SdkPath))
        {
            if (!inPid.HasValue)
            {
                Logger.LogErrorInternal("No sdk exists, launch the game and pass in the pid with --pid");
                return;
            }

            TypeSdkGenerator typeSdkGenerator = new();

            Process game = Process.GetProcessById(inPid.Value);

            if (!typeSdkGenerator.DumpTypes(game))
            {
                return;
            }

            if (!typeSdkGenerator.CreateSdk(ProfilesLibrary.SdkPath))
            {
                return;
            }
        }

        // init type library, this loads the EbxTypeSdk used to properly parse ebx assets
        if (!TypeLibrary.Initialize())
        {
            return;
        }

        // init resource manager, this parses the cas.cat files if they exist for easy asset lookup
        if (!ResourceManager.Initialize())
        {
            return;
        }

        // init asset manager, this parses the SuperBundles and loads all the assets
        if (!AssetManager.Initialize())
        {
        }
    }

    private static void AddModCommand(RootCommand rootCommand)
    {
        Argument<FileInfo> gameArg = new(
            name: "game-path",
            description: "The path to the game.");

        Argument<DirectoryInfo> modsArg = new(
            name: "mods-dir",
            description: "The directory containing the mods to generate the data with.");

        Option<DirectoryInfo?> modDataOption = new(
            name: "--mod-data-dir",
            description: "The directory to which the modded data should get generated.");

        Option<FileInfo?> keyOption = new(
            name: "--initfs-key",
            description: "The path to a file containing a key for the initfs if needed.");

        Option<int?> sdkOption = new(
            name: "--sdk",
            description: "The pid of the game if a sdk should get generated for the game.");

        Command loadCommand = new("mod", "todo")
        {
            gameArg,
            modsArg,
            modDataOption,
            keyOption,
            sdkOption
        };
        rootCommand.AddCommand(loadCommand);

        loadCommand.SetHandler(ModGame, gameArg, modsArg, modDataOption, keyOption, sdkOption);
    }

    private static void ModGame(FileInfo inGameFileInfo, DirectoryInfo inModsDirInfo, DirectoryInfo? inModDataDirInfo, FileInfo? inKeyFileInfo, int? inPid)
    {
        // load game
        LoadGame(inGameFileInfo, inKeyFileInfo, inPid);

        Logger.LogErrorInternal("Not implemented yet.");
    }
}