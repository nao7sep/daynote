using Avalonia.Media;
using DayNote.Services;

namespace DayNote.ViewModels;

/// <summary>
/// One in-window result. The kind selects the left accent stripe's color (matching the app
/// palette); committed non-successful operations remain until dismissed while ordinary notices
/// may still expire.
/// </summary>
public sealed class ToastViewModel
{
    public ToastViewModel(ToastKind kind, string message, bool isPersistent = false)
    {
        Kind = kind;
        Message = message;
        IsPersistent = isPersistent;
        Accent = kind switch
        {
            ToastKind.Warning => PaletteBrush.Resolve("WarningBrush"),
            ToastKind.Error => PaletteBrush.Resolve("DangerBrush"),
            _ => PaletteBrush.Resolve("AccentBrush"),
        };
    }

    public ToastKind Kind { get; }

    public string Message { get; }

    public IBrush Accent { get; }

    public bool IsPersistent { get; }
}
