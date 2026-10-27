using System;
using System.IO;
using System.Threading;
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
    private readonly string _thumbnails = Path.Combine(Path.GetTempPath(), "daynote-thumbnails-" + Guid.NewGuid());

    private Task<(Bitmap Bitmap, PixelSize Size)> Thumbnail() =>
        AttachmentThumbnails.LoadAsync(_path, _thumbnails, new NullLogger());

    private void WriteImage(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var image = new SKBitmap(width, height);
        image.Erase(SKColors.CornflowerBlue);
        using var bytes = image.Encode(format, 80);
        using var file = File.Create(_path);
        bytes.SaveTo(file);
    }

    public AttachmentItemViewModelTests() => File.WriteAllBytes(_path, [1]);

    [AvaloniaFact]
    public async Task A_decode_completed_after_unavailable_does_not_restore_a_thumbnail()
    {
        var pending = new TaskCompletionSource<(Bitmap, PixelSize)>();
        using var row = await LoadingRow(_ => pending.Task);
        var task = row.ThumbnailTask;
        row.ShowUnavailable();
        var bitmap = new CountedBitmap();
        pending.SetResult((bitmap, new PixelSize(1, 1)));
        await task;
        Assert.False(row.Exists);
        Assert.Null(row.Thumbnail);
        Assert.Equal(1, bitmap.DisposeCalls);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_older_decode_cannot_replace_or_clear_the_newer_thumbnail(bool olderFails)
    {
        var older = new TaskCompletionSource<(Bitmap, PixelSize)>();
        var newerEntered = new TaskCompletionSource();
        var calls = 0;
        var latest = Bitmap();
        using var row = await LoadingRow(_ =>
        {
            if (++calls == 1) return older.Task;
            newerEntered.SetResult();
            return Task.FromResult((latest, new PixelSize(9, 7)));
        });
        row.ClearOpenResult();
        await newerEntered.Task;
        // The aggregate continues to own the older physical request after a newer result arrives.
        Assert.False(row.ThumbnailTask.IsCompleted);
        if (olderFails)
            older.SetException(new IOException("older decode failed"));
        else
            older.SetResult((Bitmap(), new PixelSize(1, 1)));
        await row.ThumbnailTask;
        Assert.Same(latest, row.Thumbnail);
        Assert.Contains("9×7", row.DetailsText);
    }

    [AvaloniaFact]
    public async Task A_disposed_row_rejects_a_pending_decode()
    {
        var pending = new TaskCompletionSource<(Bitmap, PixelSize)>();
        var row = await LoadingRow(_ => pending.Task);
        var task = row.ThumbnailTask;
        row.Dispose();
        var bitmap = new CountedBitmap();
        pending.SetResult((bitmap, new PixelSize(1, 1)));
        await task;
        Assert.Null(row.Thumbnail);
        Assert.Equal(1, bitmap.DisposeCalls);
    }

    [AvaloniaFact]
    public async Task A_missing_file_is_reported_without_a_zero_size()
    {
        File.Delete(_path);
        using var row = new AttachmentItemViewModel(new Attachment("file.txt", _path), new NullLogger(), _thumbnails);
        await row.ThumbnailTask;
        Assert.False(row.Exists);
        Assert.Equal(OperationResultKind.Warning, row.Result?.Kind);
        Assert.Null(row.Thumbnail);
    }

    [AvaloniaTheory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task A_thumbnail_keeps_the_original_dimensions_and_is_kept_as_webp(SKEncodedImageFormat format)
    {
        WriteImage(1600, 800, format);

        var (preview, dimensions) = await Thumbnail();
        using (preview)
        {
            Assert.Equal(new PixelSize(1600, 800), dimensions);
            Assert.Equal(new PixelSize(AttachmentThumbnails.Size, AttachmentThumbnails.Size / 2), preview.PixelSize);
        }

        var kept = Assert.Single(Directory.GetFiles(_thumbnails));
        Assert.Equal(AttachmentThumbnails.NameFor(new FileInfo(_path)), Path.GetFileName(kept));
        using var codec = SKCodec.Create(kept);
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        Assert.Equal(new SKSizeI(AttachmentThumbnails.Size, AttachmentThumbnails.Size / 2), codec.Info.Size);
    }

    [AvaloniaFact]
    public async Task A_kept_thumbnail_is_shown_without_decoding_the_original_again()
    {
        WriteImage(1600, 800);
        using (var first = (await Thumbnail()).Bitmap) { }
        var written = File.GetLastWriteTimeUtc(_path);
        // The same name, size and time, but pixels nobody could decode: only the kept thumbnail can answer.
        var bytes = File.ReadAllBytes(_path);
        Array.Fill(bytes, (byte)0, 100, bytes.Length - 100);
        File.WriteAllBytes(_path, bytes);
        File.SetLastWriteTimeUtc(_path, written);

        var (preview, dimensions) = await Thumbnail();
        using (preview)
        {
            Assert.Equal(new PixelSize(1600, 800), dimensions);
            Assert.Equal(new PixelSize(AttachmentThumbnails.Size, AttachmentThumbnails.Size / 2), preview.PixelSize);
        }
    }

    [AvaloniaFact]
    public async Task An_edited_image_gets_a_new_thumbnail()
    {
        WriteImage(1600, 800);
        using (var first = (await Thumbnail()).Bitmap) { }
        var before = AttachmentThumbnails.NameFor(new FileInfo(_path));

        WriteImage(800, 1600);
        File.SetLastWriteTimeUtc(_path, File.GetLastWriteTimeUtc(_path).AddSeconds(5));
        var (preview, _) = await Thumbnail();
        using (preview)
            Assert.Equal(new PixelSize(AttachmentThumbnails.Size / 2, AttachmentThumbnails.Size), preview.PixelSize);

        Assert.NotEqual(before, AttachmentThumbnails.NameFor(new FileInfo(_path)));
        Assert.Equal(2, Directory.GetFiles(_thumbnails).Length);
    }

    [AvaloniaFact]
    public async Task An_image_that_cannot_be_decoded_keeps_the_file_icon_and_keeps_nothing()
    {
        File.WriteAllBytes(_path, [1, 2, 3]);

        using var row = new AttachmentItemViewModel(new Attachment("image.png", _path), new NullLogger(),
            path => AttachmentThumbnails.LoadAsync(path, _thumbnails, new NullLogger()));
        await row.ThumbnailTask;

        Assert.Null(row.Thumbnail);
        Assert.True(row.ShowFilePlaceholder);
        Assert.False(Directory.Exists(_thumbnails) && Directory.GetFiles(_thumbnails).Length > 0);
    }

    private AttachmentItemViewModel Row(Func<string, Task<(Bitmap, PixelSize)>> decode) =>
        new(new Attachment("image.png", _path), new NullLogger(), decode);

    [AvaloniaFact]
    public async Task A_cached_thumbnail_bypasses_the_original_decode_admission()
    {
        WriteImage(1600, 800);
        using (var first = (await Thumbnail()).Bitmap) { }
        await AttachmentThumbnails.DecodeAdmission.WaitAsync();
        try
        {
            var cached = Thumbnail();
            var (bitmap, dimensions) = await cached.WaitAsync(TimeSpan.FromSeconds(5));
            using (bitmap) Assert.Equal(new PixelSize(1600, 800), dimensions);
        }
        finally { AttachmentThumbnails.DecodeAdmission.Release(); }
    }

    [AvaloniaFact]
    public async Task A_canceled_request_does_not_decode_or_keep_a_thumbnail_while_admission_is_held()
    {
        WriteImage(1600, 800);
        using var cancellation = new CancellationTokenSource();
        await AttachmentThumbnails.DecodeAdmission.WaitAsync();
        try
        {
            var queued = AttachmentThumbnails.LoadAsync(_path, _thumbnails, new NullLogger(), cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            Assert.False(Directory.Exists(_thumbnails));
        }
        finally { AttachmentThumbnails.DecodeAdmission.Release(); }
    }

    [AvaloniaFact]
    public async Task Reopening_an_image_replaces_an_existing_preview_when_its_fingerprint_changes()
    {
        WriteImage(1600, 800);
        using var row = new AttachmentItemViewModel(new Attachment("image.png", _path), new NullLogger(), _thumbnails);
        await row.ThumbnailTask;
        Assert.Contains("1600×800", row.DetailsText);
        WriteImage(800, 1600);
        File.SetLastWriteTimeUtc(_path, File.GetLastWriteTimeUtc(_path).AddSeconds(5));
        row.ClearOpenResult();
        await row.ThumbnailTask;
        Assert.Contains("800×1600", row.DetailsText);
        Assert.Equal(new PixelSize(AttachmentThumbnails.Size / 2, AttachmentThumbnails.Size), row.Thumbnail?.PixelSize);
    }

    private async Task<AttachmentItemViewModel> LoadingRow(Func<string, Task<(Bitmap, PixelSize)>> decode)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var row = Row(path =>
        {
            entered.TrySetResult();
            return decode(path);
        });
        await entered.Task;
        return row;
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
            var (preview, dimensions) = await Thumbnail();
            using (preview) Assert.Equal(new PixelSize(9, 7), dimensions);
            using (var exclusive = File.Open(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                exclusive.SetLength(0);
                exclusive.Write([1, 2, 3]);
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => Thumbnail());
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

    public void Dispose()
    {
        File.Delete(_path);
        if (Directory.Exists(_thumbnails))
            Directory.Delete(_thumbnails, recursive: true);
    }
}
