using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using DayNote.I18n;
using DayNote.Services;
using Xunit;

namespace DayNote.Tests.Services;

public sealed class DialogServiceTests
{
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
}
