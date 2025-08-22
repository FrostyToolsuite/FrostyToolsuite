using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.IO;
using Frosty.Sdk;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Sdk;
using Frosty.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Frosty;

internal static partial class Program
{
	private static void CreateBaseCommands(RootCommand inRoot)
    {
        Argument<string> gamePath = new(name: "game-path",
            description: "The path to the Frostbite game executable to load", parse: result =>
            {
                if (result.Tokens.Count > 1)
                {
                    result.ErrorMessage = "Only one game path can be specified";
                    return string.Empty;
                }

                string value = result.Tokens[0].Value;
                if (!File.Exists(value))
                {
                    result.ErrorMessage = "The game executable '" + value + "' does not exist";
                    return string.Empty;
                }

                return value;
            });
        Option<int> pid = new("--pid", () => -1,
            "The process id of the running game, needed if a type sdk needs to be generated from the game");

        Command load = new("load", "Loads a Frostbite game") { gamePath, pid };
		load.AddAlias("l");
		load.SetHandler(context =>
		{
            // ReSharper disable once AssignmentInConditionalExpression
            if (s_gameIsLoaded = LoadGame(context.ParseResult.GetValueForArgument(gamePath), context.ParseResult.GetValueForOption(pid)))
			{
				Handle(context);
			}
		});
		inRoot.AddCommand(load);

        Argument<string> modPath = new("mod-path",
            "The path to a .fbmod file or a .json load order file referencing multiple mods");
        Option<string> modDataPath = new("mod-data-path",
            "The directory to which the modded data gets written to, default is ModData/Default in the game's directory");
        Command mod =
            new("mod", "Generates a ModData folder, which can be used to mod the game")
            {
                gamePath, modPath, modDataPath
            };
		mod.AddAlias("m");
		inRoot.AddCommand(mod);
	}

	private static bool LoadGame(string inGamePath, int inPid)
	{
		ILoggerFactory loggerFactory = LoggerFactory.Create(delegate(ILoggingBuilder builder)
		{
			builder.SetMinimumLevel(s_logLevel).AddSimpleConsole(delegate(SimpleConsoleFormatterOptions options)
			{
				options.IncludeScopes = true;
				options.SingleLine = true;
			});
		});
		ILogger logger = FrostyLogger.Logger = loggerFactory.CreateLogger("Frosty");
		Utils.BaseDirectory = Path.GetDirectoryName(AppContext.BaseDirectory) ?? string.Empty;
		if (!ProfilesLibrary.Initialize(Path.GetFileNameWithoutExtension(inGamePath)))
		{
			return false;
		}
		if (ProfilesLibrary.RequiresInitFsKey && !KeyManager.HasKey("InitFsKey"))
		{
			logger.LogCritical("{} requires an encryption key for its initFs", ProfilesLibrary.DisplayName);
			return false;
		}
		if (ProfilesLibrary.RequiresBundleKey && !KeyManager.HasKey("BundleEncryptionKey"))
		{
			logger.LogCritical("{} requires an encryption key for its bundles", ProfilesLibrary.DisplayName);
			return false;
		}
		if (ProfilesLibrary.RequiresCasKey && !KeyManager.HasKey("CasObfuscationKey"))
		{
			logger.LogCritical("{} requires an obfuscation key for its cas archives", ProfilesLibrary.DisplayName);
			return false;
		}
		string? directoryName = Path.GetDirectoryName(inGamePath);
		if (string.IsNullOrEmpty(directoryName))
		{
			logger.LogCritical("The game needs to be in a directory containing the games data");
			return false;
		}
		if (!FileSystemManager.Initialize(directoryName))
		{
			return false;
		}
		if (!File.Exists(ProfilesLibrary.SdkPath))
		{
			if (inPid == -1)
			{
				logger.LogCritical("To generate a type sdk from the games memory, a process id of the running game is required");
				return false;
			}
			TypeSdkGenerator generator = new();
			using Process process = Process.GetProcessById(inPid);
			if (!generator.DumpTypes(process))
			{
				return false;
			}
			logger.LogInformation("The game is not needed anymore and can be closed");
			if (!generator.CreateSdk(ProfilesLibrary.SdkPath))
			{
				return false;
			}
		}
		if (!TypeLibrary.Initialize())
		{
			return false;
		}

        PluginManager.LoadPlugins(Path.Combine(Utils.BaseDirectory, "Plugins"));

		if (!ResourceManager.Initialize())
		{
			return false;
		}
		return AssetManager.Initialize();
	}
}