using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DayNote.Core.Models;
using DayNote.Logging;
using DayNote.ViewModels;
using SkiaSharp;
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
        if (olderFails)
            older.SetException(new IOException("older decode failed"));
        else
            older.SetResult((Bitmap(), new PixelSize(1, 1)));
        await olderTask;
        await newerTask;
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

    [AvaloniaFact]
    public async Task A_disposed_queued_row_does_not_start_a_decode()
    {
        var pending = new TaskCompletionSource<(Bitmap, PixelSize)>();
        using var active = Row(_ => pending.Task);
        var called = false;
        using var queued = Row(_ =>
        {
            called = true;
            return Task.FromResult((Bitmap(), new PixelSize(1, 1)));
        });
        queued.Dispose();
        pending.SetResult((Bitmap(), new PixelSize(1, 1)));
        await active.ThumbnailTask;
        await queued.ThumbnailTask;
        Assert.False(called);
        queued.ClearOpenResult();
        Assert.False(called);
    }

    [AvaloniaTheory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    public async Task Native_decoding_keeps_original_dimensions_with_a_small_preview(SKEncodedImageFormat format)
    {
        using (var image = new SKBitmap(1600, 800))
        using (var bytes = image.Encode(format, 80))
        using (var file = File.Create(_path))
            bytes.SaveTo(file);

        var (preview, dimensions) = await AttachmentItemViewModel.DecodeThumbnailAsync(_path);
        using (preview)
        {
            Assert.Equal(new PixelSize(1600, 800), dimensions);
            Assert.Equal(new PixelSize(240, 120), preview.PixelSize);
        }
    }

    private AttachmentItemViewModel Row(Func<string, Task<(Bitmap, PixelSize)>> decode) =>
        new(new Attachment("image.png", _path), new NullLogger(), decode);

    [AvaloniaFact]
    public async Task Repeated_preview_replacement_and_cancellation_disposes_every_bitmap_once()
    {
        var created = new List<CountedBitmap>();
        var active = 0;
        var maximumActive = 0;
        var queuedCalls = 0;
        for (var round = 0; round < 12; round++)
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var tasks = new List<Task>();
            var queued = new List<AttachmentItemViewModel>();
            using var row = Row(async _ =>
            {
                active++;
                maximumActive = Math.Max(maximumActive, active);
                var bitmap = new CountedBitmap();
                created.Add(bitmap);
                try
                {
                    if (++calls == 1)
                    {
                        entered.SetResult();
                        await release.Task;
                    }
                    return (bitmap, new PixelSize(1, 1));
                }
                finally { active--; }
            });
            tasks.Add(row.ThumbnailTask);
            try
            {
                await entered.Task;
                for (var index = 0; index < 3; index++)
                {
                    var obsolete = Row(_ =>
                    {
                        queuedCalls++;
                        var bitmap = new CountedBitmap();
                        created.Add(bitmap);
                        return Task.FromResult<(Bitmap, PixelSize)>((bitmap, new PixelSize(1, 1)));
                    });
                    queued.Add(obsolete);
                    tasks.Add(obsolete.ThumbnailTask);
                    obsolete.Dispose();
                }
                row.ShowUnavailable();
                if (round % 2 == 0)
                {
                    row.ClearOpenResult();
                    tasks.Add(row.ThumbnailTask);
                }
                else row.Dispose();
                release.SetResult();
                await Task.WhenAll(tasks);
                if (round % 2 == 0)
                {
                    var adopted = Assert.IsType<CountedBitmap>(row.Thumbnail);
                    Assert.Equal(0, adopted.DisposeCalls);
                    row.ShowUnavailable();
                    Assert.Equal(1, adopted.DisposeCalls);
                    row.ClearOpenResult();
                    tasks.Add(row.ThumbnailTask);
                    await row.ThumbnailTask;
                    Assert.Equal(0, Assert.IsType<CountedBitmap>(row.Thumbnail).DisposeCalls);
                }
            }
            finally
            {
                release.TrySetResult();
                row.Dispose();
                queued.ForEach(item => item.Dispose());
                await Task.WhenAll(tasks);
            }
        }
        Assert.Equal(1, maximumActive);
        Assert.Equal(0, active);
        Assert.Equal(0, queuedCalls);
        Assert.Equal(24, created.Count);
        Assert.All(created, bitmap => Assert.Equal(1, bitmap.DisposeCalls));
    }

    [AvaloniaFact]
    public async Task Repeated_native_valid_and_corrupt_decodes_release_the_source_file()
    {
        for (var round = 0; round < 6; round++)
        {
            using (var pixels = new SKBitmap(9, 7))
            using (var bytes = pixels.Encode(SKEncodedImageFormat.Png, 80))
            using (var file = File.Create(_path))
                bytes.SaveTo(file);
            var (preview, dimensions) = await AttachmentItemViewModel.DecodeThumbnailAsync(_path);
            using (preview) Assert.Equal(new PixelSize(9, 7), dimensions);
            using (var exclusive = File.Open(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                exclusive.SetLength(0);
                exclusive.Write([1, 2, 3]);
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => AttachmentItemViewModel.DecodeThumbnailAsync(_path));
            using var afterFailure = File.Open(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            afterFailure.SetLength(0);
        }
    }

    private sealed class CountedBitmap()
        : WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul)
    {
        public int DisposeCalls { get; private set; }
        public override void Dispose()
        {
            DisposeCalls++;
            base.Dispose();
        }
    }

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
