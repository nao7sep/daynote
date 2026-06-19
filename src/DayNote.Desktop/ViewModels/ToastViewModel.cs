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
            ToastKind.Warning => new SolidColorBrush(Color.Parse("#F59E0B")),
            ToastKind.Error => new SolidColorBrush(Color.Parse("#EF4444")),
            _ => new SolidColorBrush(Color.Parse("#3B82F6")),
        };
    }

    public ToastKind Kind { get; }

    public string Message { get; }

    public IBrush Accent { get; }
}
