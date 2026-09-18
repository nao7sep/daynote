using Avalonia.Automation;

namespace DayNote.ViewModels;

/// <summary>
/// Presentation data for an app-controlled result. The surface that owns the operation decides
/// where the result is rendered and how it is cleared; this type owns only severity, accessibility,
/// and message data shared by those surfaces. The views map the severity to theme brushes.
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
        AccessibleMessage = message;
        LiveSetting = kind == OperationResultKind.Error
            ? AutomationLiveSetting.Assertive
            : AutomationLiveSetting.Polite;
    }

    public OperationResultKind Kind { get; }

    public string Message { get; }

    public string AccessibleMessage { get; }

    public AutomationLiveSetting LiveSetting { get; }

    public bool IsWarning => Kind == OperationResultKind.Warning;

    public bool IsError => Kind == OperationResultKind.Error;

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
