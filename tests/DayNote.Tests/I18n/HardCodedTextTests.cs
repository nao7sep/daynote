using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace DayNote.Tests.I18n;

/// <summary>
/// The hard-coded-text gate (localization conventions): no sentence a reader sees is written in the
/// source, and no key reaches the source that the catalogue does not have.
///
/// This reads the shipped source as text, which is what a test of a file rather than a module does
/// (tests-folder conventions). It is deliberately narrow: it looks at the properties a person reads
/// or hears — text, a button's content, a menu's header, a window title, a placeholder, a tooltip and
/// the two automation properties — and leaves everything else alone.
/// </summary>
public class HardCodedTextTests
{
    /// <summary>
    /// Literals allowed in a text position: the app's own name, which reads the same in every language.
    /// Punctuation and separators pass on their own, having no letters.
    /// </summary>
    private static readonly string[] AllowedLiterals = ["DayNote"];

    private static readonly string[] TextProperties =
        ["Text", "Content", "Header", "Title", "PlaceholderText", "Watermark"];

    [Fact]
    public void no_markup_holds_a_sentence_of_its_own()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(SourceDirectory(), "*.axaml", SearchOption.AllDirectories))
            offenders.AddRange(Offenders(MarkupTexts(File.ReadAllText(file)), Path.GetFileName(file)));

        Assert.Empty(offenders);
    }

    [Fact]
    public void no_code_assigns_a_sentence_to_a_property_a_reader_sees()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(SourceDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;
            offenders.AddRange(Offenders(CodeTexts(File.ReadAllText(file)), Path.GetFileName(file)));
        }

        Assert.Empty(offenders);
    }

    // The scans above prove nothing unless they can fail: each finds a sentence planted in its own
    // kind of source, and passes the forms it is meant to let through.
    [Fact]
    public void the_scans_find_a_planted_sentence()
    {
        Assert.Single(Offenders(MarkupTexts("""<TextBlock Text="Save the note"/>"""), "planted"));
        Assert.Single(Offenders(MarkupTexts("""<Button ToolTip.Tip="Close this"/>"""), "planted"));
        Assert.Single(Offenders(CodeTexts("""block.Text = $"Saved {count} notes";"""), "planted"));
        Assert.Empty(Offenders(MarkupTexts("""<TextBlock Text="{Binding Title}" Title="DayNote"/>"""), "planted"));
    }

    // An allowance nothing in the source needs any more would let a sentence through unseen.
    [Fact]
    public void every_allowed_literal_is_still_used()
    {
        var texts = Directory.GetFiles(SourceDirectory(), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(file => MarkupTexts(File.ReadAllText(file)))
            .Concat(Directory.GetFiles(SourceDirectory(), "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .SelectMany(file => CodeTexts(File.ReadAllText(file))))
            .Select(text => text.Value.Trim())
            .ToHashSet(StringComparer.Ordinal);

        var unused = AllowedLiterals.Where(literal => literal.Length > 0 && !texts.Contains(literal)).ToArray();
        Assert.True(unused.Length == 0, "Allowed literals no source uses: " + string.Join(", ", unused));
    }

    private static IEnumerable<(string Property, string Value)> MarkupTexts(string markup)
    {
        foreach (var property in TextProperties)
        {
            foreach (Match match in Regex.Matches(markup, $@"(?<![\w.]){property}\s*=\s*""([^""]*)"""))
                yield return (property, match.Groups[1].Value);
        }

        foreach (Match match in Regex.Matches(markup, @"ToolTip\.Tip\s*=\s*""([^""]*)"""))
            yield return ("ToolTip.Tip", match.Groups[1].Value);

        foreach (Match match in Regex.Matches(markup, @"AutomationProperties\.(Name|HelpText)\s*=\s*""([^""]*)"""))
            yield return ("AutomationProperties." + match.Groups[1].Value, match.Groups[2].Value);
    }

    // Text = "…", Content = "…", Title = "…" and their kin, including an interpolated string.
    private static IEnumerable<(string Property, string Value)> CodeTexts(string code)
    {
        foreach (var property in TextProperties)
        {
            // Both an initializer (Text = "…") and an assignment to a member (block.Text = "…").
            foreach (Match match in Regex.Matches(code, $@"(?<!\w){property}\s*=\s*\$?""([^""]*)"""))
                yield return (property, match.Groups[1].Value);
        }

        foreach (Match match in Regex.Matches(code, @"AutomationProperties\.SetName\([^,]+,\s*\$?""([^""]*)"""))
            yield return ("AutomationProperties.SetName", match.Groups[1].Value);

        foreach (Match match in Regex.Matches(code, @"ToolTip\.SetTip\([^,]+,\s*\$?""([^""]*)"""))
            yield return ("ToolTip.SetTip", match.Groups[1].Value);
    }

    private static IEnumerable<string> Offenders(IEnumerable<(string Property, string Value)> texts, string file) =>
        texts.Where(text => IsSentence(text.Value)).Select(text => $"{file}: {text.Property}=\"{text.Value}\"");

    [Fact]
    public void every_key_the_source_names_is_in_the_catalogue()
    {
        // Any string in the source shaped like a key under one of the catalogue's own namespaces must
        // be a key. Keys reach the screen through more than the translator's own calls — the shortcut
        // catalogue's helpers, the column headers, the row states — so the check reads every literal
        // rather than a list of call shapes, and a mistyped key fails wherever it is written.
        var english = Keys();
        var namespaces = english.Select(key => key[..key.IndexOf('.')]).ToHashSet(StringComparer.Ordinal);
        var shaped = new Regex(@"""([a-z][a-zA-Z]*)\.([a-zA-Z][a-zA-Z.]*)""");
        var missing = new List<string>();

        foreach (var file in SourceFiles())
        {
            foreach (Match match in shaped.Matches(File.ReadAllText(file)))
            {
                var key = match.Groups[1].Value + "." + match.Groups[2].Value;
                if (namespaces.Contains(match.Groups[1].Value) && !english.Contains(key))
                    missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void every_dialog_button_is_labelled_by_a_key()
    {
        // A footer button's label goes through the translator, which shows a missing key as the key:
        // a label written as a word ("Close") would reach the screen as that word in every language,
        // and it has no dot, so the key-shaped check above would never see it.
        var english = Keys();
        var labels = SourceFiles()
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"new DialogButton\(\s*""([^""]*)""")
                .Select(match => $"{Path.GetFileName(file)}: {match.Groups[1].Value}"))
            .Where(label => !english.Contains(label[(label.IndexOf(": ", StringComparison.Ordinal) + 2)..]))
            .ToArray();

        Assert.Empty(labels);
    }

    [Fact]
    public void every_key_in_the_catalogue_is_used()
    {
        // A key nothing names is dead weight every translator still has to translate.
        var text = string.Join("\n", SourceFiles().Select(File.ReadAllText));

        var unused = Keys().Where(key => !text.Contains($"\"{key}\"", StringComparison.Ordinal)).ToArray();
        Assert.Empty(unused);
    }

    private static bool IsSentence(string value)
    {
        var text = value.Trim();
        if (text.Length == 0 || AllowedLiterals.Contains(text))
            return false;
        // A binding, a resource, a static reference or a number is not a sentence.
        if (text.StartsWith('{') || text.StartsWith('/') || !text.Any(char.IsLetter))
            return false;
        // A single lower-case token is a name or a value, not a sentence a reader reads.
        return text.Any(char.IsWhiteSpace) || !char.IsLower(text[0]) || text.Contains('’');
    }

    private static HashSet<string> Keys()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(CatalogueTests.LocalesDirectory(), "en.json")));
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
    }

    private static string SourceDirectory() => Path.Combine(CatalogueTests.RepoRoot(), "src", "DayNote");

    private static IEnumerable<string> SourceFiles() =>
        Directory.GetFiles(SourceDirectory(), "*.*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file) is ".cs" or ".axaml")
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
}
