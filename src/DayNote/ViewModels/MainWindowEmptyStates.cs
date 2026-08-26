namespace DayNote.ViewModels;

/// <summary>
/// Chooses contextual messages for the main window's mandatory list panes. Returning an empty
/// string keeps the overlay out of the way whenever the pane has visible rows.
/// </summary>
internal static class MainWindowEmptyStates
{
    internal static string Binders(int totalCount, int visibleCount, string filter)
    {
        if (visibleCount > 0)
        {
            return string.Empty;
        }

        return totalCount > 0 && !string.IsNullOrWhiteSpace(filter)
            ? "No binders match this filter."
            : "No binders yet. Create or open one.";
    }

    internal static string Notes(bool hasBinder, int totalCount, int visibleCount, string filter)
    {
        if (visibleCount > 0)
        {
            return string.Empty;
        }

        if (!hasBinder)
        {
            return "Open or create a binder to add notes.";
        }

        return totalCount > 0 && !string.IsNullOrWhiteSpace(filter)
            ? "No notes match this filter."
            : "No notes yet. Create one to get started.";
    }

    internal static string Attachments(bool hasSelectedNote, int visibleCount)
    {
        if (visibleCount > 0)
        {
            return string.Empty;
        }

        return hasSelectedNote
            ? "No attachments yet."
            : "Select or create a note to add attachments.";
    }
}
