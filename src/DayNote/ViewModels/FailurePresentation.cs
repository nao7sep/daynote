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

    public static Message StartupData() => Message.Of("failure.startupData");

    public static Message RecoveredData(bool binderListWasReset) =>
        Message.Of(binderListWasReset ? "quarantine.binderListBody" : "quarantine.settingsBody");

    public static Message OpenBinder(Exception error) => Message.Of(error switch
    {
        BinderFormatException => "failure.openBinderFormat",
        UnauthorizedAccessException => "failure.openBinderPermission",
        FileNotFoundException or DirectoryNotFoundException => "failure.openBinderGone",
        _ => "failure.openBinder",
    });

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
