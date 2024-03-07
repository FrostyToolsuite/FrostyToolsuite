using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace FrostyEditor.Utils;

public static class FileService
{
    /// <summary>
    /// Opens file picker dialog.
    /// </summary>
    /// <returns>Array of selected <see cref="IStorageFile"/> or empty collection if user canceled the dialog or null if no MainWindow was found.</returns>
    public static async Task<IReadOnlyList<IStorageFile>?> OpenFilesAsync(FilePickerOpenOptions inOptions)
    {
        TopLevel? topLevel = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;

        if (topLevel is null)
        {
            return null;
        }

        return await topLevel.StorageProvider.OpenFilePickerAsync(inOptions);
    }

    /// <summary>
    /// Opens save file picker dialog.
    /// </summary>
    /// <returns>Saved <see cref="IStorageFile"/> or null if user canceled the dialog.</returns>
    public static async Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions inOptions)
    {
        TopLevel? topLevel = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;

        if (topLevel is null)
        {
            return null;
        }

        return await topLevel.StorageProvider.SaveFilePickerAsync(inOptions);
    }
}