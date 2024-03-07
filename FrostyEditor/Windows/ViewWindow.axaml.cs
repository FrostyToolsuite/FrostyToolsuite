using Avalonia.Controls;
using FrostyEditor.ViewModels;

namespace FrostyEditor.Windows;

public partial class ViewWindow : Window
{
    public ViewWindow()
    {
        InitializeComponent();
    }

    public ViewWindow(ViewModelBase inViewModel)
        : this()
    {
        Content = inViewModel;
    }
}