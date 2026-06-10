using Avalonia.Controls;
using DayNote.Core.Storage;
using DayNote.Core.Time;

namespace DayNote.Desktop.Views;

/// <summary>Lists a notebook's backup versions (newest first) and returns the chosen one.</summary>
public sealed class BackupPickerDialog : DialogBase
{
    private readonly ListBox _list;

    public BackupPickerDialog(IReadOnlyList<BackupVersion> versions, string displayTimeZone)
    {
        Title = "Restore from backup";
        Width = 460;

        _list = new ListBox
        {
            Height = 320,
            ItemsSource = versions
                .Select(v => new Row(v, DayNoteTime.ToDisplay(v.CreatedUtc, displayTimeZone)))
                .ToList(),
        };

        SetContent(_list);
        var buttons = SetButtons([("Cancel", "cancel", false), ("Restore", "ok", true)]);
        var restore = buttons["ok"];

        // Restore overwrites the live notebook, so it stays disabled until a version is selected.
        restore.IsEnabled = false;
        _list.SelectionChanged += (_, _) => restore.IsEnabled = _list.SelectedItem is not null;
        SetInitialFocus(buttons["cancel"]);
    }

    public BackupVersion? Selected => (_list.SelectedItem as Row)?.Version;

    public bool Confirmed => ResultTag == "ok" && Selected is not null;

    private sealed record Row(BackupVersion Version, string Display)
    {
        public override string ToString() => Display;
    }
}
