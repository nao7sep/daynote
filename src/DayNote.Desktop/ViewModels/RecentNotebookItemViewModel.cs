using CommunityToolkit.Mvvm.ComponentModel;

namespace DayNote.Desktop.ViewModels;

/// <summary>A row in the recent-notebooks pane.</summary>
public sealed partial class RecentNotebookItemViewModel : ObservableObject
{
    public RecentNotebookItemViewModel(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
    }

    public string Path { get; }

    public string Name { get; }

    public string Directory { get; }

    [ObservableProperty]
    private bool _isMissing;
}
