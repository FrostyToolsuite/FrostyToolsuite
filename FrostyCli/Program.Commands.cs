using System.CommandLine;
using System.IO;

namespace FrostyCli;

internal static partial class Program
{
    private static Argument<FileInfo?> gameArg = new(
        name: "game-path",
        description: "The path to the game.");

    private static Option<int?> pidOption = new(
            name: "--pid",
            description: "The pid of the game if a sdk should get generated for the game.");

    private static Option<FileInfo?> key1Option = new(
        name: "--initfs-key",
        description: "The path to a file containing a key for the initfs if needed.");

    private static Option<FileInfo?> key2Option = new(
        name: "--bundle-key",
        description: "The path to a file containing a key for bundles if needed.");

    private static Option<FileInfo?> key3Option = new(
        name: "--cas-key",
        description: "The path to a file containing a key for cas files if needed.");

    private static void AddLoadCommand(RootCommand rootCommand)
    {
        Command loadCommand = new("load", "Load a games data from the cache or create it.")
        {
            gameArg,
            pidOption,
            key1Option,
            key2Option,
            key3Option
        };
        rootCommand.AddCommand(loadCommand);

        loadCommand.SetHandler((game, pid, key1, key2, key3) => LoadGame(game, pid, key1, key2, key3), gameArg, pidOption, key1Option, key2Option, key3Option);
    }

    private static void AddModCommand(RootCommand rootCommand)
    {
        Argument<DirectoryInfo?> modsArg = new(
            name: "mods-dir",
            description: "The directory containing the mods to generate the data with.");

        Argument<DirectoryInfo?> modDataOption = new(
            name: "mod-data-dir",
            description: "The directory to which the modded data should get generated.");

        Command modCommand = new("mod", "Generates a ModData folder, which can be used to mod the game.")
        {
            gameArg,
            modsArg,
            modDataOption,
            pidOption,
            key1Option,
            key2Option,
            key3Option
        };
        rootCommand.AddCommand(modCommand);

        modCommand.SetHandler(
            (game, mods, modData, pid, key1, key2, key3) => ModGame(game, pid, key1, key2, key3, mods, modData),
            gameArg, modsArg, modDataOption, pidOption, key1Option, key2Option, key3Option);
    }

    private static void AddUpdateModCommand(RootCommand rootCommand)
    {
        Argument<FileInfo> modArg = new(
            name: "mod-path",
            description: "The path to the mod that should get updated.");

        Option<string?> outputOption = new(
            name: "--output",
            description: "The path where the updated mod should be saved to, defaults to the input path.");

        Command updateModCommand = new("update-mod", "Updates a fbmod to the newest version.")
        {
            gameArg,
            modArg,
            outputOption,
            pidOption,
            key1Option,
            key2Option,
            key3Option
        };
        rootCommand.AddCommand(updateModCommand);

        updateModCommand.SetHandler(
            (game, mod, output, pid, key1, key2, key3) => UpdateMod(game, pid, key1, key2, key3, mod, output), gameArg,
            modArg, outputOption, pidOption, key1Option, key2Option, key3Option);
    }

    private static void AddCreateModCommand(RootCommand rootCommand)
    {
        Argument<DirectoryInfo> projectArg = new(
            name: "project-path",
            description: "The path to the project directory that should get updated.");

        Option<string?> outputOption = new(
            name: "--output",
            description: "The path where the created mod should be saved to, defaults to the project path with the fbmod extension.");

        Command updateModCommand = new("create-mod", "Creates a mod from a project.")
        {
            gameArg,
            projectArg,
            outputOption,
            pidOption,
            key1Option,
            key2Option,
            key3Option
        };
        rootCommand.AddCommand(updateModCommand);

        updateModCommand.SetHandler(
            ((game, project, output, pid, key1, key2, key3) => CreateMod(game, pid, key1, key2, key3, project, output)),
            gameArg, projectArg, outputOption, pidOption, key1Option, key2Option, key3Option);
    }
}