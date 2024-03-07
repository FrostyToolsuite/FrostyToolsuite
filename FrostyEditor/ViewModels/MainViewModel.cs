using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using FrostyEditor.Models;
using FrostyEditor.Views;

namespace FrostyEditor.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private MenuViewModel m_menu = new();

    [ObservableProperty]
    private DataExplorerViewModel m_dataExplorer = new();

    public ObservableCollection<DocumentModel> Documents { get; } = new();

    public MainViewModel()
    {
        if (App.MainViewModel is not null)
        {
            throw new Exception();
        }

        App.MainViewModel = this;
        AddTabItem("Start Page", "Nothing here yet, please someone implement a PropertyGrid");
    }

    public void AddEditor(AssetEditorViewModel inEditor)
    {
        AddTabItem(inEditor.Header, inEditor);
    }

    private void AddTabItem(string inHeader, object? inContent)
    {
        Documents.Add(new DocumentModel()
        {
            Header = inHeader,
            Content = inContent,
        });
    }
}