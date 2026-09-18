using Avalonia;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace DayNote.Views;

/// <summary>
/// Theme-following resources for code-built controls: the code equivalent of
/// <c>{DynamicResource}</c>. A brush read once from the resources would freeze the control in the
/// theme it was built in, because the palette lives in App.axaml's Light and Dark theme dictionaries.
/// </summary>
internal static class ThemeResources
{
    public static T Themed<T>(this T control, AvaloniaProperty property, string key)
        where T : AvaloniaObject
    {
        control[!property] = new DynamicResourceExtension(key);
        return control;
    }
}
