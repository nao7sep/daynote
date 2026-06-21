namespace DayNote.Desktop.Views;

/// <summary>
/// Derives the main window's minimum size from the layout itself, per the window-chrome
/// conventions: the minimum is the sum of the content panes' real minimums plus the fixed
/// chrome — never a hand-typed magic constant. The pane Grid's four content columns drive the
/// minimum width; the toolbar, status bar, and the tallest pane's content minimum drive the
/// minimum height.
/// </summary>
/// <remarks>
/// Kept as a pure function over the column minimums (read from the live grid by the caller) and
/// the pane content minimum, so the window minimum and the columns can never drift apart, and so
/// the derivation can be tested without a UI thread — mirroring pathhide's WindowMetrics.
/// </remarks>
public static class WindowMetrics
{
    // The pane Grid carries Margin="4" on all sides, so it loses 4px of horizontal room on each
    // edge before the columns are laid out.
    private const double GridHorizontalMargin = 4 + 4;

    // Each GridSplitter between the panes is Width="6"; there are three of them (one between each
    // adjacent pair of the four content panes).
    private const double SplitterWidth = 6;
    private const int SplitterCount = 3;

    // Fixed chrome heights: the toolbar Border (StackPanel Margin="12,8" + a 15px title) and the
    // status-bar Border (TextBlock Margin="12,5" + ~16px text), measured to match Fluent's natural
    // sizing of those rows. Mirrors pathhide's ToolbarHeight/StatusBarHeight constants.
    private const double ToolbarHeight = 52;
    private const double StatusBarHeight = 33;

    // The pane Grid's Margin="4" (top+bottom) plus each pane Border's own Margin="4" (top+bottom)
    // both sit inside the star-sized middle row, so they add to the height the panes need before
    // their content minimum is even reached.
    private const double PaneVerticalMargin = (4 + 4) + (4 + 4);

    /// <summary>
    /// The minimum window width: the sum of the content columns' minimum widths plus the three
    /// splitters plus the pane Grid's outer horizontal margin.
    /// </summary>
    public static double MinWidthFor(IEnumerable<double> columnMinWidths)
        => columnMinWidths.Sum() + (SplitterWidth * SplitterCount) + GridHorizontalMargin;

    /// <summary>
    /// The minimum window height: the fixed chrome (toolbar + status bar) plus the tallest pane's
    /// content minimum and the vertical margins that wrap the panes. The editor pane is the tallest,
    /// so its content minimum is the one that must fit in the middle row.
    /// </summary>
    public static double MinHeightFor(double tallestPaneMinHeight)
        => ToolbarHeight + StatusBarHeight + PaneVerticalMargin + tallestPaneMinHeight;
}
