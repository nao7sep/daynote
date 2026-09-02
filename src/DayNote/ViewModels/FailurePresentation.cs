using DayNote.Core.Toml;

namespace DayNote.ViewModels;

/// <summary>Maps diagnostic exceptions to deliberately authored, user-safe DayNote copy.</summary>
public static class FailurePresentation
{
    public static string StartupData() =>
        "DayNote could not read its configuration or state files, so saving is disabled to avoid " +
        "overwriting good data. Check the session log, repair or remove the affected data file, and restart.";

    public static string RecoveredData(bool binderListWasReset) => binderListWasReset
        ? "A data file was unreadable, so DayNote preserved it rather than overwriting it. DayNote " +
          "started with an empty binder list; your binder files are untouched, but the list of open " +
          "binders and their custom titles is only in the preserved copy. Check the session log for " +
          "its location, then re-open your binders or recover the list before quitting."
        : "A settings file was unreadable, so DayNote preserved it and started with defaults in its " +
          "place. Your binders and notes are untouched. Check the session log for the preserved copy's location.";

    public static string OpenBinder(Exception error) => error switch
    {
        BinderFormatException =>
            "This binder could not be opened because its contents are not valid DayNote data. " +
            "Repair the binder or restore a known-good copy, then try again.",
        UnauthorizedAccessException =>
            "This binder could not be opened. Check that you have permission to read it, then try again.",
        FileNotFoundException or DirectoryNotFoundException =>
            "This binder is no longer available at the selected location.",
        _ => "This binder could not be opened. Check that it is available, then try again.",
    };

    public static string SaveBinder(Exception error) => error switch
    {
        UnauthorizedAccessException =>
            "Your changes are still in DayNote, but the binder could not be saved. " +
            "Check that the binder location is writable, then try again.",
        DirectoryNotFoundException =>
            "Your changes are still in DayNote, but the binder location is no longer available. " +
            "Restore the location, then try again.",
        _ =>
            "Your changes are still in DayNote, but the binder could not be saved. " +
            "Check that its location is available and has enough free space, then try again.",
    };
}
