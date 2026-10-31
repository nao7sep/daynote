using DayNote.Core.Storage;
using DayNote.Core.Toml;
using DayNote.I18n;

namespace DayNote.ViewModels;

/// <summary>
/// Maps diagnostic exceptions to deliberately authored, user-safe DayNote copy. Each answer is a held
/// message, rendered where it is shown, so it follows a language change and never carries the
/// exception's own text.
/// </summary>
public static class FailurePresentation
{
    public static Message StartupStorage() => Message.Of("failure.startupStorage");

    /// <summary>
    /// Why the settings file stopped startup. Every case leaves the file untouched: it may hold authored
    /// text styles, so DayNote never replaces it with defaults (store-recovery-conventions).
    /// </summary>
    public static Message StartupSettings(string configFile, Exception error) => Message.Of(error switch
    {
        NewerFormatException => "failure.startupSettingsNewer",
        InvalidDataException => "failure.startupSettingsMalformed",
        _ => "failure.startupSettings",
    }, ("path", QuotedValue.Path(configFile)));

    /// <summary>
    /// Why the binder list stopped startup: a newer DayNote wrote it, or it could not be read. The file is
    /// left untouched, since it is the list the user keeps.
    /// </summary>
    public static Message StartupBinders(string bindersFile, Exception error) => Message.Of(
        error is NewerFormatException ? "failure.startupBindersNewer" : "failure.startupBinders",
        ("path", QuotedValue.Path(bindersFile)));

    /// <summary>The view state was written by a newer DayNote, which stops startup, untouched.</summary>
    public static Message StartupStateNewer(string stateFile) =>
        Message.Of("failure.startupStateNewer", ("path", QuotedValue.Path(stateFile)));

    /// <summary>The records database was written by a newer DayNote, which stops startup, untouched.</summary>
    public static Message StartupRecordsNewer(string recordsFile) =>
        Message.Of("failure.startupRecordsNewer", ("path", QuotedValue.Path(recordsFile)));

    /// <summary>The backup history was written by a newer DayNote, which stops startup, untouched.</summary>
    public static Message StartupBackupsNewer(string backupsFile) =>
        Message.Of("failure.startupBackupsNewer", ("path", QuotedValue.Path(backupsFile)));

    /// <summary>Every file that stopped startup, in one message, each in its own paragraph.</summary>
    public static Message StartupStop(IReadOnlyList<Message> stops) => Message.Join("failure.startupJoin", stops);

    /// <summary>The records database could not be read: untouched, and this session records to a text file.</summary>
    public static Message RecordsOff(string recordsFile, string logsDirectory) => Message.Of(
        "failure.recordsOff", ("path", QuotedValue.Path(recordsFile)), ("folder", QuotedValue.Path(logsDirectory)));

    /// <summary>The backup history could not be read: untouched, and nothing is recorded this session.</summary>
    public static Message BackupsOff(string backupsFile) =>
        Message.Of("failure.backupsOff", ("path", QuotedValue.Path(backupsFile)));

    public static Message OpenBinder(Exception error, string binderTitle) => error switch
    {
        NewerFormatException => NewerBinder(binderTitle),
        BinderFormatException => Message.Of("failure.openBinderFormat", ("name", QuotedValue.Name(binderTitle))),
        UnauthorizedAccessException => Message.Of("failure.openBinderPermission"),
        FileNotFoundException or DirectoryNotFoundException => Message.Of("failure.openBinderGone"),
        _ => Message.Of("failure.openBinder"),
    };

    /// <summary>A binder written by a newer DayNote, which this build neither opens nor writes.</summary>
    public static Message NewerBinder(string binderTitle) => Message.Of("failure.openBinderNewer", ("name", QuotedValue.Name(binderTitle)));

    public static Message SaveBinder(Exception error) => Message.Of(error switch
    {
        UnauthorizedAccessException => "failure.saveBinderPermission",
        DirectoryNotFoundException => "failure.saveBinderGone",
        _ => "failure.saveBinder",
    });

    public static Message NewBinderPicker(Exception error) => Message.Of("failure.newBinderPicker");

    public static Message OpenBinderPicker(Exception error) => Message.Of("failure.openBinderPicker");

    public static Message AttachmentPicker(Exception error) => Message.Of("failure.attachmentPicker");

    public static Message ReloadBinder(Exception error) => Message.Of("failure.reloadBinder");

    public static Message OpenExternalLink(Exception error) => Message.Of("failure.openExternalLink");
}
