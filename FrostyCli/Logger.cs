using System;
using Frosty.Sdk.Interfaces;
using Pastel;

namespace FrostyCli;

internal class Logger : ILogger
{
    private static readonly string s_info = "INFO".Pastel(ConsoleColor.Green);
    private static readonly string s_warn = "WARN".Pastel(ConsoleColor.DarkYellow);
    private static readonly string s_error = "ERROR".Pastel(ConsoleColor.Red);

    public void LogInfo(string message)
    {
        Console.WriteLine($"{s_info} - {message}");
    }

    public void LogWarning(string message)
    {
        Console.WriteLine($"{s_warn} - {message}");
    }

    public void LogError(string message)
    {
        Console.WriteLine($"{s_error} - {message}");
    }

    internal static void LogErrorInternal(string message)
    {
        Console.WriteLine($"{s_error} - {message}");
    }

    public void LogProgress(double progress)
    {
    }
}