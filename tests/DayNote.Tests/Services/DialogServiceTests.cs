using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using DayNote.Core.Configuration;
using DayNote.I18n;
using DayNote.Logging;
using DayNote.Services;
using Xunit;

namespace DayNote.Tests.Services;

public sealed class DialogServiceTests
{
    [AvaloniaFact]
    public async Task Ordinary_modal_display_failures_are_logged_and_return_dismissed_outcomes()
    {
        // Avalonia rejects modal display over an owner that is not visible.
        var log = new FailureLogger();
        var service = new DialogService(log) { Owner = new Window() };
        var saved = false;

        Assert.False(await service.ConfirmAsync(Message.Of("note.deleteTitle"), Message.Of("note.deleteUntitled"), "common.delete"));
        await service.ShowErrorAsync(Message.Of("failure.startupDataTitle"), Message.Of("failure.settingsSave"));
        await service.ShowAboutAsync();
        await service.ShowShortcutsAsync();
        Assert.False(await service.ShowSettingsAsync(new AppConfig(), _ =>
        {
            saved = true;
            return Task.FromResult(true);
        }));

        Assert.False(saved);
        Assert.Equal(5, log.Errors.Count);
        Assert.All(log.Errors, entry =>
        {
            Assert.Equal("Failed to show dialog", entry.Message);
            Assert.IsType<InvalidOperationException>(entry.Error);
            Assert.NotEmpty(entry.Error.StackTrace!);
        });
    }

    [AvaloniaFact]
    public async Task Operation_owned_modal_failures_remain_rejected_for_the_caller()
    {
        var log = new FailureLogger();
        var service = new DialogService(log) { Owner = new Window() };
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AskExternalChangeAsync("Journal"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AskQuitWithUnsavedBinderAsync("Journal"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.WaitForAttachmentsAsync(settled.Task, TimeSpan.FromHours(1)));
            Assert.Empty(log.Errors);
        }
        finally
        {
            settled.TrySetResult();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Binder_pickers_use_the_resolved_current_directory(bool available)
    {
        var directory = Path.GetTempPath();
        var folder = await new Window().StorageProvider.TryGetFolderFromPathAsync(new Uri(directory));
        Assert.NotNull(folder);
        var storage = RecordingStorage.Create();
        storage.Folder = available ? folder : null;

        Assert.Null(await DialogService.PickBinderToOpenAsync(storage.Provider, directory));
        Assert.Equal(new Uri(directory), storage.RequestedFolder);
        Assert.Same(storage.Folder, storage.OpenOptions!.SuggestedStartLocation);
        Assert.False(storage.OpenOptions.AllowMultiple);
        Assert.Equal("*.daynote", Assert.Single(Assert.Single(storage.OpenOptions.FileTypeFilter!).Patterns!));

        Assert.Null(await DialogService.PickBinderToCreateAsync(storage.Provider, directory));
        Assert.Equal(new Uri(directory), storage.RequestedFolder);
        Assert.Same(storage.Folder, storage.SaveOptions!.SuggestedStartLocation);
        Assert.Equal(Localizer.T("picker.binderFileName"), storage.SaveOptions.SuggestedFileName);
        Assert.Equal("daynote", storage.SaveOptions.DefaultExtension);
        Assert.True(storage.SaveOptions.ShowOverwritePrompt);
    }

    [AvaloniaFact]
    public async Task Binder_pickers_without_context_leave_the_start_to_the_platform()
    {
        var storage = RecordingStorage.Create();
        Assert.Null(await DialogService.PickBinderToOpenAsync(storage.Provider, null));
        Assert.Null(await DialogService.PickBinderToCreateAsync(storage.Provider, null));
        Assert.Null(storage.RequestedFolder);
        Assert.Null(storage.OpenOptions!.SuggestedStartLocation);
        Assert.Null(storage.SaveOptions!.SuggestedStartLocation);
    }

    // Avalonia deliberately forbids source implementations of this evolving interface. A runtime
    // proxy records the actual outbound calls without replacing the dialog option construction.
    public class RecordingStorage : DispatchProxy
    {
        public IStorageProvider Provider => (IStorageProvider)(object)this;
        public IStorageFolder? Folder { get; set; }
        public Uri? RequestedFolder { get; private set; }
        public FilePickerOpenOptions? OpenOptions { get; private set; }
        public FilePickerSaveOptions? SaveOptions { get; private set; }
        public static RecordingStorage Create() =>
            (RecordingStorage)(object)DispatchProxy.Create<IStorageProvider, RecordingStorage>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case nameof(IStorageProvider.TryGetFolderFromPathAsync):
                    RequestedFolder = (Uri)args![0]!;
                    return Task.FromResult(Folder);
                case nameof(IStorageProvider.OpenFilePickerAsync):
                    OpenOptions = (FilePickerOpenOptions)args![0]!;
                    return Task.FromResult<IReadOnlyList<IStorageFile>>([]);
                case nameof(IStorageProvider.SaveFilePickerAsync):
                    SaveOptions = (FilePickerSaveOptions)args![0]!;
                    return Task.FromResult<IStorageFile?>(null);
                default:
                    throw new NotSupportedException(targetMethod.Name);
            }
        }
    }

    private sealed class FailureLogger : IAppLogger
    {
        public List<(string Message, Exception Error)> Errors { get; } = [];
        public void Debug(string message, object? data = null, Exception? error = null) { }
        public void Info(string message, object? data = null, Exception? error = null) { }
        public void Warn(string message, object? data = null, Exception? error = null) { }
        public void Error(string message, object? data = null, Exception? error = null) => Errors.Add((message, error!));
    }
}
