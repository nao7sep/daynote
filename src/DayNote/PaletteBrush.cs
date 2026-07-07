using Avalonia;
using Avalonia.Media;

namespace DayNote;

/// <summary>
/// Resolves a named brush from the application's theme resources (the palette declared in App.axaml),
/// so every view and view model shares one lookup instead of repeating it. Returns
/// <paramref name="fallback"/> (or transparent) when the key is missing or the app is not yet up.
/// </summary>
public static class PaletteBrush
{
    public static IBrush Resolve(string key, IBrush? fallback = null) =>
        Application.Current is { } app
        && app.Resources.TryGetResource(key, null, out var value)
        && value is IBrush brush
            ? brush
            : fallback ?? Brushes.Transparent;
}
