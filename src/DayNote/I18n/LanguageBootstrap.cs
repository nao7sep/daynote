using DayNote.Core.Storage;
using static DayNote.Views.ObjC;

namespace DayNote.I18n;

/// <summary>
/// Settles the interface language before anything draws.
///
/// Two things have to happen this early. The first window's very first frame must already be in the
/// reader's language, and on macOS the items AppKit contributes itself — Services, Emoji &amp;
/// Symbols, Start Dictation — are drawn in the language it settles on when the application object is
/// created, which Avalonia does as it starts. So the language is resolved and handed to AppKit before
/// the app is built, from the saved preference in the shared validated config store.
///
/// A failed read chooses System for the startup-failure surface. The store retains that same failure
/// for the app's startup gate, so language lookup cannot hide a failed required read.
/// </summary>
internal static class LanguageBootstrap
{
    /// <summary>
    /// Resolves the language, tells the app, and points AppKit at it. <paramref name="store"/> is the
    /// shared config store, or null when the storage location itself could not be resolved. Returns the
    /// computer's own languages, which the app keeps so a later change resolves System the same way.
    /// </summary>
    internal static IReadOnlyList<string> Start(ConfigStore? store)
    {
        var computerLanguages = ComputerLanguages.Read();
        Localizer.Use(ReadPreference(store), computerLanguages);
        AlignAppKit(Localizer.Language);
        return computerLanguages;
    }

    internal static string ReadPreference(ConfigStore? store)
    {
        try
        {
            return store?.Load().Language ?? Languages.System;
        }
        catch (Exception)
        {
            // The same store rethrows this failure at the required startup gate; only language uses System.
            return Languages.System;
        }
    }

    /// <summary>
    /// Points AppKit at <paramref name="tag"/> by putting it in the argument domain of the user's
    /// defaults, which is volatile: it lives for this process only and writes nothing to the reader's
    /// settings. A language saved mid-session reaches AppKit's own items at the next launch, as the
    /// localization conventions say.
    /// </summary>
    internal static void AlignAppKit(string tag)
    {
        if (!OperatingSystem.IsMacOS())
            return;

        try
        {
            if (Class("NSUserDefaults") == IntPtr.Zero)
                return;

            var languages = Send(Class("NSArray"), "arrayWithObject:", NSString(tag));
            var domain = Send(Class("NSDictionary"), "dictionaryWithObject:forKey:",
                languages, NSString("AppleLanguages"));
            var defaults = Send(Class("NSUserDefaults"), "standardUserDefaults");
            Send(defaults, "setVolatileDomain:forName:", domain, NSString("NSArgumentDomain"));
        }
        catch (Exception)
        {
            // Without this the menu's own items follow the computer instead of the app: a mixed menu,
            // not a broken one, and never a reason to fail to start.
        }
    }
}
