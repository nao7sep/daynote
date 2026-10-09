namespace DayNote.Views;

/// <summary>
/// Derives the main window's minimum size and pane-layout sizes from the layout itself
/// (window-conventions). The pane Grid's four content columns drive the minimum width; the toolbar,
/// status bar, and the tallest pane's content minimum drive the minimum height. Notifications
/// overlay the panes and therefore contribute no layout reserve. The side panes are displayed from
/// the user's drag intents, clamped to the current window; the editor is the fill pane.
/// </summary>
public static class WindowMetrics
{
    // Native frame rounding and decoration measurements can differ from the working area slightly.
    private const double MaximizedSizeTolerance = 8;

    public static Avalonia.Controls.WindowState RestoredWindowState(bool maximized, bool isWindows) =>
        maximized && isWindows
            ? Avalonia.Controls.WindowState.Maximized
            : Avalonia.Controls.WindowState.Normal;

    // The strip along a window's top edge that the user drags it by: the title bar, at least as tall as the
    // tallest system title bar DayNote meets (macOS 28, Windows about 31 device-independent pixels).
    private const double TitleBarHeight = 32;

    // How much of that strip must be on the screen for the user to grab it: room beside the window
    // buttons on either platform, or the whole strip of a narrower window.
    private const double GrabWidth = 120;

    /// <summary>
    /// Whether a saved window can be put back on this screen and still be moved by ordinary interaction
    /// (window-conventions, Placement): its title bar lies on the working area from top to bottom, with
    /// enough of its width on it to grab. Overlap alone is not enough, since a corner of the window can sit
    /// on the screen with its title bar off it. The position is in device pixels, as the screen reports it,
    /// and may be negative on a display left of or above the primary one; the size is in device-independent
    /// pixels, scaled by the screen's factor.
    /// </summary>
    public static bool CanRestoreWindowGeometry(
        int? x, int? y, double? width, double? height,
        Avalonia.PixelRect workingArea, double scaling)
    {
        if (x is not { } savedX || y is not { } savedY
            || width is not > 0 || height is not > 0
            || !double.IsFinite(width.Value) || !double.IsFinite(height.Value)
            || workingArea.Width <= 0 || workingArea.Height <= 0
            || scaling <= 0 || !double.IsFinite(scaling))
        {
            return false;
        }

        var titleBarTop = (double)savedY;
        var titleBarBottom = titleBarTop + TitleBarHeight * scaling;
        if (titleBarTop < workingArea.Y || titleBarBottom > (double)workingArea.Y + workingArea.Height)
        {
            return false;
        }

        var windowWidth = width.Value * scaling;
        var visible = Math.Min((double)savedX + windowWidth, (double)workingArea.X + workingArea.Width)
            - Math.Max(savedX, workingArea.X);
        return visible >= Math.Min(windowWidth, GrabWidth * scaling);
    }

    public static bool IsMaximizedGeometry(
        Avalonia.Size frameSize, Avalonia.PixelRect workingArea, double scale)
    {
        if (scale <= 0 || !double.IsFinite(scale))
            return false;

        var workWidth = workingArea.Width / scale;
        var workHeight = workingArea.Height / scale;
        return frameSize.Width >= workWidth - MaximizedSizeTolerance
            && frameSize.Height >= workHeight - MaximizedSizeTolerance;
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

    public static double MinHeightFor(double tallestPaneMinHeight)
        => ToolbarHeight + StatusBarHeight + PaneVerticalMargin
            + tallestPaneMinHeight;

    // A dialog takes this much of the screen it opens on, never all of it. Not a share of its owner:
    // a dialog is a separate window, and an owner that happens to be small says nothing about how much
    // room the dialog has (modal-dialog conventions).
    public const double DialogHeightFraction = 0.85;

    /// <summary>
    /// The tallest a dialog's content may be: <see cref="DialogHeightFraction"/> of the working area of
    /// the screen it opens on. A working area that cannot be read leaves the dialog unbounded rather
    /// than guessing a height for it.
    /// </summary>
    public static double DialogMaxHeight(double workingAreaHeight, double scale) =>
        workingAreaHeight > 0 && scale > 0 && double.IsFinite(scale)
            ? workingAreaHeight / scale * DialogHeightFraction
            : double.PositiveInfinity;


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
