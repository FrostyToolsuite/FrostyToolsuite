using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Frosty.Sdk;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using FrostyEditor.Utils;
using FrostyEditor.Windows;
using MsBox.Avalonia;

namespace FrostyEditor.ViewModels;

public partial class ProfileSelectViewModel : WindowViewModel
{
    public class ProfileConfig
    {
        public string Name { get; set; }
        public string Key { get; set; }
        public string Path { get; set; }

        public ProfileConfig(string inKey)
        {
            Key = inKey;
            Path = Config.Get("GamePath", string.Empty, ConfigScope.Game, Key);
            Name = ProfilesLibrary.GetDisplayName(Key) ?? Key;
        }
    }

    [ObservableProperty]
    private ProfileConfig? m_selectedProfile;

    public ObservableCollection<ProfileConfig> Profiles { get; set; } = new();

    public ProfileSelectViewModel()
    {
        Title = "FrostyEditor";
        Width = Height = 500;

        // init ProfilesLibrary to load all profile json files
        ProfilesLibrary.Initialize();

        foreach (string profile in Config.GameProfiles)
        {
            ProfileConfig config = new(profile);
            if (File.Exists(config.Path))
            {
                Profiles.Add(config);
            }
            else
            {
                Config.RemoveGame(profile);
            }
        }
        Config.Save(App.ConfigPath);
    }

    public static async Task<bool> SetupFrostySdk()
    {
        return await Task.Run(() =>
        {
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

            return true;
        });
    }

    [RelayCommand]
    private async Task AddProfile()
    {
        IReadOnlyList<IStorageFile>? files = await FileService.OpenFilesAsync(new FilePickerOpenOptions
        {
            Title = "Select Game Executable",
            AllowMultiple = false
        });

        if (files is null)
        {
            return;
        }

        foreach (IStorageFile file in files)
        {
            string key = Path.GetFileNameWithoutExtension(file.Name);
            Config.AddGame(key, file.Path.LocalPath);
            Profiles.Add(new ProfileConfig(key));
        }
        Config.Save(App.ConfigPath);
    }

    [RelayCommand]
    private async Task SelectProfile()
    {
        if (SelectedProfile is not null && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            // TODO: add some kind of task window which shows a loading screen or sth

            FrostyLogger.Logger = new LoggerViewModel();

            // set base directory to the directory containing the executable
            Frosty.Sdk.Utils.Utils.BaseDirectory = Path.GetDirectoryName(AppContext.BaseDirectory) ?? string.Empty;

            if (!ProfilesLibrary.Initialize(SelectedProfile.Key))
            {
                goto failed;
            }

            if (ProfilesLibrary.RequiresInitFsKey)
            {
                string keyPath = "Keys/initFs.key";
                if (File.Exists(keyPath))
                {
                    using (BlockStream stream = BlockStream.FromFile(keyPath, false))
                    {
                        if (stream.Length != 0x10)
                        {
                            goto failed;
                        }

                        byte[] initFsKey = new byte[0x10];
                        stream.ReadExactly(initFsKey);
                        KeyManager.AddKey("InitFsKey", initFsKey);
                    }
                }
                else
                {
                    MessageBoxManager.GetMessageBoxStandard("FrostyEditor",
                        $"Missing initFs key file at {Path.Combine(Frosty.Sdk.Utils.Utils.BaseDirectory, "initFs.key")}");
                    goto failed;
                }
            }

            // init filesystem manager, this parses the layout.toc file
            if (!FileSystemManager.Initialize(Path.GetDirectoryName(SelectedProfile.Path) ?? string.Empty))
            {
                goto failed;
            }

            // generate sdk if needed
            string sdkPath = ProfilesLibrary.SdkPath;
            if (!File.Exists(sdkPath))
            {
                ViewWindow sdkUpdateWindow = ViewWindow.Create(out SdkUpdateViewModel vm);
                await sdkUpdateWindow.ShowDialog(desktopLifetime.MainWindow!);
                if (!vm.GeneratedSdk)
                {
                    CloseWindow?.Invoke();
                }
            }

            if (await SetupFrostySdk())
            {
                desktopLifetime.MainWindow = new MainWindow();

                desktopLifetime.MainWindow.Show();
            }
            else
            {
                MessageBoxManager.GetMessageBoxStandard("FrostyEditor", "Failed to initialize Frosty, for more information check the log.");
            }
            failed:
            MessageBoxManager.GetMessageBoxStandard("FrostyEditor", "Failed to initialize Frosty, for more information check the log.");

            CloseWindow?.Invoke();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseWindow?.Invoke();
    }
}