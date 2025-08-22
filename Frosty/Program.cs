using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Frosty.Sdk;
using Frosty.Sdk.Managers;
using Microsoft.Extensions.Logging;
using Pastel;

namespace Frosty;

internal static partial class Program
{
	private static bool s_gameIsLoaded;

    private static readonly Option<byte[]?> s_initFsKey = CreateKeyOption("--initfs-key",
        "The path to a file containing a 16 byte key for the initfs or the key itself in hexadecimal form", 16,
        "InitFsKey");
    private static readonly Option<byte[]?> s_bundleKey = CreateKeyOption("--bundle-key",
        "The path to a file containing a 16 byte key for the bundles or the key itself in hexadecimal form", 16,
        "BundleEncryptionKey");
    private static readonly Option<byte[]?> s_casKey = CreateKeyOption("--cas-key",
        "The path to a file containing a 16384 byte key for the cas archives or the key itself in hexadecimal form",
        16384, "CasObfuscationKey");

    private static LogLevel s_logLevel = LogLevel.Information;

	public static async Task<int> Main(string[] args)
	{
        s_logLevel = Enum.Parse<LogLevel>(Environment.GetEnvironmentVariable("FROSTY_LOG_LEVEL") ?? "Information");

		RootCommand root = new();
        root.AddGlobalOption(s_initFsKey);
        root.AddGlobalOption(s_bundleKey);
        root.AddGlobalOption(s_casKey);
        root.SetHandler(Handle);

		CreateBaseCommands(root);

		return await root.InvokeAsync(args);
	}

	private static void Handle(InvocationContext inContext)
	{
		RootCommand rootCommand = new();
		AddCommands(rootCommand);
		int currentCommand = 0;
		List<string> commands = new();
		while (true)
		{
			Console.Write(("[" + (string.IsNullOrEmpty(ProfilesLibrary.InternalName) ? "none" : ProfilesLibrary.InternalName) + "]> ").Pastel(System.Drawing.Color.DarkOrange));
			StringBuilder sb = new();
			(int Left, int Top) startPosition = Console.GetCursorPosition();
			commands.Add(string.Empty);
			while (true)
			{
				ConsoleKeyInfo consoleKeyInfo = Console.ReadKey(intercept: true);
                (int Left, int Top) currentPosition = Console.GetCursorPosition();
				switch (consoleKeyInfo.Key)
				{
				    case ConsoleKey.Backspace:
					    if (currentPosition.Left != startPosition.Left)
					    {
						    sb.Remove(currentPosition.Left - startPosition.Left - 1, 1);
						    Console.Write("\b" + sb.ToString(currentPosition.Left - startPosition.Left - 1, sb.Length - currentPosition.Left + startPosition.Item1 + 1) + " ");
						    Console.SetCursorPosition(currentPosition.Left - 1, currentPosition.Top);
					    }
					    continue;
				    case ConsoleKey.Enter:
				    {
					    Console.WriteLine();
                        break;
                    }
				    case ConsoleKey.LeftArrow:
					    Console.SetCursorPosition(Math.Max(currentPosition.Left - 1, startPosition.Left), currentPosition.Top);
                        continue;
				    case ConsoleKey.RightArrow:
					    Console.SetCursorPosition(Math.Min(currentPosition.Left + 1, startPosition.Left + sb.Length), currentPosition.Top);
                        continue;
				    case ConsoleKey.UpArrow:
					    if (currentCommand > 0)
					    {
						    string prev = commands[--currentCommand];
						    Console.SetCursorPosition(startPosition.Left, startPosition.Top);
						    Console.Write(prev);
						    if (sb.Length > prev.Length)
						    {
							    Console.Write(new string(' ', sb.Length - prev.Length));
						    }
						    Console.SetCursorPosition(startPosition.Left + prev.Length, startPosition.Top);
						    sb.Clear();
						    sb.Append(prev);
					    }
                        continue;
				    case ConsoleKey.DownArrow:
					    if (currentCommand < commands.Count - 1)
					    {
						    string next = commands[++currentCommand];
						    Console.SetCursorPosition(startPosition.Left, startPosition.Top);
						    Console.Write(next);
						    if (sb.Length > next.Length)
						    {
							    Console.Write(new string(' ', sb.Length - next.Length));
						    }
						    Console.SetCursorPosition(startPosition.Left + next.Length, startPosition.Top);
						    sb.Clear();
						    sb.Append(next);
					    }
                        continue;
				    case ConsoleKey.Home:
					    Console.SetCursorPosition(startPosition.Left, currentPosition.Top);
                        continue;
				    case ConsoleKey.End:
					    Console.SetCursorPosition(startPosition.Left + sb.Length, currentPosition.Top);
                        continue;
				    default:
					    sb.Insert(currentPosition.Left - startPosition.Left, consoleKeyInfo.KeyChar);
					    Console.Write(sb.ToString(currentPosition.Left - startPosition.Left, sb.Length - currentPosition.Left + startPosition.Left));
					    Console.SetCursorPosition(currentPosition.Item1 + 1, currentPosition.Top);
                        continue;
				    case ConsoleKey.Delete:
                        continue;
				}
				break;
			}

            string command = sb.ToString();
            commands[^1] = command;
            currentCommand = commands.Count;
            try
            {
                rootCommand.Invoke(command);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
		}
	}

	private static Option<byte[]?> CreateKeyOption(string inName, string? inDescription, int inSize, string inId)
	{
		return new Option<byte[]?>(name: inName, description: inDescription, parseArgument: result =>
		{
			if (result.Tokens.Count != 1)
			{
				result.ErrorMessage = inName + " requires one argument";
				return null;
			}
			string value = result.Tokens[0].Value;
			byte[] array;
			if (value.Length == inSize * 2)
			{
				Span<byte> span = stackalloc byte[inSize];
                for (int i = 0; i < inSize; i++)
                {
                    if (!byte.TryParse(value.AsSpan(i * 2, 2), NumberStyles.HexNumber, null, out byte b))
                    {
                        break;
                    }
                    span[i] = b;
                }
                array = span.ToArray();
                KeyManager.AddKey(inId, array);
                return array;
			}
			FileInfo fileInfo = new(value);
			if (!fileInfo.Exists || fileInfo.Length != inSize)
            {
                result.ErrorMessage =
                    $"{inName} requires either an existing file containing the {inSize} byte key or the key in hexadecimal form";
				return null;
			}
			array = File.ReadAllBytes(fileInfo.FullName);
			KeyManager.AddKey(inId, array);
			return array;
		});
	}
}