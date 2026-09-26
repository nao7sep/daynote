namespace DayNote.ViewModels;

/// <summary>
/// Chooses contextual messages for the main window's mandatory list panes, as catalogue keys. Returning
/// null keeps the overlay out of the way whenever the pane has visible rows.
/// </summary>
internal static class MainWindowEmptyStates
{
    internal static string? Binders(int totalCount, int visibleCount, string filter)
    {
        if (visibleCount > 0)
        {
            return null;
        }

        return totalCount > 0 && !string.IsNullOrWhiteSpace(filter)
            ? "binders.noMatch"
            : "binders.empty";
    }

    internal static string? Notes(bool hasBinder, int totalCount, int visibleCount, string filter)
    {
        if (visibleCount > 0)
        {
            return null;
        }

        if (!hasBinder)
        {
            return "notes.noBinder";
        }

        return totalCount > 0 && !string.IsNullOrWhiteSpace(filter)
            ? "notes.noMatch"
            : "notes.empty";
    }

    internal static string? Attachments(bool hasSelectedNote, int visibleCount)
    {
        if (visibleCount > 0)
        {
            return null;
        }

        return hasSelectedNote
            ? "attachments.empty"
            : "attachments.noNote";
    }
}
