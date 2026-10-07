using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DayNote.Core.Models;
using DayNote.Logging;
using DayNote.ViewModels;
using Xunit;

namespace DayNote.Tests.ViewModels;

public sealed class AttachmentItemViewModelTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "daynote-thumbnail-" + Guid.NewGuid() + ".png");

    public AttachmentItemViewModelTests() => File.WriteAllBytes(_path, [1]);

    [AvaloniaFact]
    public async Task A_decode_completed_after_unavailable_does_not_restore_a_thumbnail()
    {
        var pending = new TaskCompletionSource<(Bitmap, PixelSize)>();
        using var row = Row(_ => pending.Task);
        var task = row.ThumbnailTask;
        row.ShowUnavailable();
        pending.SetResult((Bitmap(), new PixelSize(1, 1)));
        await task;
        Assert.False(row.Exists);
        Assert.Null(row.Thumbnail);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_older_decode_cannot_replace_or_clear_the_newer_thumbnail(bool olderFails)
    {
        var older = new TaskCompletionSource<(Bitmap, PixelSize)>();
        var newer = new TaskCompletionSource<(Bitmap, PixelSize)>();
        var calls = 0;
        using var row = Row(_ => ++calls == 1 ? older.Task : newer.Task);
        var olderTask = row.ThumbnailTask;
        row.ClearOpenResult();
        var newerTask = row.ThumbnailTask;
        var latest = Bitmap();
        newer.SetResult((latest, new PixelSize(9, 7)));
        await newerTask;
        if (olderFails)
            older.SetException(new IOException("older decode failed"));
        else
            older.SetResult((Bitmap(), new PixelSize(1, 1)));
        await olderTask;
        Assert.Same(latest, row.Thumbnail);
        Assert.Contains("9×7", row.DetailsText);
    }

    [AvaloniaFact]
    public async Task A_disposed_row_rejects_a_pending_decode()
    {
        var pending = new TaskCompletionSource<(Bitmap, PixelSize)>();
        var row = Row(_ => pending.Task);
        var task = row.ThumbnailTask;
        row.Dispose();
        pending.SetResult((Bitmap(), new PixelSize(1, 1)));
        await task;
        Assert.Null(row.Thumbnail);
    }

    private AttachmentItemViewModel Row(Func<string, Task<(Bitmap, PixelSize)>> decode) =>
        new(new Attachment("image.png", _path), new NullLogger(), decode);

    private static Bitmap Bitmap() => new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96),
        PixelFormat.Bgra8888, AlphaFormat.Premul);

    private sealed class NullLogger : IAppLogger
    {
        public void Debug(string message, object? data = null, Exception? error = null) { }
        public void Info(string message, object? data = null, Exception? error = null) { }
        public void Warn(string message, object? data = null, Exception? error = null) { }
        public void Error(string message, object? data = null, Exception? error = null) { }
    }

    public void Dispose() => File.Delete(_path);
}
