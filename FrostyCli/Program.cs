using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using Frosty.Sdk;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Sdk;
using Frosty.Sdk.Utils;
using FrostyCli.Shaders.Rvm;
using Sharprompt;

namespace FrostyCli;

internal static partial class Program
{
    private static int Main(string[] args)
    {
        RootCommand rootCommand = new("CLI app to load and mod games made with the Frostbite Engine.");

        AddLoadCommand(rootCommand);

        AddModCommand(rootCommand);

        AddUpdateModCommand(rootCommand);

        AddCreateModCommand(rootCommand);

        rootCommand.SetHandler(InteractiveMode);

        return rootCommand.InvokeAsync(args).Result;
    }

    private static void InteractiveMode()
    {
        FileInfo? game = RequestFile("Input the path to the games executable", false);

        if (game is null)
        {
            return;
        }

        if (!game.Exists)
        {
            Logger.LogErrorInternal("Game does not exist.");
            return;
        }

        LoadGame(game);

        ActionType actionType;
        do
        {
             switch (actionType = Prompt.Select<ActionType>("Select what you want to do"))
             {
                 case ActionType.Quit:
                     if (FrostyLogger.Logger is Logger logger)
                     {
                         logger.StopLogging();
                     }
                     break;
                 case ActionType.Mod:
                     InteractiveModGame();
                     break;
                 case ActionType.UpdateMod:
                     InteractiveUpdateMod();
                     break;
                 case ActionType.CreateMod:
                     InteractiveCreateMod();
                     break;
                 case ActionType.ListEbx:
                     ListEbx();
                     break;
                 case ActionType.ListRes:
                     ListRes();
                     break;
                 case ActionType.ListChunks:
                     ListChunks();
                     break;
                 case ActionType.DumpEbx:
                     InteractiveDumpEbx();
                     break;
                 case ActionType.DumpRes:
                     InteractiveDumpRes();
                     break;
                 case ActionType.DumpChunks:
                     InteractiveDumpChunks();
                     break;
                 case ActionType.ExportEbx:
                     InteractiveExportEbx();
                     break;
                 case ActionType.ExportRes:
                     InteractiveExportRes();
                     break;
                 case ActionType.ExportChunk:
                     InteractiveExportChunk();
                     break;
             }
        } while (actionType != ActionType.Quit);

    }

    private enum ActionType
    {
        Quit,
        Mod,
        UpdateMod,
        CreateMod,
        ListEbx,
        ListRes,
        ListChunks,
        DumpEbx,
        DumpRes,
        DumpChunks,
        ExportEbx,
        ExportRes,
        ExportChunk,
    }

    private static void LoadGame(FileInfo inGameFileInfo)
    {
        if (!inGameFileInfo.Exists)
        {
            Logger.LogErrorInternal("No game exists at that path.");
            return;
        }

        // set logger
        FrostyLogger.Logger = new Logger();

        // set base directory to the directory containing the executable
        Utils.BaseDirectory = Path.GetDirectoryName(AppContext.BaseDirectory) ?? string.Empty;

        // init profile
        if (!ProfilesLibrary.Initialize(Path.GetFileNameWithoutExtension(inGameFileInfo.Name)))
        {
            return;
        }

        if (ProfilesLibrary.RequiresInitFsKey)
        {
            string keyPath = Prompt.Input<string>("Pass in the path to an initfs key");

            FileInfo keyFileInfo = new(keyPath);

            if (!keyFileInfo.Exists)
            {
                Logger.LogErrorInternal("Key does not exist.");
                return;
            }

            KeyManager.AddKey("InitFsKey", File.ReadAllBytes(keyFileInfo.FullName));
        }

        if (ProfilesLibrary.RequiresBundleKey)
        {
            string keyPath = Prompt.Input<string>("Pass in the path to an bundle key");

            FileInfo keyFileInfo = new(keyPath);

            if (!keyFileInfo.Exists)
            {
                Logger.LogErrorInternal("Key does not exist.");
                return;
            }

            KeyManager.AddKey("BundleEncryptionKey", File.ReadAllBytes(keyFileInfo.FullName));
        }

        if (ProfilesLibrary.RequiresCasKey)
        {
            string keyPath = Prompt.Input<string>("Pass in the path to an cas key");

            FileInfo keyFileInfo = new(keyPath);

            if (!keyFileInfo.Exists)
            {
                Logger.LogErrorInternal("Key does not exist.");
                return;
            }

            KeyManager.AddKey("CasObfuscationKey", File.ReadAllBytes(keyFileInfo.FullName));
        }

        if (inGameFileInfo.DirectoryName is null)
        {
            Logger.LogErrorInternal("The game needs to be in a directory containing the games data.");
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
            int pid = Prompt.Input<int>("Input pid of the currently running game");

            TypeSdkGenerator typeSdkGenerator = new();

            Process game = Process.GetProcessById(pid);

            if (!typeSdkGenerator.DumpTypes(game))
            {
                return;
            }

            Logger.LogInfoInternal("The game is not needed anymore and can be closed.");
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

    private static void ListEbx()
    {
        foreach (EbxAssetEntry entry in AssetManager.EnumerateEbxAssetEntries())
        {
            Console.WriteLine(entry.Name);
        }
    }

    private static void ListRes()
    {
        foreach (ResAssetEntry entry in AssetManager.EnumerateResAssetEntries())
        {
            Console.WriteLine(entry.Name);
        }
    }

    private static void ListChunks()
    {
        foreach (ChunkAssetEntry entry in AssetManager.EnumerateChunkAssetEntries())
        {
            Console.WriteLine(entry.Name);
        }
    }

    private static void LoadGameCommand(FileInfo inGameFileInfo, FileInfo? inKeyFileInfo, int? inPid)
    {
        if (!inGameFileInfo.Exists)
        {
            Logger.LogErrorInternal("No game exists at that path.");
            return;
        }

        // set logger
        FrostyLogger.Logger = new Logger();

        // set base directory to the directory containing the executable
        Utils.BaseDirectory = Path.GetDirectoryName(AppContext.BaseDirectory) ?? string.Empty;

        // init profile
        if (!ProfilesLibrary.Initialize(Path.GetFileNameWithoutExtension(inGameFileInfo.Name)))
        {
            return;
        }

        if (ProfilesLibrary.RequiresInitFsKey)
        {
            if (inKeyFileInfo is null || !inKeyFileInfo.Exists)
            {
                Logger.LogErrorInternal("Pass in the path to a initfs key file using --initfs-key.");
                return;
            }

            KeyManager.AddKey("InitFsKey", File.ReadAllBytes(inKeyFileInfo.FullName));
        }

        if (inGameFileInfo.DirectoryName is null)
        {
            Logger.LogErrorInternal("The game needs to be in a directory containing the games data.");
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
                Logger.LogErrorInternal("No sdk exists, launch the game and pass in the pid with --pid.");
                return;
            }

            TypeSdkGenerator typeSdkGenerator = new();

            Process game = Process.GetProcessById(inPid.Value);

            if (!typeSdkGenerator.DumpTypes(game))
            {
                return;
            }

            Logger.LogInfoInternal("The game is not needed anymore and can be closed.");
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
        AssetManager.Initialize();
    }

    private static FileInfo? RequestFile(string inMessage, bool inCreateDirectory = false, string? inDefaultName = null)
    {
        string path = Prompt.Input<string>(inMessage);

        return GetFile(path, inCreateDirectory, inDefaultName);
    }

    private static FileInfo? GetFile(string inPath, bool inCreateDirectory = false, string? inDefaultName = null)
    {
        if (Directory.Exists(inPath))
        {
            if (string.IsNullOrEmpty(inDefaultName))
            {
                Logger.LogErrorInternal("Path can not be a Directory.");
                return null;
            }

            inPath = Path.Combine(inPath, inDefaultName);
        }

        FileInfo retVal = new(inPath);

        if (inCreateDirectory)
        {
            retVal.Directory?.Create();
        }
        else if (retVal.Directory?.Exists == false)
        {
            Logger.LogErrorInternal($"Directory containing file {inPath} does not exist.");
            return null;
        }

        return retVal;
    }

    private static DirectoryInfo RequestDirectory(string inMessage, bool inCreateDirectory = false)
    {
        string path = Prompt.Input<string>(inMessage);

        DirectoryInfo retVal = new(path);

        if (inCreateDirectory)
        {
            retVal.Create();
        }

        return retVal;
    }
}