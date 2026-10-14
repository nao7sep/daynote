using System.Globalization;
using DayNote.Core.Text;

namespace DayNote.I18n;

/// <summary>
/// A user's value embedded in a message, shortened as identifying text (interface-styling-conventions,
/// Labels): a name or title keeps its beginning, and a path keeps its end and loses its middle, because
/// its beginning is usually shared. Past the limit the value ends, or breaks, with one "…". Laying out an
/// enormous unbroken value also froze a wrapping text block, which this bound prevents. Only the shown
/// copy is shortened; the value itself stays whole where it is edited or stored.
/// </summary>
public static class QuotedValue
{
    internal const int Limit = 100;

    public static string Name(string value)
    {
        var (shown, truncated) = TextCleanup.Truncate(value, Limit);
        return truncated ? shown + "…" : value;
    }

    public static string Path(string value)
    {
        var graphemes = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            graphemes.Add(enumerator.GetTextElement());
        }

        if (graphemes.Count <= Limit)
        {
            return value;
        }

        // A short start keeps the root recognisable; the rest of the budget goes to the end.
        const int start = 20;
        return string.Concat(graphemes.Take(start)) + "…" + string.Concat(graphemes.Skip(graphemes.Count - (Limit - start)));
    }
}
