using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DayNote.Core.Configuration;
using DayNote.I18n;
using DayNote.Tests.Storage;
using DayNote.ViewModels;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.I18n;

/// <summary>
/// The rendered-key gate (localization conventions): no catalogue key ever reaches the screen.
///
/// A key is a string like any other, so neither the compiler nor the source scan notices one handed
/// to a control without the translator — a whole group heading showed as <c>tasks.overdue</c> in
/// another app before this check existed. These open each surface and read what is actually drawn.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public class RenderedKeyTests : WindowTest
{
    [AvaloniaFact]
    public async Task the_main_window_shows_no_key()
    {
        using var populated = PopulatedMainWindow.Create();
        var window = Show(populated.Window);
        await populated.FillAsync();

        // Rows, the editor, the attachments, the strip, a result and the status bar all hold words.
        var vm = populated.ViewModel;
        Assert.NotEmpty(vm.Notes);
        Assert.NotEmpty(vm.Attachments);
        Assert.NotNull(vm.AttachmentResult);
        Assert.NotEmpty(vm.Results);
        Assert.NotEmpty(vm.TextStyleStatusText);
        AssertNoKeys(window);

        // The menu's items are not on screen until it opens, so the walk above cannot see them.
        var keys = Keys();
        foreach (var name in new[] { "SettingsMenuItem", "ShortcutsMenuItem", "AboutMenuItem" })
        {
            var header = Assert.IsType<string>(window.FindControl<MenuItem>(name)!.Header);
            Assert.DoesNotContain(header, keys);
        }
    }

    [AvaloniaFact]
    public void the_empty_main_window_shows_no_key()
    {
        using var empty = PopulatedMainWindow.Empty();
        var window = Show(empty.Window);

        AssertNoKeys(window);
    }

    [AvaloniaFact]
    public void the_about_dialog_shows_no_key()
    {
        var dialog = Show(new AboutDialog(new NullLogger()));

        AssertNoKeys(dialog);
    }

    [AvaloniaFact]
    public void the_shortcuts_dialog_shows_no_key()
    {
        var window = Show(new Window());
        var dialog = Show(new ShortcutsDialog(ShortcutCatalog.Build(window)));

        AssertNoKeys(dialog);
    }

    [AvaloniaFact]
    public void the_settings_dialog_shows_no_key()
    {
        // With a failed save's sentence on screen.
        var dialog = Show(new SettingsDialog(new AppConfig { UiFontFamily = "Menlo" }, _ => false));
        var save = dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ok"));
        dialog.GetVisualDescendants().OfType<TextBox>().Single(box => box.Text == "Menlo").Text = "Inter";
        Dispatcher.UIThread.RunJobs();
        save.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), text => text.IsVisible && text.Text == Localizer.T("failure.settingsSave"));
        AssertNoKeys(dialog);
    }

    [AvaloniaFact]
    public void the_notices_and_confirmations_show_no_key()
    {
        AssertNoKeys(Show(MessageDialog.CreateStartupFailure(
            Message.Of("startup.failedTitle"), FailurePresentation.StartupStorage())));
        AssertNoKeys(Show(new MessageDialog(
            Message.Of("note.deleteTitle"),
            Message.Join("note.deleteJoin", [Message.Of("note.deleteNamed", ("title", "Plans")), Message.Of("note.deleteAttachments", ("count", 3))]),
            [new DialogButton("common.cancel", "cancel"), new DialogButton("common.delete", "confirm", DialogButtonKind.Destructive)])));
        AssertNoKeys(Show(new MessageDialog(
            Message.Of("quarantine.binderListTitle"), FailurePresentation.RecoveredData(binderListWasReset: true),
            [new DialogButton("common.ok", "ok", DialogButtonKind.Primary)])));
    }

    private static void AssertNoKeys(Visual root)
    {
        var keys = Keys();
        var found = new List<string>();

        foreach (var text in root.GetVisualDescendants().OfType<TextBlock>())
        {
            if (text.Text is { } drawn && keys.Contains(drawn.Trim()))
                found.Add($"text: {drawn}");
        }

        foreach (var control in root.GetVisualDescendants().OfType<Control>())
        {
            if (control is ContentControl { Content: string content } && keys.Contains(content.Trim()))
                found.Add($"content: {content}");
            if (control is Avalonia.Controls.Primitives.HeaderedContentControl { Header: string header } && keys.Contains(header.Trim()))
                found.Add($"header: {header}");
            if (control is TextBox { PlaceholderText: { } placeholder } && keys.Contains(placeholder.Trim()))
                found.Add($"placeholder: {placeholder}");
            if (AutomationProperties.GetName(control) is { } name && keys.Contains(name.Trim()))
                found.Add($"automation name: {name}");
            if (AutomationProperties.GetHelpText(control) is { } help && keys.Contains(help.Trim()))
                found.Add($"help text: {help}");
            if (ToolTip.GetTip(control) is string tip && keys.Contains(tip.Trim()))
                found.Add($"tooltip: {tip}");
        }

        if (root is Window window && window.Title is { } title && keys.Contains(title.Trim()))
            found.Add($"title: {title}");

        Assert.Empty(found);
    }

    private static HashSet<string> Keys()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(CatalogueTests.LocalesDirectory(), "en.json")));
        return document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(System.StringComparer.Ordinal);
    }

    private sealed class NullLogger : DayNote.Logging.IAppLogger
    {
        public void Debug(string message, object? data = null, System.Exception? error = null) { }
        public void Info(string message, object? data = null, System.Exception? error = null) { }
        public void Warn(string message, object? data = null, System.Exception? error = null) { }
        public void Error(string message, object? data = null, System.Exception? error = null) { }
    }
}
