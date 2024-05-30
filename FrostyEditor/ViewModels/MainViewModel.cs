using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Frosty.Sdk;
using FrostyEditor.Models;

namespace FrostyEditor.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private MenuViewModel m_menu = new();

    [ObservableProperty]
    private DataExplorerViewModel m_dataExplorer = new();

    [ObservableProperty]
    private LoggerViewModel m_logger = new();

    public ObservableCollection<DocumentModel> Documents { get; } = new();

    public MainViewModel()
    {
        if (App.MainViewModel is not null)
        {
            throw new Exception();
        }

        App.MainViewModel = this;
        AddTabItem("Start Page", "Nothing here yet, please someone implement a PropertyGrid");

        if (FrostyLogger.Logger is LoggerViewModel logger)
        {
            Logger = logger;
        }
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