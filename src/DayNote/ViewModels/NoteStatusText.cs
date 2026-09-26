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
        NoteStatus.Ready => "status.ready",
        NoteStatus.Published => "status.published",
        NoteStatus.Expired => "status.expired",
        _ => "status.draft",
    };

    /// <summary>
    /// For markup: turns a bound status into its key, which a <c>Localized</c> property then renders
    /// and keeps in the current language.
    /// </summary>
    public static readonly IValueConverter Key =
        new FuncValueConverter<NoteStatus, string>(status => KeyOf(status));
}
