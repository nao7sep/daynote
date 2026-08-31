using Avalonia.Automation;
using Avalonia.Media;
using DayNote.Services;

namespace DayNote.ViewModels;

/// <summary>
/// One in-window result. Severity is expressed in text and through the accessibility tree as well
/// as by the palette accent; committed non-successful operations remain until dismissed while
/// ordinary information may still expire.
/// </summary>
public sealed class ToastViewModel
{
    public ToastViewModel(ToastKind kind, string message, bool isPersistent = false)
    {
        Kind = kind;
        Message = message;
        IsPersistent = isPersistent;
        SeverityLabel = kind switch
        {
            ToastKind.Warning => "Warning",
            ToastKind.Error => "Error",
            _ => "Information",
        };
        AccessibleMessage = $"{SeverityLabel}: {message}";
        LiveSetting = kind == ToastKind.Error
            ? AutomationLiveSetting.Assertive
            : AutomationLiveSetting.Polite;
        Accent = kind switch
        {
            ToastKind.Warning => PaletteBrush.Resolve("WarningBrush"),
            ToastKind.Error => PaletteBrush.Resolve("DangerBrush"),
            _ => PaletteBrush.Resolve("AccentBrush"),
        };
    }

    public ToastKind Kind { get; }

    public string Message { get; }

    public string SeverityLabel { get; }

    public string AccessibleMessage { get; }

    public AutomationLiveSetting LiveSetting { get; }

    public IBrush Accent { get; }

    public bool IsPersistent { get; }
}
