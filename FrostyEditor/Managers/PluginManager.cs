using System;
using System.Collections.Generic;
using Frosty.Sdk.Managers.Entries;
using FrostyEditor.ViewModels;

namespace FrostyEditor.Managers;

public static class PluginManager
{
    private static Dictionary<string, Type> s_ebxAssetEditors = new();

    public static AssetEditorViewModel GetEbxAssetEditor(EbxAssetEntry entry)
    {
        if (s_ebxAssetEditors.TryGetValue(entry.Type.ToLower(), out Type? type) &&
            Activator.CreateInstance(type, entry) is AssetEditorViewModel editor)
        {
            return editor;
        }

        return new AssetEditorViewModel(entry);
    }
}