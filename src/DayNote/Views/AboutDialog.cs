using Avalonia.Controls.Documents;
using Shapes = Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DayNote.Logging;
using DayNote.ViewModels;

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
    private readonly Func<Uri, Task> _openUri;
    private readonly Border _linkResult;
    private readonly TextBlock _linkResultText;

    public AboutDialog(IAppLogger log, Func<Uri, Task>? openUri = null)
    {
        _log = log;
        _openUri = openUri ?? (uri => Launcher.LaunchUriAsync(uri));
        Width = 420;
        Title = "About DayNote";

        _linkResultText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = PaletteBrush.Resolve("TextPrimaryBrush", Brushes.White),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var closeResult = new Button
        {
            Name = "CloseAboutLinkResult",
            Content = CloseMark(),
            VerticalAlignment = VerticalAlignment.Top,
        };
        closeResult.Classes.Add("resultClose");
        ToolTip.SetTip(closeResult, "Close");
        AutomationProperties.SetName(closeResult, "Close link result");
        var resultGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
        };
        resultGrid.Children.Add(_linkResultText);
        Grid.SetColumn(closeResult, 1);
        resultGrid.Children.Add(closeResult);
        _linkResult = new Border
        {
            Name = "AboutLinkResult",
            IsVisible = false,
            Margin = new Thickness(0, 0, 0, 16),
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(6),
            Background = PaletteBrush.Resolve("AppBackgroundBrush", Brushes.Transparent),
            BorderBrush = PaletteBrush.Resolve("DangerBrush", Brushes.IndianRed),
            BorderThickness = new Thickness(1),
            Child = resultGrid,
        };
        AutomationProperties.SetLiveSetting(_linkResult, AutomationLiveSetting.Assertive);
        closeResult.Click += (_, _) => _linkResult.IsVisible = false;

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
                _linkResult,
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
        var button = new Button
        {
            Name = label.Replace(" ", string.Empty) + "LinkButton",
            Content = ExternalLinkLabel(label),
        };
        button.Classes.Add("utility");
        button.Click += async (_, _) =>
        {
            try
            {
                await _openUri(new Uri(url));
                _linkResult.IsVisible = false;
            }
            catch (Exception ex)
            {
                _log.Warn("Failed to open external link", new { url }, ex);
                var message = FailurePresentation.OpenExternalLink(ex);
                _linkResultText.Text = message;
                AutomationProperties.SetName(_linkResult, message);
                _linkResult.IsVisible = true;
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

    private static Shapes.Path CloseMark() => new()
    {
        Width = 10,
        Height = 10,
        Stroke = PaletteBrush.Resolve("DangerBrush", Brushes.IndianRed),
        StrokeThickness = 1.6,
        StrokeLineCap = PenLineCap.Round,
        Data = Geometry.Parse("M1,1 L9,9 M9,1 L1,9"),
    };

}
