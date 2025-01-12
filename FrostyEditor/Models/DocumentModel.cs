using CommunityToolkit.Mvvm.ComponentModel;

namespace FrostyEditor.Models;

public partial class DocumentModel : ObservableObject
{
    public string? Header { get; set; }
    public object? Content { get; set; }
}