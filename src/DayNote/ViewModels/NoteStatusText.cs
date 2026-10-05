using Avalonia.Data.Converters;
using DayNote.Core.Models;

namespace DayNote.ViewModels;

/// <summary>
/// A lifecycle status as the reader sees it: catalogue text, never the enum's own name. The stored
/// value keeps its English token in every file; only the words on screen change with the language.
/// </summary>
public static class NoteStatusText
{
    /// <summary>The catalogue key naming <paramref name="status"/>.</summary>
    public static string KeyOf(NoteStatus status) => status switch
    {
        NoteStatus.Discarded => "status.discarded",
        NoteStatus.Verified => "status.verified",
        NoteStatus.Published => "status.published",
        NoteStatus.Retired => "status.retired",
        _ => "status.draft",
    };

    /// <summary>
    /// For markup: turns a bound status into its key, which a <c>Localized</c> property then renders
    /// and keeps in the current language.
    /// </summary>
    public static readonly IValueConverter Key =
        new FuncValueConverter<NoteStatus, string>(status => KeyOf(status));
}
