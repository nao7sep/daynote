namespace DayNote.Views;

/// <summary>
/// Derives the main window's minimum size and pane-layout sizes from the layout itself, per the
/// window-chrome conventions: the minimum is the sum of the content panes' real minimums plus the
/// fixed chrome — never a hand-typed magic constant. The pane Grid's four content columns drive
/// the minimum width; the toolbar, status bar, and the tallest pane's content minimum drive the
/// minimum height. The bounded result row is also reserved so visible results never steal the
/// panes' minimum height. The side-pane distribution computes the displayed pixel widths from the
/// user's drag intents, clamped to the current window — so the side panes stay fixed on resize and
/// only the fill pane (the editor) absorbs the change.
/// </summary>
public static class WindowMetrics
{
    public static bool CanSaveWindowGeometry(Avalonia.Controls.WindowState state) =>
        state == Avalonia.Controls.WindowState.Normal;

    public static bool CanRestoreWindowGeometry(
        int? x, int? y, double? width, double? height,
        IEnumerable<Avalonia.PixelRect> workingAreas)
    {
        if (x is not { } savedX || y is not { } savedY
            || width is not > 0 || height is not > 0
            || !double.IsFinite(width.Value) || !double.IsFinite(height.Value))
        {
            return false;
        }

        return workingAreas.Any(area =>
            area.Width > 0 && area.Height > 0
            && savedX >= area.X && savedX < (long)area.X + area.Width
            && savedY >= area.Y && savedY < (long)area.Y + area.Height);
    }

    public static Avalonia.Size CapMinimumToWorkArea(
        Avalonia.Size contentFloor, Avalonia.PixelRect workArea, double scale, Avalonia.Size chrome) =>
        new(System.Math.Min(contentFloor.Width, System.Math.Max(1, workArea.Width / scale - chrome.Width)),
            System.Math.Min(contentFloor.Height, System.Math.Max(1, workArea.Height / scale - chrome.Height)));

    private const double GridHorizontalMargin = 4 + 4;
    private const double SplitterWidth = 6;
    private const int SplitterCount = 3;
    private const double ToolbarHeight = 52;
    private const double StatusBarHeight = 33;
    private const double PaneVerticalMargin = (4 + 4) + (4 + 4);

    public static double MinWidthFor(IEnumerable<double> columnMinWidths)
        => columnMinWidths.Sum() + (SplitterWidth * SplitterCount) + GridHorizontalMargin;

    public static double MinHeightFor(double tallestPaneMinHeight, double resultViewportReserveHeight)
        => ToolbarHeight + StatusBarHeight + PaneVerticalMargin
            + tallestPaneMinHeight + resultViewportReserveHeight;

    /// <summary>
    /// The total pixel budget available for the three side panes (binders, notes, attachments)
    /// after reserving the editor's minimum, the three splitters, and the grid margin.
    /// </summary>
    public static double SidePaneBudget(double windowWidth, double editorMinWidth)
        => windowWidth - editorMinWidth - (SplitterWidth * SplitterCount) - GridHorizontalMargin;

    /// <summary>
    /// Distributes the side-pane budget among three adjustable panes. When all intents fit, each
    /// pane gets its intent and the editor absorbs the rest. When the window is too narrow, the
    /// panes shrink proportionally relative to their excess above minimum — so each pane reaches
    /// its minimum at the same rate and the editor never drops below its own minimum.
    /// </summary>
    public static double[] DistributeSidePanes(double[] intents, double[] mins, double budget)
    {
        var sumMins = mins.Sum();
        budget = Math.Max(sumMins, budget);

        var clamped = new double[intents.Length];
        var sumClamped = 0.0;
        for (var i = 0; i < intents.Length; i++)
        {
            clamped[i] = Math.Max(intents[i], mins[i]);
            sumClamped += clamped[i];
        }

        if (sumClamped <= budget)
            return clamped;

        var slack = budget - sumMins;
        var totalExcess = 0.0;
        for (var i = 0; i < intents.Length; i++)
            totalExcess += Math.Max(0, intents[i] - mins[i]);

        if (totalExcess <= 0)
            return (double[])mins.Clone();

        var displays = new double[intents.Length];
        for (var i = 0; i < intents.Length; i++)
        {
            var excess = Math.Max(0, intents[i] - mins[i]);
            displays[i] = mins[i] + (excess / totalExcess * slack);
        }

        return displays;
    }
}
