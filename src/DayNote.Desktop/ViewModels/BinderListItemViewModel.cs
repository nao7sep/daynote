using CommunityToolkit.Mvvm.ComponentModel;

namespace DayNote.Desktop.ViewModels;

/// <summary>A row in the binders pane: one known binder (its file path and display title).</summary>
public sealed partial class BinderListItemViewModel : ObservableObject
{
    public BinderListItemViewModel(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        _title = Name;
    }

    public string Path { get; }

    /// <summary>The binder's file name (without extension); lowercase by the user's convention.</summary>
    public string Name { get; }

    /// <summary>
    /// The binder's human-friendly title — the row's primary label, capitalized and possibly spaced,
    /// distinct from the lowercase file name. Editable inline (double-click the row); defaults to the
    /// file name until the view model fills in the real title.
    /// </summary>
    [ObservableProperty]
    private string _title;

    /// <summary>Working buffer while editing the title, so Escape can discard without touching <see cref="Title"/>.</summary>
    [ObservableProperty]
    private string _editText = string.Empty;

    [ObservableProperty]
    private bool _isMissing;

    /// <summary>True for the binder that is currently open (drives the row highlight).</summary>
    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>True while the title is being edited inline (double-click to enter; blur/Enter applies).</summary>
    [ObservableProperty]
    private bool _isEditing;
}
