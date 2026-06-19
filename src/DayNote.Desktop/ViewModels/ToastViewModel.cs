using Avalonia;
using Avalonia.Media;
using DayNote.Desktop.Services;

namespace DayNote.Desktop.ViewModels;

/// <summary>
/// One transient toast in the top-right overlay. The kind selects the left accent stripe's color
/// (matching the app palette); the message is plain text. Toasts are added and auto-removed by
/// <see cref="MainWindowViewModel"/>.
/// </summary>
public sealed class ToastViewModel
{
    public ToastViewModel(ToastKind kind, string message)
    {
        Kind = kind;
        Message = message;
        Accent = kind switch
        {
            ToastKind.Warning => Brush("WarningBrush"),
            ToastKind.Error => Brush("DangerBrush"),
            _ => Brush("AccentBrush"),
        };
    }

    public ToastKind Kind { get; }

    public string Message { get; }

    public IBrush Accent { get; }

    private static IBrush Brush(string key) =>
        Application.Current is { } app
        && app.Resources.TryGetResource(key, null, out var value)
        && value is IBrush brush
            ? brush
            : Brushes.Transparent;
}
