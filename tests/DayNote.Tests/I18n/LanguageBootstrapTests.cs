using DayNote.I18n;
using Xunit;
using static DayNote.Views.ObjC;

namespace DayNote.Tests.I18n;

/// <summary>
/// The language is settled before the app is built, and on macOS handed to AppKit for this process.
/// </summary>
public sealed class LanguageBootstrapTests
{
    [MacOnlyFact]
    public void on_macOS_appkit_is_pointed_at_the_language_for_this_process_only()
    {
        var defaults = Send(Class("NSUserDefaults"), "standardUserDefaults");
        var argumentDomain = NSString("NSArgumentDomain");
        var previous = Send(defaults, "volatileDomainForName:", argumentDomain);
        try
        {
            LanguageBootstrap.AlignAppKit("ko");

            var languages = Send(defaults, "objectForKey:", NSString("AppleLanguages"));
            Assert.Equal(1UL, SendForUInt(languages, "count"));
            Assert.Equal("ko", String(SendWithIndex(languages, "objectAtIndex:", 0)));
        }
        finally
        {
            // The argument domain is process-wide; give the rest of the run the one it started with.
            if (previous == System.IntPtr.Zero)
                Send(defaults, "removeVolatileDomainForName:", argumentDomain);
            else
                Send(defaults, "setVolatileDomain:forName:", previous, argumentDomain);
        }
    }
}
