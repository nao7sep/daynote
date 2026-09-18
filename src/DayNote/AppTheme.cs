using Avalonia;
using Avalonia.Styling;
using DayNote.Core.Configuration;

namespace DayNote;

/// <summary>
/// The application's requested theme variant is DayNote's one theme authority (app-chrome
/// conventions, Theme): every window inherits it, Avalonia paints each native title bar from it,
/// and App.axaml's Light and Dark theme dictionaries resolve through it. Set on the UI thread at the
/// composition root and when Settings commits, never from a view model.
/// </summary>
public static class AppTheme
{
    /// <summary>System follows the OS through <see cref="ThemeVariant.Default"/>.</summary>
    public static ThemeVariant VariantFor(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => ThemeVariant.Light,
        ThemePreference.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    public static void Apply(ThemePreference preference)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = VariantFor(preference);
    }
}
