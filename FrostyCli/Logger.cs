using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Frosty.Sdk.Interfaces;
using Pastel;

namespace FrostyCli;

internal class Logger : ILogger
{
    private static readonly string s_info = "INFO".Pastel(ConsoleColor.Green);
    private static readonly string s_warn = "WARN".Pastel(ConsoleColor.DarkYellow);
    private static readonly string s_error = "ERROR".Pastel(ConsoleColor.Red);

    private readonly BlockingCollection<string> m_logQueue = new();
    private readonly CancellationTokenSource m_cancellationTokenSource = new();

    public Logger()
    {
        Task.Run(() => ProcessLogQueue(m_cancellationTokenSource.Token));
    }

    public void LogInfo(string message)
    {
        m_logQueue.Add($"{s_info} - {message}");
    }

    public void LogWarning(string message)
    {
        m_logQueue.Add($"{s_warn} - {message}");
    }

    public void LogError(string message)
    {
        m_logQueue.Add($"{s_error} - {message}");
    }

    internal static void LogErrorInternal(string message)
    {
        Console.WriteLine($"{s_error} - {message}");
    }

    internal static void LogInfoInternal(string message)
    {
        Console.WriteLine($"{s_info} - {message}");
    }

    public void LogProgress(double progress)
    {
        // Implement progress logging if needed
    }

    private void ProcessLogQueue(CancellationToken cancellationToken)
    {
        foreach (string logMessage in m_logQueue.GetConsumingEnumerable(cancellationToken))
        {
            Console.WriteLine(logMessage);
        }
    }

    public void StopLogging()
    {
        m_cancellationTokenSource.Cancel();
        m_logQueue.CompleteAdding();
    }
}
