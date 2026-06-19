using System.Text;
using DayNote.Core.Models;
using DayNote.Core.Text;
using DayNote.Core.Time;

namespace DayNote.Core.Toml;

/// <summary>
/// Emits a notebook as canonical <c>.daynote</c> TOML text. This writer — not Tomlyn's serializer
/// — owns the output shape so files are deterministic and diffs stay minimal:
///
/// <list type="bullet">
/// <item>Keys are always emitted in canonical order, independent of in-memory construction order.</item>
/// <item>Timestamps are quoted ISO-8601 UTC strings with millisecond precision, not TOML datetimes.</item>
/// <item>Bodies are stored as multiline literal strings (triple single quotes), preserving all
/// whitespace verbatim, after <see cref="BodyCleanup"/> normalization.</item>
/// <item>The one exception to verbatim storage: a body containing the sequence <c>'''</c> (which
/// would terminate a literal string) is emitted as an escaped basic multiline string instead.</item>
/// </list>
///
/// Output is UTF-8 (written without a BOM by the storage layer) with LF line endings and a trailing newline.
/// </summary>
public static class NotebookTomlWriter
{
    private const string LiteralDelimiter = "'''";

    public static string Write(Notebook notebook)
    {
        var builder = new StringBuilder();

        AppendBasic(builder, "id", notebook.Id);
        AppendBasic(builder, "title", TextCleanup.SingleLine(notebook.Title));
        AppendTimestamp(builder, "created", notebook.Created);
        AppendTimestamp(builder, "modified", notebook.Modified);

        foreach (var note in notebook.Notes)
        {
            builder.Append('\n');
            builder.Append("[[note]]\n");
            AppendBasic(builder, "id", note.Id);
            AppendBasic(builder, "title", TextCleanup.SingleLine(note.Title));
            AppendTimestamp(builder, "created", note.Created);
            AppendTimestamp(builder, "modified", note.Modified);
            AppendAttachments(builder, note.Attachments);
            AppendBody(builder, note.Body);
        }

        return builder.ToString();
    }

    private static void AppendBasic(StringBuilder builder, string key, string value)
    {
        builder.Append(key).Append(" = \"").Append(EscapeBasicString(value)).Append("\"\n");
    }

    private static void AppendTimestamp(StringBuilder builder, string key, DateTimeOffset value)
    {
        builder.Append(key).Append(" = \"").Append(DayNoteTime.ToIso(value)).Append("\"\n");
    }

    private static void AppendAttachments(StringBuilder builder, IReadOnlyList<string> attachments)
    {
        builder.Append("attachments = [");
        for (var i = 0; i < attachments.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append('"').Append(EscapeBasicString(attachments[i])).Append('"');
        }

        builder.Append("]\n");
    }

    private static void AppendBody(StringBuilder builder, string body)
    {
        var clean = BodyCleanup.Normalize(body);

        if (clean.Length == 0)
        {
            builder.Append("body = ''\n");
            return;
        }

        if (clean.Contains(LiteralDelimiter, StringComparison.Ordinal))
        {
            // The body would prematurely close a literal multiline string; fall back to an escaped
            // basic multiline string for this one field.
            builder.Append("body = \"\"\"\n").Append(EscapeBasicMultiline(clean)).Append("\n\"\"\"\n");
            return;
        }

        builder.Append("body = '''\n").Append(clean).Append("\n'''\n");
    }

    private static string EscapeBasicString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    if (c < ' ' || c == (char)0x7F)
                    {
                        builder.Append("\\u").Append(((int)c).ToString("X4"));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static string EscapeBasicMultiline(string value)
    {
        // BodyCleanup has already removed control characters other than tab and newline, which are
        // legal in a basic multiline string. Escaping the backslash and every double-quote is
        // therefore sufficient, and escaping every quote also prevents a """ run from closing the
        // string early.
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
