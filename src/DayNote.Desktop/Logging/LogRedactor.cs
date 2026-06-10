using System.Text.Json.Nodes;

namespace DayNote.Desktop.Logging;

/// <summary>
/// The mandatory, non-destructive redaction backstop. It runs on the structured log object (already
/// a <see cref="JsonNode"/> tree) <em>before</em> serialization and replaces the <em>value</em> of
/// any field whose name exactly matches a denied key — case-insensitively, whole-name only, never a
/// substring (so <c>token</c> never matches <c>tokenCount</c> or <c>broken</c>) — with
/// <see cref="Marker"/>.
/// </summary>
/// <remarks>
/// The redactor recurses through nested objects and arrays, never scans or rewrites string values,
/// and never edits the <c>message</c> (a non-denied field). It is a pure, total, in-place edit that
/// cannot throw, cannot drop fields, and leaves every non-matched field byte-identical — its only
/// effect is to overwrite matched values. The denied-key set is supplied by the logger and compared
/// with an ordinal-ignore-case <see cref="ISet{T}"/>, which is what makes the match case-insensitive.
/// </remarks>
public static class LogRedactor
{
    /// <summary>The fixed value written in place of a redacted field.</summary>
    public const string Marker = "[redacted]";

    /// <summary>
    /// Redacts <paramref name="node"/> in place: any object field named in <paramref name="deniedKeys"/>
    /// has its value replaced with <see cref="Marker"/>; everything else is left untouched.
    /// </summary>
    public static void Redact(JsonNode? node, IReadOnlySet<string> deniedKeys)
    {
        switch (node)
        {
            case JsonObject obj:
                // Snapshot the keys first: a matched value is reassigned while iterating.
                foreach (var key in obj.Select(pair => pair.Key).ToArray())
                {
                    if (deniedKeys.Contains(key))
                    {
                        obj[key] = Marker;
                    }
                    else
                    {
                        Redact(obj[key], deniedKeys);
                    }
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    Redact(item, deniedKeys);
                }

                break;
        }
    }
}
