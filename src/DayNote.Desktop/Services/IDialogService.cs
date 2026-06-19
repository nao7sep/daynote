using DayNote.Core.Configuration;
using DayNote.Core.Storage;

namespace DayNote.Desktop.Services;

/// <summary>
/// The application's own modal user interface plus the one operating-system dialog it uses — the
/// native file picker. Implemented by the view layer so view models stay free of Avalonia types.
/// </summary>
public interface IDialogService
{
    /// <summary>Native open picker for an existing notebook. Returns the chosen path or null.</summary>
    Task<string?> PickNotebookToOpenAsync();

    /// <summary>Native save picker for a new notebook. Returns the chosen path or null.</summary>
    Task<string?> PickNotebookToCreateAsync();

    /// <summary>Native open picker for attachment files. Returns the chosen paths (possibly empty).</summary>
    Task<IReadOnlyList<string>> PickAttachmentsAsync();

    /// <summary>A custom yes/no confirmation. Returns true if confirmed.</summary>
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>A custom error dialog.</summary>
    Task ShowErrorAsync(string title, string message);

    /// <summary>The custom About dialog.</summary>
    Task ShowAboutAsync();

    /// <summary>The custom keyboard-shortcuts dialog.</summary>
    Task ShowShortcutsAsync();

    /// <summary>The custom settings dialog; mutates <paramref name="config"/> and returns true if applied.</summary>
    Task<bool> ShowSettingsAsync(AppConfig config);

    /// <summary>Asks how to handle a notebook already locked by another instance.</summary>
    Task<LockedNotebookChoice> AskLockedNotebookAsync(string notebookName);

    /// <summary>Asks how to handle an external modification detected against unsaved edits.</summary>
    Task<ExternalChangeChoice> AskExternalChangeAsync(string notebookName, ExternalChange change);

    /// <summary>Opens a file with the operating system's default handler.</summary>
    Task OpenPathExternallyAsync(string path);

    /// <summary>Shows a transient in-window notification (toast).</summary>
    void Notify(ToastKind kind, string message);
}
