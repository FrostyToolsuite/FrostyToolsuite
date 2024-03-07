using Frosty.Sdk.Managers.Entries;

namespace FrostyEditor.ViewModels;

public class AssetEditorViewModel : ViewModelBase
{
    public string Header => m_entry.Filename;

    protected readonly AssetEntry m_entry;

    public AssetEditorViewModel(AssetEntry inEntry)
    {
        m_entry = inEntry;
    }
}