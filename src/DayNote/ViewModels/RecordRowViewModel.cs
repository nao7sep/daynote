using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.Logging;

namespace DayNote.ViewModels;

/// <summary>A row in the records list: when, at what level, and the message.</summary>
public sealed partial class RecordRowViewModel : ObservableObject
{
    public RecordRowViewModel(RecordSummary record, TimeZoneInfo displayZone)
    {
        Record = record;
        Refresh(displayZone);
    }

    public RecordSummary Record { get; }

    public string Message => Record.Message;

    public bool IsError => Record.Level == "error";

    public bool IsWarning => Record.Level == "warn";

    [ObservableProperty]
    private string _timeText = string.Empty;

    [ObservableProperty]
    private string _levelText = string.Empty;

    /// <summary>Re-reads the row's words in the current language; the owner calls it on a change.</summary>
    public void Refresh(TimeZoneInfo displayZone)
    {
        TimeText = RecordFormat.TimeText(Record.Time, displayZone);
        LevelText = RecordFormat.LevelText(Record.Level);
    }
}

/// <summary>One choice of a records filter, as the picker shows it.</summary>
public abstract partial class RecordFilterOption : ObservableObject
{
    private readonly Func<string> _label;

    protected RecordFilterOption(Func<string> label)
    {
        _label = label;
        _text = label();
    }

    [ObservableProperty]
    private string _text;

    /// <summary>Re-reads the words in the current language.</summary>
    public void Refresh() => Text = _label();
}

/// <summary>A filter choice and its value, null for all.</summary>
public sealed class RecordFilterOption<T> : RecordFilterOption
{
    public RecordFilterOption(T? value, Func<string> label)
        : base(label) => Value = value;

    public T? Value { get; }
}

/// <summary>A labelled value in the record detail.</summary>
public sealed record RecordField(string Label, string Value);

/// <summary>The selected record whole: its message, level, every column, and the fields no column shows, as JSON.</summary>
public sealed class RecordDetailViewModel
{
    public RecordDetailViewModel(RecordDetail record, string currentSession, TimeZoneInfo displayZone)
    {
        Record = record;
        Title = record.Message;
        LevelText = RecordFormat.LevelText(record.Level);
        var fields = new List<RecordField>
        {
            new(I18n.Localizer.T("records.time"), RecordFormat.TimeText(record.Time, displayZone, milliseconds: true)),
            new(I18n.Localizer.T("records.level"), LevelText),
            new(I18n.Localizer.T("records.launch"), RecordFormat.LaunchText(record.Session, currentSession, displayZone)),
        };
        if (record.NoteId is { } noteId)
        {
            fields.Add(new RecordField(I18n.Localizer.T("records.note"), noteId));
        }

        Fields = fields;
        DetailsLabel = I18n.Localizer.T("records.details");
        Details = RecordFormat.DetailsText(record.Fields, record.NoteId);
    }

    public RecordDetail Record { get; }

    public string Title { get; }

    public string LevelText { get; }

    public bool IsError => Record.Level == "error";

    public bool IsWarning => Record.Level == "warn";

    public IReadOnlyList<RecordField> Fields { get; }

    public string DetailsLabel { get; }

    /// <summary>The fields nothing else in the detail shows, or null when there are none.</summary>
    public string? Details { get; }

    public bool HasDetails => Details is not null;
}
