using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Frosty.Sdk;
using FrostyEditor.Models;

namespace FrostyEditor.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public ObservableCollection<DocumentModel> Documents { get; } = new();

    [ObservableProperty]
    private MenuViewModel m_menu = new();

    [ObservableProperty]
    private DataExplorerViewModel m_dataExplorer = new();

    [ObservableProperty]
    private LoggerViewModel m_logger = new();

    [ObservableProperty]
    private bool m_showStartMenu = true;

    public MainViewModel()
    {
        if (App.MainViewModel is not null)
        {
            throw new Exception();
        }

        App.MainViewModel = this;

        if (FrostyLogger.Logger is LoggerViewModel logger)
        {
            Logger = logger;
        }
    }

    public void AddEditor(AssetEditorViewModel inEditor)
    {
        AddTabItem(inEditor.Header, inEditor);

        // Hide the start menu, if not already hidden
        ShowStartMenu = false;
    }

    private void AddTabItem(string inHeader, object? inContent)
    {
        Documents.Add(new DocumentModel()
        {
            Header = inHeader,
            Content = inContent,
            // Icon = inContent.icon,
        });
    }

    [RelayCommand]
    private void RemoveTabItem(DocumentModel tab)
    {
        Documents.Remove(tab);

        // Make the start page visible again if there are no other tabs open
        if (Documents.Count == 0)
        {
            ShowStartMenu = true;
        }
    }
}