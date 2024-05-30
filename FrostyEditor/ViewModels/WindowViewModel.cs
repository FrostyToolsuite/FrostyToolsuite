using CommunityToolkit.Mvvm.ComponentModel;

namespace FrostyEditor.ViewModels;

public abstract partial class WindowViewModel : ViewModelBase
{
    public delegate void CloseWindowFunc();

    public CloseWindowFunc? CloseWindow { get; internal set; }

    [ObservableProperty]
    private double m_width;

    [ObservableProperty]
    private double m_height;

    [ObservableProperty]
    private string? m_title;
}