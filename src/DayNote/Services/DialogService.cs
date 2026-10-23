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

    public Task<string?> PickBinderToOpenAsync(string? directory) =>
        PickBinderToOpenAsync(RequireOwner().StorageProvider, directory);

    internal static async Task<string?> PickBinderToOpenAsync(IStorageProvider storage, string? directory)
    {
        var start = directory is null ? null : await storage.TryGetFolderFromPathAsync(new Uri(directory));
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.T("picker.openBinder"),
            SuggestedStartLocation = start,
            AllowMultiple = false,
            FileTypeFilter = new[] { BinderType() },
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public Task<string?> PickBinderToCreateAsync(string? directory) =>
        PickBinderToCreateAsync(RequireOwner().StorageProvider, directory);

    internal static async Task<string?> PickBinderToCreateAsync(IStorageProvider storage, string? directory)
    {
        var start = directory is null ? null : await storage.TryGetFolderFromPathAsync(new Uri(directory));
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.T("picker.newBinder"),
            SuggestedStartLocation = start,
            ShowOverwritePrompt = true,
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
        var dialog = await TryShowAsync(_ => new MessageDialog(title, message, new[]
        {
            new DialogButton("common.cancel", "cancel"),
            new DialogButton(confirmLabelKey, "confirm", destructive ? DialogButtonKind.Destructive : DialogButtonKind.Primary),
        }));
        return dialog?.ResultTag == "confirm";
    }

    public async Task ShowErrorAsync(Message title, Message message)
    {
        await TryShowAsync(_ => new MessageDialog(title, message, new[] { new DialogButton("common.ok", "ok", DialogButtonKind.Primary) }));
    }

    public async Task ShowAboutAsync()
    {
        await TryShowAsync(_ => new AboutDialog(_log));
    }

    public async Task ShowShortcutsAsync()
    {
        await TryShowAsync(owner => new ShortcutsDialog(ShortcutCatalog.Build(owner)));
    }

    public async Task<bool> ShowSettingsAsync(AppConfig config, Func<AppConfig, Task<bool>> trySave)
    {
        var dialog = await TryShowAsync(_ => new SettingsDialog(config, trySave,
            logSaveFailure: ex => _log.Error("Failed to save settings", error: ex)));
        return dialog?.Applied == true;
    }

    public async Task<ExternalChangeChoice> AskExternalChangeAsync(string binderName)
    {
        var dialog = new MessageDialog(
            Message.Of("binder.changedTitle"),
            Message.Of("binder.changedMessage", ("name", QuotedValue.Name(binderName))),
            new[]
            {
                new DialogButton("binder.keepMine", "keep"),
                // Reloading discards the user's unsaved local edits; keeping them replaces the version on disk.
                // Each gives up a version and putting the choice off would leave the edits only in memory, so
                // neither is focused and the question cannot be dismissed (modal-dialog conventions).
                new DialogButton("binder.reloadFromDisk", "reload", DialogButtonKind.Destructive),
            },
            dismissable: false);
        await Prepare(dialog).ShowBoundedAsync(RequireOwner());
        return dialog.ResultTag switch
        {
            "keep" => ExternalChangeChoice.KeepMine,
            "reload" => ExternalChangeChoice.ReloadFromDisk,
            _ => ExternalChangeChoice.Unanswered,
        };
    }

    public async Task<bool> WaitForAttachmentsAsync(Task settled, TimeSpan stopOfferedAfter)
    {
        if (settled.IsCompleted)
        {
            return false;
        }

        var dialog = new AttachmentWaitDialog(settled, stopOfferedAfter);
        await Prepare(dialog).ShowBoundedAsync(RequireOwner());
        return dialog.ResultTag == AttachmentWaitDialog.StopTag;
    }

    public async Task<UnsavedQuitChoice> AskQuitWithUnsavedBinderAsync(string binderName)
    {
        var dialog = new MessageDialog(
            Message.Of("quit.unsavedTitle"),
            Message.Of("quit.unsavedMessage", ("name", QuotedValue.Name(binderName))),
            new[]
            {
                // Cancel keeps the app open with the binder and its edits, so it is the labelled way out
                // and takes focus; Escape does the same. Retry keeps the edits too; quitting without them is
                // the destructive choice.
                new DialogButton("common.cancel", "cancel"),
                new DialogButton("quit.retry", "retry"),
                new DialogButton("quit.quitAnyway", "quit", DialogButtonKind.Destructive),
            },
            // One width in every language, wide enough for all three translated actions.
            width: 480);
        await Prepare(dialog).ShowBoundedAsync(RequireOwner());
        return dialog.ResultTag switch
        {
            "retry" => UnsavedQuitChoice.Retry,
            "quit" => UnsavedQuitChoice.QuitAnyway,
            _ => UnsavedQuitChoice.Stay,
        };
    }

    public async Task OpenPathExternallyAsync(string path)
    {
        var owner = RequireOwner();
        await ExternalLaunch.RequireSuccessAsync(owner.Launcher.LaunchFileInfoAsync(new FileInfo(path)));
    }

    private Window RequireOwner() =>
        Owner ?? throw new InvalidOperationException("Dialog owner window has not been set.");

    private T Prepare<T>(T dialog) where T : DialogBase
    {
        dialog.LogFailure = (message, error) => _log.Error(message, error: error);
        return dialog;
    }

    // Only ordinary display failures end as a dismissed dialog here. Questions whose failure changes
    // an in-progress operation remain rejectable so that operation's owner can retain its work.
    private async Task<T?> TryShowAsync<T>(Func<Window, T> create) where T : DialogBase
    {
        T? dialog = null;
        try
        {
            var owner = RequireOwner();
            dialog = Prepare(create(owner));
            await dialog.ShowBoundedAsync(owner);
            return dialog;
        }
        catch (Exception ex)
        {
            _log.Error("Failed to show dialog", error: ex);
            try
            {
                // Constructors may subscribe to platform events before a window is shown.
                dialog?.Close();
            }
            catch (Exception closeError)
            {
                _log.Warn("Failed to close dialog after display failure", error: closeError);
            }
            return null;
        }
    }
}
