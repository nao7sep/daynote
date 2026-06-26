using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DayNote.Logging;

namespace DayNote.Views;

/// <summary>
/// The About dialog: app name and version, a one-line description, links to the project on GitHub,
/// and the license line. The window Title ("About DayNote") becomes the dialog's header via
/// <see cref="DialogBase"/>; the content carries the version, description, links, and copyright.
/// </summary>
public sealed class AboutDialog : DialogBase
{
    private const string GitHubUrl = "https://github.com/nao7sep/daynote";

    private readonly IAppLogger _log;

    public AboutDialog(IAppLogger log)
    {
        _log = log;
        Width = 420;
        Title = "About DayNote";

        var panel = new StackPanel
        {
            Spacing = 0,
            Children =
            {
                new TextBlock
                {
                    Text = $"{AppInfo.Name} {AppInfo.Version}",
                    FontSize = 13,
                    Foreground = Secondary,
                    Margin = new Thickness(0, 0, 0, 12),
                },
                new TextBlock
                {
                    Text = "A plain-text notes desktop application: binders containing notes. Successor to quickdeck.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    Margin = new Thickness(0, 0, 0, 16),
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Margin = new Thickness(0, 0, 0, 16),
                    Children = { LinkButton("GitHub ↗", GitHubUrl), LinkButton("Report Issue ↗", GitHubUrl + "/issues") },
                },
                new TextBlock
                {
                    Text = "© 2026 Yoshinao Inoguchi · MIT License",
                    FontSize = 12,
                    Foreground = Secondary,
                },
            },
        };

        SetContent(panel);
        var buttons = SetButtons([new DialogButton("Close", "ok", DialogButtonKind.Primary)]);
        SetInitialFocus(buttons["ok"]);
    }

    private Button LinkButton(string label, string url)
    {
        var button = new Button { Content = label };
        button.Classes.Add("utility");
        button.Click += async (_, _) =>
        {
            try
            {
                await Launcher.LaunchUriAsync(new Uri(url));
            }
            catch (Exception ex)
            {
                // Best effort: failing to open a browser must not crash the About dialog — but the
                // boundary failure is logged (warn) rather than silently swallowed.
                _log.Warn("Failed to open external link", new { url }, ex);
            }
        };

        return button;
    }

    private static IBrush Secondary => PaletteBrush.Resolve("TextSecondaryBrush", Brushes.Gray);
}
