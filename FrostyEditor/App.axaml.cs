using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FrostyEditor.Utils;
using FrostyEditor.ViewModels;
using FrostyEditor.Windows;

namespace FrostyEditor;

public partial class App : Application
{
    public static string ConfigPath = Path.Combine(AppContext.BaseDirectory, "editor_config.json");

    public static MainViewModel? MainViewModel = null;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Config.Load(ConfigPath);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = ViewWindow.Create<ProfileSelectViewModel>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}