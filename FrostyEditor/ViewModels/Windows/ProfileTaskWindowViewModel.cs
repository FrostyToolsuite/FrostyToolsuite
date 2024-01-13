using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using Frosty.Sdk;
using Frosty.Sdk.Interfaces;
using FrostyEditor.Views;

namespace FrostyEditor.ViewModels.Windows;

public partial class ProfileTaskWindowViewModel : ObservableObject, ILogger
{
    [ObservableProperty]
    private string? m_info;

    [ObservableProperty]
    private double m_progress = 0.0;

    public async Task Setup(string inKey, string inPath)
    {
        FrostyLogger.Logger = this;
        if (await MainWindowViewModel.SetupFrostySdk(inKey, inPath))
        {
            ShowMainWindow();
        }
        else
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                desktopLifetime.MainWindow?.Close();
            }
        }
    }

    private void ShowMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            MainWindowViewModel mainWindowViewModel;
            Window? window = desktopLifetime.MainWindow;
            MainWindow mainWindow = new()
            {
                DataContext = mainWindowViewModel = new()
            };

            mainWindow.Closing += (_, _) =>
            {
                mainWindowViewModel?.CloseLayout();
            };

            desktopLifetime.MainWindow = mainWindow;

            desktopLifetime.Exit += (_, _) =>
            {
                mainWindowViewModel?.CloseLayout();
            };

            desktopLifetime.MainWindow.Show();
            window?.Close();
        }
    }

    public void LogInfo(string message)
    {
        Info = message;
    }

    public void LogWarning(string message)
    {
    }

    public void LogError(string message)
    {
    }

    public void LogProgress(double progress)
    {
        Progress = progress;
    }
}