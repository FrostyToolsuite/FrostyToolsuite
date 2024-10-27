using Frosty.Sdk;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Sdk;
using Frosty.Sdk.Utils;
using Sharprompt;
using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace FrostyCli;

internal static partial class Program
{
    private static bool s_isInteractive;

    private static int Main(string[] args)
    {
        RootCommand rootCommand = new("CLI app to load and mod games made with the Frostbite Engine.")
        {
            key1Option, key2Option, key3Option
        };

        AddLoadCommand(rootCommand);

        AddModCommand(rootCommand);

        AddUpdateModCommand(rootCommand);

        AddCreateModCommand(rootCommand);

        rootCommand.SetHandler(InteractiveMode, key1Option, key2Option, key3Option);

        return rootCommand.InvokeAsync(args).Result;
    }

    private static void InteractiveMode(FileInfo? initFsKey, FileInfo? bundleKey, FileInfo? casKey)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
#if DEBUG || NIGHTLY
        Logger.LogInfoInternal(
            $"FrostyCli v{assembly.GetName().Version?.ToString(2)}-{assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "GitHash")?.Value}");
#else
        Logger.LogInfoInternal($"FrostyCli v{assembly.GetName().Version?.ToString(3)}");

#endif


        if (!LoadGame(inInitFsKeyFileInfo: initFsKey, inBundleKeyFileInfo: bundleKey, inCasKeyFileInfo: casKey))
        {
            return;
        }

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
                    ModGame();
                    break;
                case ActionType.UpdateMod:
                    UpdateMod();
                    break;
                case ActionType.CreateMod:
                    CreateMod();
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

    private static bool LoadGame(FileInfo? inGameFileInfo = null, int? inPid = null,
        FileInfo? inInitFsKeyFileInfo = null, FileInfo? inBundleKeyFileInfo = null, FileInfo? inCasKeyFileInfo = null)
    {
        FileInfo? game = inGameFileInfo ?? RequestFile("Input the path to the games executable");

        if (game?.Exists != true)
        {
            Logger.LogErrorInternal("Game does not exist.");
            return false;
        }

        // set logger
        FrostyLogger.Logger = new Logger();

        // set base directory to the directory containing the executable
        Utils.BaseDirectory = Path.GetDirectoryName(AppContext.BaseDirectory) ?? string.Empty;

        // init profile
        if (!ProfilesLibrary.Initialize(Path.GetFileNameWithoutExtension(game.Name)))
        {
            return false;
        }

        if (ProfilesLibrary.RequiresInitFsKey)
        {
            FileInfo? keyFileInfo = inInitFsKeyFileInfo ?? RequestFile("Pass in the path to an initfs key");

            if (keyFileInfo?.Exists != true)
            {
                Logger.LogErrorInternal("Key does not exist.");
                return false;
            }

            if (keyFileInfo.Length != 0x10)
            {
                Logger.LogErrorInternal("InitFs key needs to be 16 bytes long.");
                return false;
            }

            KeyManager.AddKey("InitFsKey", File.ReadAllBytes(keyFileInfo.FullName));
        }

        if (ProfilesLibrary.RequiresBundleKey)
        {
            FileInfo? keyFileInfo = inBundleKeyFileInfo ?? RequestFile("Pass in the path to an bundle key");

            if (keyFileInfo?.Exists != true)
            {
                Logger.LogErrorInternal("Key does not exist.");
                return false;
            }

            if (keyFileInfo.Length != 0x10)
            {
                Logger.LogErrorInternal("Bundle key needs to be 16 bytes long.");
                return false;
            }

            KeyManager.AddKey("BundleEncryptionKey", File.ReadAllBytes(keyFileInfo.FullName));
        }

        if (ProfilesLibrary.RequiresCasKey)
        {
            FileInfo? keyFileInfo = inCasKeyFileInfo ?? RequestFile("Pass in the path to an cas key");

            if (keyFileInfo?.Exists != true)
            {
                Logger.LogErrorInternal("Key does not exist.");
                return false;
            }

            if (keyFileInfo.Length != 0x4000)
            {
                Logger.LogErrorInternal("Cas key needs to be 16384 bytes long.");
                return false;
            }

            KeyManager.AddKey("CasObfuscationKey", File.ReadAllBytes(keyFileInfo.FullName));
        }

        if (game.DirectoryName is null)
        {
            Logger.LogErrorInternal("The game needs to be in a directory containing the games data.");
            return false;
        }

        // init filesystem manager, this parses the layout.toc file
        if (!FileSystemManager.Initialize(game.DirectoryName))
        {
            return false;
        }

        // generate sdk if needed
        if (!File.Exists(ProfilesLibrary.SdkPath))
        {
            int pid = inPid ?? Prompt.Input<int>("Input pid of the currently running game");

            TypeSdkGenerator typeSdkGenerator = new();

            using Process process = Process.GetProcessById(pid);

            if (!typeSdkGenerator.DumpTypes(process))
            {
                return false;
            }

            Logger.LogInfoInternal("The game is not needed anymore and can be closed.");
            if (!typeSdkGenerator.CreateSdk(ProfilesLibrary.SdkPath))
            {
                return false;
            }
        }

        // init type library, this loads the EbxTypeSdk used to properly parse ebx assets
        if (!TypeLibrary.Initialize())
        {
            return false;
        }

        // init resource manager, this parses the cas.cat files if they exist for easy asset lookup
        if (!ResourceManager.Initialize())
        {
            return false;
        }

        // init asset manager, this parses the SuperBundles and loads all the assets
        if (!AssetManager.Initialize())
        {
            return false;
        }

        s_isInteractive = true;
        return true;
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