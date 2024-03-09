using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Frosty.Sdk.Interfaces;
using Pastel;

namespace FrostyEditor.ViewModels;

public partial class LoggerViewModel : ViewModelBase, ILogger
{
    private static readonly string s_info = "INFO";
    private static readonly string s_warn = "WARN";
    private static readonly string s_error = "ERROR";

    [ObservableProperty]
    private string? m_text;

    public void LogInfo(string message)
    {
        Text += $"{s_info} - {message}\n";
    }

    public void LogWarning(string message)
    {
        Text += $"{s_warn} - {message}\n";
    }

    public void LogError(string message)
    {
        Text += $"{s_error} - {message}\n";
    }

    public void LogProgress(double progress)
    {
    }
}