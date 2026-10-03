namespace DayNote.ViewModels;

/// <summary>A window's last normal rectangle, and whether it was maximized on Windows.</summary>
public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool Maximized);

/// <summary>
/// What the records window needs from the app beside the records themselves: its saved layout, kept
/// in <c>state.json</c> with the rest of the app's state, and the zone times are shown in.
/// </summary>
public interface IRecordsWindowHost
{
    /// <summary>The list pane's last dragged width.</summary>
    double RecordsListWidth { get; }

    /// <summary>Saves the list pane's width after a drag.</summary>
    void SaveRecordsListWidth(double width);

    /// <summary>The window's saved placement, or null when none is saved.</summary>
    WindowPlacement? RecordsWindowPlacement { get; }

    /// <summary>Saves the window's placement as it closes.</summary>
    void SaveRecordsWindowPlacement(WindowPlacement placement);

    TimeZoneInfo DisplayZone { get; }
}
