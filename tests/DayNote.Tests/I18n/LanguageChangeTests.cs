using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DayNote.Core.Configuration;
using DayNote.Core.Models;
using DayNote.Core.Time;
using DayNote.I18n;
using DayNote.Tests.Storage;
using DayNote.ViewModels;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.I18n;

/// <summary>
/// What happens when the language changes while the app is open.
///
/// This is the whole point of holding keys rather than words: a window that is already on screen
/// speaks the new language without being rebuilt, and nothing has to be restarted. A control assigned
/// once in a constructor — which is every dialog here — must follow too.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public class LanguageChangeTests : WindowTest
{
    [AvaloniaFact]
    public async Task the_main_window_follows_the_language_everywhere_it_shows_words()
    {
        using var populated = PopulatedMainWindow.Create();
        var window = Show(populated.Window);
        await populated.FillAsync();
        var vm = populated.ViewModel;

        // "New" also labels the notes pane's button; the binders pane comes first.
        var newBinder = window.GetVisualDescendants().OfType<Button>().First(button => Equals(button.Content, English.Of("binders.new")));
        var notesHeader = Text(window, English.Of("notes.pane"));
        var draftRow = vm.Notes.Single(row => row.Note.Status == NoteStatus.Draft);
        var result = vm.Results.First();
        var gone = vm.Attachments.Single(item => !item.Exists);
        var saveState = vm.SaveStateText;
        Assert.Equal(English.Of("status.draft"), draftRow.StatusLabel);

        using (Localizer.Speaking("ja"))
        {
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Localizer.T("binders.new"), newBinder.Content);
            Assert.Equal(Localizer.T("notes.pane"), notesHeader.Text);
            Assert.Equal(Localizer.T("status.draft"), draftRow.StatusLabel);
            Assert.Equal(DayNoteTime.ToDisplay(draftRow.Note.Created, DayNoteTime.DisplayZone(DayNoteTime.SystemZone), Localizer.Current.Culture), draftRow.Subtitle);
            Assert.Contains(vm.SaveStateText, new[] { "save.saved", "save.unsaved", "save.saving" }.Select(key => Localizer.T(key)));
            Assert.Equal(Localizer.Of(result.Message), result.Text);
            Assert.Equal(Localizer.Of(vm.AttachmentResult!.Message), vm.AttachmentResult.Text);
            Assert.Equal(Localizer.T("attachment.unavailable"), gone.DetailsText);
            Assert.Equal(Localizer.Of(gone.Result!.Message), gone.Result.Text);
            Assert.StartsWith(Localizer.T("meta.created", ("time", "")), vm.Editor.CreatedText);
            Assert.Contains(Localizer.T("counts.words", ("count", 4)), vm.Editor.WordsText);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == Localizer.T("status.expired"));
            Assert.NotEqual(English.Of("binders.new"), newBinder.Content);
            Assert.NotEqual(saveState, vm.SaveStateText);
        }

        // And back, so the change is a re-reading rather than a one-way overwrite.
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(English.Of("binders.new"), newBinder.Content);
        Assert.Equal(English.Of("notes.pane"), notesHeader.Text);
        Assert.Equal(English.Of("status.draft"), draftRow.StatusLabel);
        Assert.Equal(saveState, vm.SaveStateText);
    }

    [AvaloniaFact]
    public void a_closed_main_window_stops_listening()
    {
        using var empty = PopulatedMainWindow.Empty();
        var window = Show(empty.Window);
        var header = Text(window, English.Of("binders.pane"));
        var emptyState = empty.ViewModel.BindersEmptyStateText;
        window.Close();
        Dispatcher.UIThread.RunJobs();

        using (Localizer.Speaking("ru"))
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(English.Of("binders.pane"), header.Text);
            Assert.Equal(English.Of("binders.empty"), emptyState);
        }
    }

    [AvaloniaFact]
    public void a_dialog_built_in_code_follows_the_language()
    {
        var dialog = Show(new SettingsDialog(new AppConfig(), _ => true));

        var theme = dialog.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == English.Of("settings.theme"));
        var save = Button(dialog, English.Of("common.save"));

        using (Localizer.Speaking("ru"))
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Localizer.T("settings.theme"), theme.Text);
            Assert.Equal(Localizer.T("common.save"), save.Content);
            Assert.Equal(Localizer.T("settings.title"), dialog.Title);
        }
    }

    [AvaloniaFact]
    public void a_window_that_has_closed_is_left_alone_and_corrected_if_it_opens_again()
    {
        var block = new TextBlock();
        Localized.SetText(block, "common.close");
        var window = Show(new Window { Content = block });
        Assert.Equal(English.Of("common.close"), block.Text);

        window.Close();
        window.Content = null;
        Dispatcher.UIThread.RunJobs();

        // Closed is not collected: the control is still in the retranslation table, and writing into
        // it now would reach template bindings and glyph runs that went with its window.
        using (Localizer.Speaking("ja"))
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(English.Of("common.close"), block.Text);

            // Shown again, it catches up with the language it slept through.
            Show(new Window { Content = block });
            Assert.Equal(Localizer.T("common.close"), block.Text);
        }
    }

    [AvaloniaFact]
    public void the_language_list_names_every_language_in_its_own_words()
    {
        var options = LanguageOption.All();

        // System first, then the ten languages.
        Assert.Equal(Languages.System, options[0].Value);
        Assert.Equal(English.Of("settings.languageSystem"), options[0].Name);
        Assert.Equal(Languages.Tags, options.Skip(1).Select(option => option.Value));

        // Each name is the language's own, not a translation of it.
        Assert.Contains(options, option => option.Name == "日本語");
        Assert.Contains(options, option => option.Name == "Русский");
        Assert.Contains(options, option => option.Name == "中文");
        Assert.Contains(options, option => option.Name == "Português");
    }

    [AvaloniaFact]
    public void choosing_a_language_enables_save()
    {
        var config = new AppConfig();
        var dialog = Show(new SettingsDialog(config, _ => true));
        var save = Button(dialog, English.Of("common.save"));
        var languages = dialog.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "LanguageBox");
        Assert.False(save.IsEnabled);
        Assert.Equal(Languages.System, ((LanguageOption)languages.SelectedItem!).Value);

        languages.SelectedItem = languages.Items.OfType<LanguageOption>().Single(option => option.Value == "fr");

        Assert.True(save.IsEnabled);
        Assert.Equal("fr", config.Language);
    }

    [AvaloniaFact]
    public async Task a_saved_language_is_spoken_at_once_and_stored()
    {
        using var empty = PopulatedMainWindow.Empty();
        // Speaking the current language is only the guard: it puts the language and the preference
        // back exactly when the test ends, whatever the save did to them.
        using var restore = Localizer.Speaking(Localizer.Language);
        empty.Dialogs.SettingsEdit = config => config.Language = "de";

        await empty.ViewModel.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal("de", Localizer.Language);
        Assert.Contains("\"language\": \"de\"", System.IO.File.ReadAllText(new DayNote.Core.Storage.AppPaths().ConfigFile));
    }

    [AvaloniaFact]
    public void a_binding_re_reads_when_a_view_model_says_every_property_changed()
    {
        // The view model answers a language change with one PropertyChanged carrying no name, which
        // means "all of them". Everything the main window shows depends on Avalonia honouring that, so
        // it is pinned here rather than assumed.
        var source = new EverythingChanged();
        var text = new TextBlock();
        text.Bind(TextBlock.TextProperty, new Binding(nameof(EverythingChanged.Words)) { Source = source });
        Show(new Window { Content = text });
        Assert.Equal("before", text.Text);

        source.Words = "after";
        source.Raise();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("after", text.Text);
    }

    private sealed class EverythingChanged : INotifyPropertyChanged
    {
        public string Words { get; set; } = "before";

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private static Button Button(Window window, string content) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, content));

    private static TextBlock Text(Window window, string words) =>
        window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == words);
}
