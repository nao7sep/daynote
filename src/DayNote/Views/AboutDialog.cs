using Avalonia.Controls.Documents;
using Shapes = Avalonia.Controls.Shapes;
using Avalonia.Data;
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
                    Children = { LinkButton("GitHub", GitHubUrl), LinkButton("Report Issue", GitHubUrl + "/issues") },
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
        var button = new Button { Content = ExternalLinkLabel(label) };
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

    /// <summary>
    /// A button label with a trailing external-link mark drawn as a vector rather than the
    /// ↗ glyph, whose weight and size vary by font. The mark binds to the button's own
    /// foreground, so it follows theme and hover exactly as the text does.
    ///
    /// It rides INSIDE the text as an inline rather than beside it in a panel, so it is
    /// positioned against the text baseline — the one datum that holds whatever font the
    /// app is set to. Coordinates are written at the target pixel size rather than
    /// stretched, so the stroke keeps one weight, matching the app's XAML icons.
    /// </summary>
    private static Control ExternalLinkLabel(string text)
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        label.Inlines!.Add(new Run(text));
        label.Inlines!.Add(new InlineUIContainer(ExternalLinkMark())
        {
            BaselineAlignment = BaselineAlignment.Baseline,
        });
        return label;
    }

    private static Shapes.Path ExternalLinkMark()
    {
        var mark = new Shapes.Path
        {
            Width = 11,
            Height = 11,
            Margin = new Thickness(5, 0, 0, 0),
            StrokeThickness = 1.3,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            UseLayoutRounding = true,
            Data = Geometry.Parse("M7.8,6.1 V10.35 H0.65 V3.2 H5.0 M6.3,0.65 H10.35 V4.7 M10.35,0.65 L5.2,5.8"),
        };
        mark.Bind(
            Shapes.Shape.StrokeProperty,
            new Binding("Foreground") { RelativeSource = new RelativeSource { AncestorType = typeof(Button) } });
        return mark;
    }

}
