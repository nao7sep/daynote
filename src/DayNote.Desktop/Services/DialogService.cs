using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DayNote.Core.Configuration;
using DayNote.Core.Storage;
using DayNote.Desktop.Views;

namespace DayNote.Desktop.Services;

/// <summary>
/// View-layer implementation of <see cref="IDialogService"/>: the application's own modal windows
/// plus the native file picker. The owner window is set once after it is constructed.
/// </summary>
public sealed class DialogService : IDialogService
{
    private static readonly FilePickerFileType NotebookType = new("DayNote notebook")
    {
        Patterns = new[] { "*.daynote" },
    };

    public Window? Owner { get; set; }

    public async Task<string?> PickNotebookToOpenAsync()
    {
        var owner = RequireOwner();
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open notebook",
            AllowMultiple = false,
            FileTypeFilter = new[] { NotebookType },
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickNotebookToCreateAsync()
    {
        var owner = RequireOwner();
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "New notebook",
            DefaultExtension = "daynote",
            SuggestedFileName = "Notebook",
            FileTypeChoices = new[] { NotebookType },
        });

        return file?.TryGetLocalPath();
    }

    public async Task<IReadOnlyList<string>> PickAttachmentsAsync()
    {
        var owner = RequireOwner();
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add attachments",
            AllowMultiple = true,
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new MessageDialog(title, message, new[] { ("No", "no", false), ("Yes", "yes", true) });
        await dialog.ShowDialog(RequireOwner());
        return dialog.ResultTag == "yes";
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new MessageDialog(title, message, new[] { ("OK", "ok", true) });
        await dialog.ShowDialog(RequireOwner());
    }

    public async Task ShowAboutAsync()
    {
        var dialog = new AboutDialog();
        await dialog.ShowDialog(RequireOwner());
    }

    public async Task ShowShortcutsAsync()
    {
        var owner = RequireOwner();
        var dialog = new ShortcutsDialog(ShortcutCatalog.Build(owner));
        await dialog.ShowDialog(owner);
    }

    public async Task<bool> ShowSettingsAsync(AppConfig config)
    {
        // The dialog edits the working copy in place, so Save just means "keep it"; Cancel discards it.
        var dialog = new SettingsDialog(config);
        await dialog.ShowDialog(RequireOwner());
        return dialog.Applied;
    }

    public async Task<LockedNotebookChoice> AskLockedNotebookAsync(string notebookName)
    {
        var dialog = new MessageDialog(
            "Notebook in use",
            $"“{notebookName}” is open in another instance of DayNote. You can open it read-only or cancel.",
            new[] { ("Cancel", "cancel", false), ("Open read-only", "readonly", true) });
        await dialog.ShowDialog(RequireOwner());
        return dialog.ResultTag == "readonly" ? LockedNotebookChoice.OpenReadOnly : LockedNotebookChoice.Cancel;
    }

    public async Task<ExternalChangeChoice> AskExternalChangeAsync(string notebookName, ExternalChange change)
    {
        var dialog = new MessageDialog(
            "Notebook changed on disk",
            $"“{notebookName}” was modified outside DayNote while you have unsaved edits. " +
            "Reload from disk and lose your edits, or keep your version (the next save overwrites the file)?",
            new[] { ("Keep my version", "keep", false), ("Reload from disk", "reload", true) });
        await dialog.ShowDialog(RequireOwner());
        return dialog.ResultTag == "reload" ? ExternalChangeChoice.ReloadFromDisk : ExternalChangeChoice.KeepMine;
    }

    public async Task OpenPathExternallyAsync(string path)
    {
        var owner = RequireOwner();
        await owner.Launcher.LaunchFileInfoAsync(new FileInfo(path));
    }

    private Window RequireOwner() =>
        Owner ?? throw new InvalidOperationException("Dialog owner window has not been set.");
}
