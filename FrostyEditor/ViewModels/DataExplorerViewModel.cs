using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Selection;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Utils;
using FrostyEditor.Managers;
using FrostyEditor.Models;
using FrostyEditor.Utils;

namespace FrostyEditor.ViewModels;

public partial class DataExplorerViewModel : ViewModelBase
{
    public static IMultiValueConverter FolderIconConverter
    {
        get
        {
            if (s_folderIconConverter is null)
            {
                using (Stream folderCollapsedStream = AssetLoader.Open(new Uri("avares://FrostyEditor/Assets/FolderCollapsed.png")))
                using (Stream folderExpandStream = AssetLoader.Open(new Uri("avares://FrostyEditor/Assets/FolderExpanded.png")))
                {
                    Bitmap folderCollapsedIcon = new(folderCollapsedStream);
                    Bitmap folderExpandIcon = new(folderExpandStream);

                    s_folderIconConverter = new FolderIconConvert(folderExpandIcon, folderCollapsedIcon);
                }
            }

            return s_folderIconConverter;
        }
    }

    public HierarchicalTreeDataGridSource<FolderTreeNodeModel> FolderSource { get; }

    private static FolderIconConvert? s_folderIconConverter;

    [ObservableProperty]
    private string m_test = "Explorer";

    [ObservableProperty]
    private FlatTreeDataGridSource<AssetModel> m_assetsSource;

    [ObservableProperty]
    private MenuFlyout m_contextMenu;

    public DataExplorerViewModel()
    {
        FolderSource = new HierarchicalTreeDataGridSource<FolderTreeNodeModel>(FolderTreeNodeModel.Create())
        {
            Columns =
            {
                new HierarchicalExpanderColumn<FolderTreeNodeModel>(
                    new TemplateColumn<FolderTreeNodeModel>(
                        "Name",
                        "FolderNameCell",
                        null,
                        new GridLength(1, GridUnitType.Star),
                        options: new()
                        {
                            CanUserResizeColumn = false,
                            CanUserSortColumn = false,
                            CompareAscending = FolderTreeNodeModel.SortAscending(x => x.Name),
                            CompareDescending = FolderTreeNodeModel.SortDescending(x => x.Name)
                        }),
                    x => x.Children,
                    x => x.HasChildren,
                    x => x.IsExpanded),
            }
        };

        FolderSource.RowSelection!.SelectionChanged += OnSelectionChanged;
        FolderSource.Sort(FolderTreeNodeModel.SortAscending(x => x.Name));

        AssetsSource = new FlatTreeDataGridSource<AssetModel>(Array.Empty<AssetModel>())
        {
            Columns =
            {
                new TextColumn<AssetModel, string>(
                    "Name",
                    x => x.Name,
                    new GridLength(2, GridUnitType.Star),
                    new TextColumnOptions<AssetModel>()
                    {
                        CompareAscending = AssetModel.SortAscending(x => x.Name),
                        CompareDescending = AssetModel.SortDescending(x => x.Name),
                    }),
                new TextColumn<AssetModel, string>(
                    "Type",
                    x => x.Type,
                    new GridLength(1, GridUnitType.Star),
                    new TextColumnOptions<AssetModel>()
                    {
                        CompareAscending = AssetModel.SortAscending(x => x.Type),
                        CompareDescending = AssetModel.SortDescending(x => x.Type),
                    })
            }
        };

        ContextMenu = new MenuFlyout()
        {
            Items =
            {
                new MenuItem { Header = "Open", Command = OpenAssetCommand },
                new MenuItem { Header = "Export", Command = ExportAssetCommand }
            }
        };
    }

    [RelayCommand]
    private async Task ExportAsset()
    {
        if (AssetsSource.Selection is not TreeDataGridRowSelectionModel<AssetModel> selection)
        {
            return;
        }

        if (selection.SelectedItem?.Entry is not EbxAssetEntry entry)
        {
            return;
        }

        IStorageFile? file = await FileService.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save ebx as",
            SuggestedFileName = entry.Filename,
            DefaultExtension = "dbx",
            FileTypeChoices = new FilePickerFileType[]
            {
                new("DBX files (*.dbx)") { Patterns = new[] { "dbx" } },
                new("EBX files (*.ebx)") { Patterns = new[] { "ebx" } },
            }
        });

        if (file is not null)
        {
            await using Stream stream = await file.OpenWriteAsync();

            string extension = Path.GetExtension(file.Name);

            switch (extension)
            {
                case ".dbx":
                {
                    EbxAsset asset = AssetManager.GetEbxAsset(entry);
                    using DbxWriter writer = new(stream);
                    writer.Write(asset);
                    break;
                }
                case ".ebx":
                {
                    using Block<byte> data = AssetManager.GetAsset(entry);
                    stream.Write(data);
                    break;
                }
            }
        }
    }

    [RelayCommand]
    private void OpenAsset()
    {
        if (AssetsSource.Selection is not TreeDataGridRowSelectionModel<AssetModel> selection)
        {
            return;
        }

        if (selection.SelectedItem?.Entry is not EbxAssetEntry entry)
        {
            return;
        }

        App.MainViewModel?.AddEditor(PluginManager.GetEbxAssetEditor(entry));
    }

    private void OnSelectionChanged(object? sender, TreeSelectionModelSelectionChangedEventArgs<FolderTreeNodeModel> e)
    {
        FolderTreeNodeModel? b = e.SelectedItems[0];
        if (b is null)
        {
            return;
        }

        AssetsSource.Items = b.Assets;
    }

    private class FolderIconConvert : IMultiValueConverter
    {
        private readonly Bitmap m_folderExpanded;
        private readonly Bitmap m_folderCollapsed;
        public FolderIconConvert(Bitmap folderExpanded, Bitmap folderCollapsed)
        {
            m_folderExpanded = folderExpanded;
            m_folderCollapsed = folderCollapsed;
        }

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count == 1 &&
                values[0] is bool isExpanded)
            {
                return isExpanded ? m_folderExpanded : m_folderCollapsed;
            }

            return null;
        }
    }
}