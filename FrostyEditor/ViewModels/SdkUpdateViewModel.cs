using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Frosty.Sdk;
using Frosty.Sdk.Sdk;

namespace FrostyEditor.ViewModels;

public partial class SdkUpdateViewModel : WindowViewModel
{
    [ObservableProperty]
    private ObservableCollection<Process> m_runningProcesses = new();

    [ObservableProperty]
    private Process? m_selectedProcess;

    public bool GeneratedSdk;

    public SdkUpdateViewModel()
    {
        Title = "SdkUpdateWindow";
        Height = Width = 500;

        RefreshProcesses();
    }

    [RelayCommand]
    private void RefreshProcesses()
    {
        RunningProcesses.Clear();
        foreach (Process process in Process.GetProcesses())
        {
            RunningProcesses.Add(process);
        }
    }

    [RelayCommand]
    private async Task CreateSdk()
    {
        if (SelectedProcess is null)
        {
            return;
        }

        await Task.Run(() =>
        {
            TypeSdkGenerator typeSdkGenerator = new();

            if (!typeSdkGenerator.DumpTypes(SelectedProcess))
            {
                return;
            }

            if (!typeSdkGenerator.CreateSdk(ProfilesLibrary.SdkPath))
            {
                return;
            }

            GeneratedSdk = true;
        });

        CloseWindow?.Invoke();
    }
}