using System.Collections.Generic;
using System.Linq;

namespace DayNote.I18n;

/// <summary>
/// A line in the Settings language list: the value that is saved, and the name shown for it.
///
/// Every language is named in its own words, so a reader finds theirs whatever language is showing;
/// only System is translated, because only System is a sentence about the computer rather than a
/// language's own name.
/// </summary>
public sealed record LanguageOption(string Value, string Name)
{
    /// <summary>
    /// System first, with the language it resolves to on this computer now named in the interface
    /// language, such as "System (Japanese)" (localization-conventions, Translation), then each
    /// language by its own name.
    /// </summary>
    internal static IReadOnlyList<LanguageOption> All(IReadOnlyList<string> computerLanguages) =>
    [
        new(Languages.System, Localizer.T("settings.languageSystem",
            ("language", Localizer.T(NameKey(Languages.Resolve(Languages.System, computerLanguages)))))),
        .. Languages.All.Select(language => new LanguageOption(language.Tag, language.Name)),
    ];

    /// <summary>The catalogue key naming a language in the interface language, written out so the
    /// catalogue checks can see each one.</summary>
    private static string NameKey(string tag) => tag switch
    {
        "de" => "settings.languageName.de",
        "es" => "settings.languageName.es",
        "fr" => "settings.languageName.fr",
        "it" => "settings.languageName.it",
        "pt-BR" => "settings.languageName.pt-BR",
        "ru" => "settings.languageName.ru",
        "zh-Hans" => "settings.languageName.zh-Hans",
        "ja" => "settings.languageName.ja",
        "ko" => "settings.languageName.ko",
        _ => "settings.languageName.en",
    };

    /// <summary>The line for a saved preference, falling back to System.</summary>
    internal static LanguageOption For(string? preference, IReadOnlyList<LanguageOption> options)
    {
        var normalized = Languages.NormalizePreference(preference);
        return options.FirstOrDefault(option => option.Value == normalized) ?? options[0];
    }
}
