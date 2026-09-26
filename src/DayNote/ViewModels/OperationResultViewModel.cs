using Avalonia.Automation;
using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.I18n;

namespace DayNote.ViewModels;

/// <summary>
/// Presentation data for an app-controlled result. The surface that owns the operation decides
/// where the result is rendered and how it is cleared; this type owns only severity, accessibility,
/// and message data shared by those surfaces. The views map the severity to theme brushes. It holds
/// its <see cref="Message"/>, not the words, so a language change re-renders it where it stands
/// (<see cref="Retranslate"/>).
/// </summary>
public sealed class OperationResultViewModel : ObservableObject
{
    public OperationResultViewModel(
        OperationResultKind kind,
        Message message,
        bool isPersistent = false,
        string? resultKey = null)
    {
        Kind = kind;
        Message = message;
        IsPersistent = isPersistent;
        ResultKey = resultKey;
        LiveSetting = kind == OperationResultKind.Error
            ? AutomationLiveSetting.Assertive
            : AutomationLiveSetting.Polite;
    }

    public OperationResultKind Kind { get; }

    public Message Message { get; }

    /// <summary>The words the reader sees, in the current language.</summary>
    public string Text => Localizer.Of(Message);

    public string AccessibleMessage => Text;

    public AutomationLiveSetting LiveSetting { get; }

    public bool IsWarning => Kind == OperationResultKind.Warning;

    public bool IsError => Kind == OperationResultKind.Error;

    public bool IsPersistent { get; }

    /// <summary>Identity of one still-active shell result. Null means the owner clears it directly.</summary>
    public string? ResultKey { get; }

    /// <summary>Called by the owning view model when the language changes.</summary>
    internal void Retranslate()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(AccessibleMessage));
    }
}

/// <summary>The severity of an app-controlled operation result.</summary>
public enum OperationResultKind
{
    Info,
    Warning,
    Error,
}
