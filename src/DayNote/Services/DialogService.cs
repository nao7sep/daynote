using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DayNote.Core.Configuration;
using DayNote.I18n;
using DayNote.Logging;
using DayNote.Views;

namespace DayNote.Services;

/// <summary>
/// View-layer implementation of <see cref="IDialogService"/>: the application's own modal windows
/// plus the native file picker. The owner window is set once after it is constructed.
/// </summary>
public sealed class DialogService : IDialogService
{
    // Built per picker, so its name is in the language showing when the picker opens.
    private static FilePickerFileType BinderType() => new(Localizer.T("picker.binderType"))
    {
        Patterns = new[] { "*.daynote" },
    };

    private readonly IAppLogger _log;

    public DialogService(IAppLogger log) => _log = log;

    public Window? Owner { get; set; }

    public async Task<string?> PickBinderToOpenAsync()
    {
        var owner = RequireOwner();
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.T("picker.openBinder"),
            AllowMultiple = false,
            FileTypeFilter = new[] { BinderType() },
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickBinderToCreateAsync()
    {
        var owner = RequireOwner();
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.T("picker.newBinder"),
            DefaultExtension = "daynote",
            // A lowercase default filename, the name the app gives a new binder, so it is in the
            // reader's language (the human-friendly capitalized name lives in the binder title).
            SuggestedFileName = Localizer.T("picker.binderFileName"),
            FileTypeChoices = new[] { BinderType() },
        });

        return file?.TryGetLocalPath();
    }

    public async Task<IReadOnlyList<string>> PickAttachmentsAsync()
    {
        var owner = RequireOwner();
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.T("picker.addAttachments"),
            AllowMultiple = true,
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
    }

    public async Task<bool> ConfirmAsync(Message title, Message message, string confirmLabelKey, bool destructive = false)
    {
        var dialog = new MessageDialog(title, message, new[]
        {
            new DialogButton("common.cancel", "cancel"),
            new DialogButton(confirmLabelKey, "confirm", destructive ? DialogButtonKind.Destructive : DialogButtonKind.Primary),
        });
        await dialog.ShowBoundedAsync(RequireOwner());
        return dialog.ResultTag == "confirm";
    }

    public async Task ShowErrorAsync(Message title, Message message)
    {
        var dialog = new MessageDialog(title, message, new[] { new DialogButton("common.ok", "ok", DialogButtonKind.Primary) });
        await dialog.ShowBoundedAsync(RequireOwner());
    }

    public async Task ShowAboutAsync()
    {
        var dialog = new AboutDialog(_log);
        await dialog.ShowBoundedAsync(RequireOwner());
    }

    public async Task ShowShortcutsAsync()
    {
        var owner = RequireOwner();
        var dialog = new ShortcutsDialog(ShortcutCatalog.Build(owner));
        await dialog.ShowBoundedAsync(owner);
    }

    public async Task<bool> ShowSettingsAsync(AppConfig config, Func<AppConfig, bool> trySave)
    {
        var dialog = new SettingsDialog(config, trySave);
        await dialog.ShowBoundedAsync(RequireOwner());
        return dialog.Applied;
    }

    public async Task<ExternalChangeChoice> AskExternalChangeAsync(string binderName)
    {
        var dialog = new MessageDialog(
            Message.Of("binder.changedTitle"),
            Message.Of("binder.changedMessage", ("name", binderName)),
            new[]
            {
                new DialogButton("binder.keepMine", "keep"),
                // Reloading discards the user's unsaved local edits, so it is the destructive choice:
                // mark it Destructive both for its styling and so initial focus lands on the safe
                // "Keep my version" instead of the edit-losing button.
                new DialogButton("binder.reloadFromDisk", "reload", DialogButtonKind.Destructive),
            });
        await dialog.ShowBoundedAsync(RequireOwner());
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
