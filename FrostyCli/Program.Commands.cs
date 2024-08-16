using System.CommandLine;
using System.IO;

namespace FrostyCli;

internal static partial class Program
{
    private static void AddLoadCommand(RootCommand rootCommand)
    {
        Argument<FileInfo> gameOption = new(
            name: "game-path",
            description: "The path to the game.");

        Option<FileInfo?> keyOption = new(
            name: "--initfs-key",
            description: "The path to a file containing a key for the initfs if needed.");

        Option<int?> pidOption = new(
            name: "--pid",
            description: "The pid of the game if a sdk should get generated for the game.");

        Command loadCommand = new("load", "Load a games data from the cache or create it.")
        {
            gameOption,
            keyOption,
            pidOption
        };
        rootCommand.AddCommand(loadCommand);

        loadCommand.SetHandler(LoadGameCommand, gameOption, keyOption, pidOption);
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

        Option<int?> pidOption = new(
            name: "--pid",
            description: "The pid of the game if a sdk should get generated for the game.");

        Command modCommand = new("mod", "Generates a ModData folder, which can be used to mod the game.")
        {
            gameArg,
            modsArg,
            modDataOption,
            keyOption,
            pidOption
        };
        rootCommand.AddCommand(modCommand);

        modCommand.SetHandler(ModGameCommand, gameArg, modsArg, modDataOption, keyOption, pidOption);
    }

    private static void AddUpdateModCommand(RootCommand rootCommand)
    {
        Argument<FileInfo> gameArg = new(
            name: "game-path",
            description: "The path to the game.");

        Argument<FileInfo> modArg = new(
            name: "mod-path",
            description: "The path to the mod that should get updated.");

        Option<string?> outputOption = new(
            name: "--output",
            description: "The path where the updated mod should be saved to, defaults to the input path.");

        Option<FileInfo?> keyOption = new(
            name: "--initfs-key",
            description: "The path to a file containing a key for the initfs if needed.");

        Option<int?> pidOption = new(
            name: "--pid",
            description: "The pid of the game if a sdk should get generated for the game.");

        Command updateModCommand = new("update-mod", "Updates a fbmod to the newest version.")
        {
            gameArg,
            modArg,
            outputOption,
            keyOption,
            pidOption
        };
        rootCommand.AddCommand(updateModCommand);

        updateModCommand.SetHandler(UpdateModCommand, gameArg, modArg, outputOption, keyOption, pidOption);
    }

    private static void AddCreateModCommand(RootCommand rootCommand)
    {
        Argument<FileInfo> gameArg = new(
            name: "game-path",
            description: "The path to the game.");

        Argument<DirectoryInfo> projectArg = new(
            name: "project-path",
            description: "The path to the project directory that should get updated.");

        Argument<string?> outputOption = new(
            name: "--output",
            description: "The path where the created mod should be saved to, defaults to the project path with the fbmod extension.");

        Option<FileInfo?> keyOption = new(
            name: "--initfs-key",
            description: "The path to a file containing a key for the initfs if needed.");

        Option<int?> pidOption = new(
            name: "--pid",
            description: "The pid of the game if a sdk should get generated for the game.");

        Command updateModCommand = new("create-mod", "Creates a mod from a project.")
        {
            gameArg,
            projectArg,
            outputOption,
            keyOption,
            pidOption
        };
        rootCommand.AddCommand(updateModCommand);

        updateModCommand.SetHandler(CreateModCommand, gameArg, projectArg, outputOption, keyOption, pidOption);
    }
}