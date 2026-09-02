using Avalonia.Automation;
using Avalonia.Media;

namespace DayNote.ViewModels;

/// <summary>
/// Presentation data for an app-controlled result. The surface that owns the operation decides
/// where the result is rendered and how it is cleared; this type owns only severity, accessibility,
/// palette, and message data shared by those surfaces.
/// </summary>
public sealed class OperationResultViewModel
{
    public OperationResultViewModel(
        OperationResultKind kind,
        string message,
        bool isPersistent = false,
        string? resultKey = null)
    {
        Kind = kind;
        Message = message;
        IsPersistent = isPersistent;
        ResultKey = resultKey;
        SeverityLabel = kind switch
        {
            OperationResultKind.Warning => "Warning",
            OperationResultKind.Error => "Error",
            _ => "Information",
        };
        AccessibleMessage = $"{SeverityLabel}: {message}";
        LiveSetting = kind == OperationResultKind.Error
            ? AutomationLiveSetting.Assertive
            : AutomationLiveSetting.Polite;
        Accent = kind switch
        {
            OperationResultKind.Warning => PaletteBrush.Resolve("WarningBrush"),
            OperationResultKind.Error => PaletteBrush.Resolve("DangerBrush"),
            _ => PaletteBrush.Resolve("AccentBrush"),
        };
    }

    public OperationResultKind Kind { get; }

    public string Message { get; }

    public string SeverityLabel { get; }

    public string AccessibleMessage { get; }

    public AutomationLiveSetting LiveSetting { get; }

    public IBrush Accent { get; }

    public bool IsPersistent { get; }

    /// <summary>Identity of one still-active shell result. Null means the owner clears it directly.</summary>
    public string? ResultKey { get; }
}

/// <summary>The severity of an app-controlled operation result.</summary>
public enum OperationResultKind
{
    Info,
    Warning,
    Error,
}
