using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

/// <summary>
/// The macOS menu bar: an app menu under DayNote's own name, and an Edit menu on every window, which
/// is where macOS attaches Emoji &amp; Symbols, Start Dictation, and AutoFill.
/// </summary>
public sealed class MacMenusTests
{
    [Fact]
    public void TheAppMenuNamesDayNoteAndOffersSettings()
    {
        var headers = XDocument.Load(Path.Combine(RepoRoot(), "src", "DayNote", "App.axaml"))
            .Descendants()
            .Where(element => element.Name.LocalName == "NativeMenuItem")
            .Select(element => (string?)element.Attribute("Header"))
            .ToList();
        Assert.Equal(new[] { "About DayNote", "Settings…" }, headers);
    }

    [AvaloniaFact]
    public void TheMainWindowHasEditAndWindowMenus()
    {
        var menu = NativeMenu.GetMenu(new MainWindow())!;
        Assert.Equal(new[] { "Edit", "Window" }, TopLevelHeaders(menu));
        Assert.Equal(
            new[] { "Undo", "Redo", "Cut", "Copy", "Paste", "Select All" },
            Items(menu, "Edit").Select(item => item.Header));
        Assert.Equal(new KeyGesture(Key.V, KeyModifiers.Meta), Items(menu, "Edit").Single(item => item.Header == "Paste").Gesture);
        Assert.Equal(new[] { "Minimize", "Zoom" }, Items(menu, "Window").Select(item => item.Header));
    }

    [AvaloniaFact]
    public void EveryDialogKeepsTheEditMenu()
    {
        var menu = NativeMenu.GetMenu(new MessageDialog("Title", "Message", [new DialogButton("OK", "ok")]))!;
        Assert.Equal(new[] { "Edit" }, TopLevelHeaders(menu));
    }

    [AvaloniaFact]
    public void EditCommandsActOnTheActiveWindowsFocusedField()
    {
        var field = new TextBox { Text = "journal" };
        var window = new Window { Content = field };
        try
        {
            window.Show();
            field.Focus();
            Dispatcher.UIThread.RunJobs();

            MacMenus.OnFocusedField(window, box => box.SelectAll());

            Assert.Equal("journal", field.SelectedText);
        }
        finally
        {
            window.Close();
        }
    }

    private static string?[] TopLevelHeaders(NativeMenu menu) =>
        menu.Items.OfType<NativeMenuItem>().Select(item => item.Header).ToArray();

    private static NativeMenuItem[] Items(NativeMenu menu, string header) =>
        menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == header).Menu!.Items
            .OfType<NativeMenuItem>().Where(item => item is not NativeMenuItemSeparator).ToArray();

    private static string RepoRoot([CallerFilePath] string callerPath = "") =>
        // This file: <repo>/tests/DayNote.Tests/Views/MacMenusTests.cs
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, "..", "..", ".."));
}
