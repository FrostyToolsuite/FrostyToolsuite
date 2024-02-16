using System;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Frosty.ModSupport.Mod;
using Frosty.Sdk;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Sdk;
using Frosty.Sdk.Utils;
using Sharprompt;

namespace FrostyCli;

internal static class Program
{
    private static int Main(string[] args)
    {
        RootCommand rootCommand = new("CLI app to load and mod games made with the Frostbite Engine.");

        AddLoadCommand(rootCommand);

        AddModCommand(rootCommand);

        AddUpdateModCommand(rootCommand);

        rootCommand.SetHandler(InteractiveMode);

        return rootCommand.InvokeAsync(args).Result;
    }

    private static void InteractiveMode()
    {
        string gamePath = Prompt.Input<string>("Input the path to the games executable");

        FileInfo game = new(gamePath);

        if (!game.Exists)
        {
            Logger.LogErrorInternal("Game does not exist");
            return;
        }

        LoadGame(game);

        ActionType actionType;
        do
        {
             switch (actionType = Prompt.Select<ActionType>("Select what you want to do"))
             {
                 case ActionType.Quit:
                     break;
                 case ActionType.Mod:
                     Logger.LogErrorInternal("Not implemented yet.");
                     break;
                 case ActionType.UpdateMod:
                     UpdateMod();
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
                 case ActionType.GetEbx:
                     GetEbx();
                     break;
                 case ActionType.GetDbx:
                     GetDbx();
                     break;
                 case ActionType.GetRes:
                     GetRes();
                     break;
                 case ActionType.GetChunk:
                     GetChunk();
                     break;
             }
        } while (actionType != ActionType.Quit);

    }

    private enum ActionType
    {
        Quit,
        Mod,
        UpdateMod,
        ListEbx,
        ListRes,
        ListChunks,
        GetEbx,
        GetDbx,
        GetRes,
        GetChunk,
    }

    private static void LoadGame(FileInfo inGameFileInfo)
    {
        if (!inGameFileInfo.Exists)
        {
            Logger.LogErrorInternal("No game exists at that path");
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
                Logger.LogErrorInternal("Key does not exist");
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
                Logger.LogErrorInternal("Key does not exist");
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
                Logger.LogErrorInternal("Key does not exist");
                return;
            }

            KeyManager.AddKey("CasObfuscationKey", File.ReadAllBytes(keyFileInfo.FullName));
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
            int pid = Prompt.Input<int>("Input pid of the currently running game");

            TypeSdkGenerator typeSdkGenerator = new();

            Process game = Process.GetProcessById(pid);

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

    private static void UpdateMod()
    {
        FileInfo modFileInfo = new(Prompt.Input<string>("Pass in the path to the mod that should get updated"));
        if (!modFileInfo.Exists)
        {
            Logger.LogErrorInternal("Mod file does not exist");
            return;
        }

        FileInfo outputFileInfo = new(Prompt.Input<string>("Pass in the path where the updated mod should get saved to"));

        ModUpdater.UpdateMod(modFileInfo.FullName, outputFileInfo.FullName);
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

    private static void GetEbx()
    {
        string name = Prompt.Input<string>("Input ebx name");

        AssetEntry? entry = AssetManager.GetEbxAssetEntry(name);

        if (entry is null)
        {
            Logger.LogErrorInternal("Asset does not exist");
            return;
        }

        string file = Prompt.Input<string>("Input file path to save ebx to");

        using (Block<byte> data = AssetManager.GetAsset(entry))
        {
            File.WriteAllBytes(file, data.ToArray());
        }
    }

    private static void GetDbx()
    {
        string name = Prompt.Input<string>("Input ebx name");

        EbxAssetEntry? entry = AssetManager.GetEbxAssetEntry(name);

        if (entry is null)
        {
            Logger.LogErrorInternal("Asset does not exist");
            return;
        }

        string file = Prompt.Input<string>("Input file path to save ebx to");

        EbxAsset asset = AssetManager.GetEbxAsset(entry);

        using (DbxWriter writer = new(file))
        {
            writer.Write(asset);
        }
    }

    private static void GetRes()
    {
        string name = Prompt.Input<string>("Input res name or rid");

        AssetEntry? entry;
        if (ulong.TryParse(name, NumberStyles.HexNumber, null, out ulong rid))
        {
            entry = AssetManager.GetResAssetEntry(rid);
        }
        else
        {
            entry = AssetManager.GetResAssetEntry(name);
        }
        if (entry is null)
        {
            Logger.LogErrorInternal("Asset does not exist");
            return;
        }

        string file = Prompt.Input<string>("Input file path to save res to");

        using (Block<byte> data = AssetManager.GetAsset(entry))
        {
            File.WriteAllBytes(file, data.ToArray());
        }
    }

    private static void GetChunk()
    {
        string name = Prompt.Input<string>("Input chunk id");
        Guid id = Guid.Parse(name);

        AssetEntry? entry = AssetManager.GetChunkAssetEntry(id);

        if (entry is null)
        {
            Logger.LogErrorInternal("Asset does not exist");
            return;
        }

        string file = Prompt.Input<string>("Input file path to save chunk to");

        using (Block<byte> data = AssetManager.GetAsset(entry))
        {
            File.WriteAllBytes(file, data.ToArray());
        }
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
            name: "--pid",
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

        Command modCommand = new("mod", "todo")
        {
            gameArg,
            modsArg,
            modDataOption,
            keyOption,
            sdkOption
        };
        rootCommand.AddCommand(modCommand);

        modCommand.SetHandler(ModGame, gameArg, modsArg, modDataOption, keyOption, sdkOption);
    }

    private static void ModGame(FileInfo inGameFileInfo, DirectoryInfo inModsDirInfo, DirectoryInfo? inModDataDirInfo, FileInfo? inKeyFileInfo, int? inPid)
    {
        // load game
        LoadGame(inGameFileInfo, inKeyFileInfo, inPid);

        Logger.LogErrorInternal("Not implemented yet.");
    }

    private static void AddUpdateModCommand(RootCommand rootCommand)
    {
        Argument<FileInfo> gameArg = new(
            name: "game-path",
            description: "The path to the game.");

        Argument<FileInfo> modArg = new(
            name: "mod-path",
            description: "The path to the mod that should get updated.");

        Option<FileInfo?> outputOption = new(
            name: "--output",
            description: "The path where the updated mod should be saved to, defaults to the input path.");

        Option<FileInfo?> keyOption = new(
            name: "--initfs-key",
            description: "The path to a file containing a key for the initfs if needed.");

        Option<int?> sdkOption = new(
            name: "--sdk",
            description: "The pid of the game if a sdk should get generated for the game.");

        Command updateModCommand = new("update-mod", "Updates a fbmod to the newest version.")
        {
            gameArg,
            modArg,
            outputOption,
            keyOption,
            sdkOption
        };
        rootCommand.AddCommand(updateModCommand);

        updateModCommand.SetHandler(UpdateMod, gameArg, modArg, outputOption, keyOption, sdkOption);
    }

    private static void UpdateMod(FileInfo inGameFileInfo, FileInfo inModFileInfo, FileInfo? inOutputFileInfo, FileInfo? inKeyFileInfo, int? inPid)
    {
        if (!inModFileInfo.Exists)
        {
            Logger.LogErrorInternal("Mod file does not exist");
            return;
        }

        // load game
        LoadGame(inGameFileInfo, inKeyFileInfo, inPid);

        ModUpdater.UpdateMod(inModFileInfo.FullName, inOutputFileInfo?.FullName ?? inModFileInfo.FullName);
    }
}