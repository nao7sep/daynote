using DayNote.Core.Configuration;
using DayNote.I18n;

namespace DayNote.Services;

/// <summary>
/// The application's own modal user interface plus the one operating-system dialog it uses — the
/// native file picker. Implemented by the view layer so view models stay free of Avalonia types.
/// </summary>
public interface IDialogService
{
    /// <summary>Native open picker for an existing binder. Returns the chosen path or null.</summary>
    Task<string?> PickBinderToOpenAsync();

    /// <summary>Native save picker for a new binder. Returns the chosen path or null.</summary>
    Task<string?> PickBinderToCreateAsync();

    /// <summary>Native open picker for attachment files. Returns the chosen paths (possibly empty).</summary>
    Task<IReadOnlyList<string>> PickAttachmentsAsync();

    /// <summary>
    /// A custom confirmation with a specific confirm label, given by its catalogue key (e.g.
    /// <c>common.delete</c>), danger-styled when <paramref name="destructive"/>. Returns true if
    /// confirmed. A destructive prompt focuses Cancel.
    /// </summary>
    Task<bool> ConfirmAsync(Message title, Message message, string confirmLabelKey, bool destructive = false);

    /// <summary>A custom error dialog.</summary>
    Task ShowErrorAsync(Message title, Message message);

    /// <summary>The custom About dialog.</summary>
    Task ShowAboutAsync();

    /// <summary>The custom keyboard-shortcuts dialog.</summary>
    Task ShowShortcutsAsync();

    /// <summary>The custom settings dialog; keeps a failed save inline and returns true only after persistence succeeds.</summary>
    Task<bool> ShowSettingsAsync(AppConfig config, Func<AppConfig, bool> trySave);

    /// <summary>
    /// Asks how to handle an external modification detected against unsaved edits. Both answers give up a
    /// version, so the question cannot be dismissed and no answer is focused; it closes unanswered only when
    /// the app or the operating system closes it.
    /// </summary>
    Task<ExternalChangeChoice> AskExternalChangeAsync(string binderName);

    /// <summary>
    /// Shows that attachments are still being added until <paramref name="settled"/> completes, then closes
    /// itself. After <paramref name="stopOfferedAfter"/> it offers Stop. Returns true when the user stopped
    /// waiting; false when the work settled or the app or the operating system closed it.
    /// </summary>
    Task<bool> WaitForAttachmentsAsync(Task settled, TimeSpan stopOfferedAfter);

    /// <summary>
    /// Asks, after a quit could not save the binder's edits, whether to retry the save or quit without
    /// them. Dismissing it answers <see cref="UnsavedQuitChoice.Stay"/>.
    /// </summary>
    Task<UnsavedQuitChoice> AskQuitWithUnsavedBinderAsync(string binderName);

    /// <summary>Opens a file with the operating system's default handler.</summary>
    Task OpenPathExternallyAsync(string path);
}
