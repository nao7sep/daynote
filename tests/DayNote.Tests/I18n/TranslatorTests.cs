using System.Globalization;
using DayNote.I18n;
using Xunit;

namespace DayNote.Tests.I18n;

/// <summary>
/// The translator and the pieces it stands on: how a preference resolves, which plural form a count
/// takes, how held messages join, and what happens when a key or a value is missing.
/// </summary>
public class TranslatorTests
{
    private static readonly Translator InEnglish = new("en", CultureInfo.GetCultureInfo("en"));

    [Theory]
    [InlineData(null, Languages.System)]
    [InlineData("", Languages.System)]
    [InlineData("   ", Languages.System)]
    [InlineData("klingon", Languages.System)]
    [InlineData("SYSTEM", Languages.System)]
    [InlineData("ja", "ja")]
    [InlineData("pt-br", "pt-BR")]
    [InlineData(" zh-Hans ", "zh-Hans")]
    public void a_saved_preference_is_read_forgivingly(string? saved, string expected) =>
        Assert.Equal(expected, Languages.NormalizePreference(saved));

    [Theory]
    [InlineData("ja-JP", "ja")]
    [InlineData("zh-Hant-TW", "zh-Hans")]
    [InlineData("zh-HK", "zh-Hans")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("es-419", "es")]
    [InlineData("en_US", "en")]
    [InlineData("nl-NL", "en")]
    public void a_computer_language_resolves_to_the_variety_the_app_ships(string computer, string expected) =>
        Assert.Equal(expected, Languages.Match([computer]));

    [Fact]
    public void system_takes_the_first_language_in_the_set()
    {
        Assert.Equal("de", Languages.Match(["nl-NL", "de-AT", "ja"]));
        Assert.Equal("en", Languages.Match(["nl-NL", "pl-PL"]));
        Assert.Equal("en", Languages.Match([]));
    }

    [Theory]
    // Japanese, Korean and Chinese do not change the noun with the number.
    [InlineData("ja", 1, Plural.Other)]
    [InlineData("zh-Hans", 5, Plural.Other)]
    // English and German split at one.
    [InlineData("en", 1, Plural.One)]
    [InlineData("en", 0, Plural.Other)]
    [InlineData("de", 2, Plural.Other)]
    // French and Brazilian Portuguese count zero with the singular.
    [InlineData("fr", 0, Plural.One)]
    [InlineData("pt-BR", 1, Plural.One)]
    [InlineData("es", 0, Plural.Other)]
    // The Romance "many" form is for round millions.
    [InlineData("it", 1_000_000, Plural.Many)]
    [InlineData("fr", 2_000_000, Plural.Many)]
    [InlineData("it", 1_000_001, Plural.Other)]
    // Russian changes at one, at two to four, and again above.
    [InlineData("ru", 1, Plural.One)]
    [InlineData("ru", 21, Plural.One)]
    [InlineData("ru", 11, Plural.Many)]
    [InlineData("ru", 3, Plural.Few)]
    [InlineData("ru", 24, Plural.Few)]
    [InlineData("ru", 14, Plural.Many)]
    [InlineData("ru", 5, Plural.Many)]
    [InlineData("ru", 0, Plural.Many)]
    public void a_count_takes_its_languages_plural_form(string tag, long count, string expected) =>
        Assert.Equal(expected, Plural.CategoryFor(tag, count));

    [Fact]
    public void every_language_declares_the_forms_it_selects()
    {
        // Whatever the count, the form chosen must be one the catalogue is required to carry.
        foreach (var tag in Languages.Tags)
        {
            var categories = Plural.CategoriesOf(tag);
            for (long count = 0; count <= 200; count++)
                Assert.Contains(Plural.CategoryFor(tag, count), categories);
            foreach (var count in new long[] { 1_000_000, 2_000_000, 1_000_001, 999_999 })
                Assert.Contains(Plural.CategoryFor(tag, count), categories);
        }
    }

    [Fact]
    public void a_missing_key_shows_as_its_key() =>
        Assert.Equal("nothing.here", InEnglish.T("nothing.here"));

    [Fact]
    public void a_language_missing_a_key_falls_back_to_english()
    {
        // Every catalogue has every key today, and the gate keeps it that way; this is what a reader
        // would get if one ever slipped through.
        var japanese = new Translator("ja", CultureInfo.GetCultureInfo("ja"));
        Assert.Equal(InEnglish.T("nothing.here"), japanese.T("nothing.here"));
    }

    [Fact]
    public void a_count_takes_its_plural_form_and_is_formatted_for_the_reader()
    {
        Assert.Equal("1 note", InEnglish.T("binder.noteCount", ("count", 1)));
        Assert.Equal("1,234 notes", InEnglish.T("binder.noteCount", ("count", 1234)));

        var german = new Translator("de", CultureInfo.GetCultureInfo("de"));
        Assert.Contains("1.234", german.T("binder.noteCount", ("count", 1234)));
    }

    [Fact]
    public void an_unfilled_placeholder_is_left_as_it_is()
    {
        // Better a visible {style} than a sentence with a hole in it.
        Assert.Contains("{style}", InEnglish.T("textStyle.applied"));
    }

    [Fact]
    public void sentences_join_through_their_entry_and_each_keeps_its_own_number()
    {
        // An attachment result reports several outcomes. Each is its own sentence with its own plural
        // form, and the language's join entry decides how they sit together, so "1 attachment" and
        // "3 items" are never forced to agree with one number.
        var line = Message.Join("attachments.join",
        [
            Message.Of("attachments.added", ("count", 1)),
            Message.Of("attachments.duplicates", ("names", "a.png")),
            Message.Of("attachments.unreadable", ("count", 3)),
        ]);

        Assert.Equal(
            "Added 1 attachment. Already attached: a.png. 3 items are not readable local files.",
            InEnglish.Of(line));
    }

    [Fact]
    public void a_single_message_joins_to_itself()
    {
        var only = Message.Of("attachments.added", ("count", 2));
        Assert.Same(only, Message.Join("attachments.join", [only]));
    }

    [Fact]
    public void a_value_can_be_another_message_in_the_same_language()
    {
        var names = Message.Of("files.join", ("first", "a.png"), ("rest", Message.Of("files.join", ("first", "b.png"), ("rest", "c.png"))));
        var sentence = Message.Of("attachments.duplicates", ("names", Message.Of("files.andMore", ("names", names), ("count", 2))));

        Assert.Equal("Already attached: a.png, b.png, c.png, and 2 more.", InEnglish.Of(sentence));
    }

    [Fact]
    public void the_formatting_culture_follows_the_computer_when_it_speaks_the_language()
    {
        // A German computer set to German keeps its own regional format.
        var computer = CultureInfo.GetCultureInfo("de-AT");
        Assert.Equal(computer, Languages.FormattingCulture("de", computer));

        // A German-speaking app on a Japanese computer uses German's own format, not Japan's.
        Assert.Equal(
            CultureInfo.GetCultureInfo("de"),
            Languages.FormattingCulture("de", CultureInfo.GetCultureInfo("ja-JP")));
    }
}
