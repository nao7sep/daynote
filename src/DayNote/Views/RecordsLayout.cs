namespace DayNote.Views;

/// <summary>
/// The records window's pane sizes (window-conventions, Content-based minimum size): a list pane the
/// user sizes by dragging, beside the detail pane, which takes the rest. The numbers mirror
/// RecordsWindow.axaml, whose column bounds are <see cref="ListMin"/>, <see cref="ListMax"/> and
/// <see cref="DetailMin"/>; a column's width includes its pane's margin and border, as the main
/// window's do.
/// </summary>
public static class RecordsLayout
{
    public const double ListMin = 320;
    public const double ListDefault = 380;
    public const double ListMax = 640;
    public const double DetailMin = 420;

    // The list body's smallest useful height: a few rows.
    public const double ListBodyMin = 160;

    // The pane grid's margin (4 each side), a pane's margin (4 each side) and border (1 each side),
    // and the splitter between the panes.
    private const double GridMargin = 4 + 4;
    private const double PaneChrome = 4 + 4 + 1 + 1;
    private const double Splitter = 6;

    // The list body's own margin (8 above and below), and the room the end-of-list note takes when a
    // page is loading or failed: one line with its padding.
    private const double ListBodyChrome = 8 + 8;
    private const double EndNoteReserve = 26;

    /// <summary>The window's minimum width: both pane minimums and everything beside them.</summary>
    public static double MinWidth => GridMargin + ListMin + Splitter + DetailMin;

    /// <summary>The window's minimum height, under a filter band of <paramref name="filtersHeight"/>.</summary>
    public static double MinHeight(double filtersHeight) =>
        GridMargin + PaneChrome + filtersHeight + ListBodyChrome + ListBodyMin + EndNoteReserve;

    /// <summary>
    /// The list pane's displayed width: the user's <paramref name="intent"/>, within its bounds, and
    /// narrowed so the detail pane keeps its minimum in a window <paramref name="windowWidth"/> wide.
    /// </summary>
    public static double ListWidth(double intent, double windowWidth)
    {
        var room = windowWidth - GridMargin - Splitter - DetailMin;
        var ceiling = Math.Max(ListMin, Math.Min(ListMax, room));
        return double.IsFinite(intent) ? Math.Clamp(intent, ListMin, ceiling) : Math.Min(ListDefault, ceiling);
    }
}
