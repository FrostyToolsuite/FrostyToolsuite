using Avalonia.Controls;
using FrostyEditor.ViewModels;

namespace FrostyEditor.Windows;

public partial class ViewWindow : Window
{
    public ViewWindow()
    {
        InitializeComponent();
    }

    public ViewWindow(WindowViewModel inViewModel)
        : this()
    {
        DataContext = inViewModel;
        Content = inViewModel;
        inViewModel.CloseWindow = Close;
    }

    public static ViewWindow Create<T>()
        where T : WindowViewModel, new()
    {
        return new ViewWindow(new T());
    }

    public static ViewWindow Create<T>(out T outViewModel)
        where T : WindowViewModel, new()
    {
        outViewModel = new T();
        return new ViewWindow(outViewModel);
    }
}