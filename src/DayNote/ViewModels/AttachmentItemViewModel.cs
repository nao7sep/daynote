using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.Core.Models;
using DayNote.Logging;

namespace DayNote.ViewModels;

/// <summary>
/// A row in the attachments pane. Images are decoded to a bounded thumbnail; other files and failed
/// previews use the view's generic document placeholder. A details line shows the image dimensions
/// (for images) and the file size.
/// </summary>
public sealed partial class AttachmentItemViewModel : ObservableObject, IDisposable
{
    private const int ThumbnailWidth = 240;

    private readonly IAppLogger _log;
    private readonly string _sizeText;
    private bool _disposed;

    public AttachmentItemViewModel(Attachment attachment, IAppLogger log)
    {
        _log = log;
        Attachment = attachment;
        FileName = attachment.FileName;
        FullPath = attachment.FullPath;
        IsImage = attachment.IsImage;
        Exists = File.Exists(attachment.FullPath);
        _sizeText = FormatSize(FileSize(attachment.FullPath));
        // Until/unless image dimensions load, the details line is just the file size.
        DetailsText = _sizeText;

        if (IsImage && Exists)
        {
            _ = LoadThumbnailAsync();
        }
    }

    public Attachment Attachment { get; }

    public string FileName { get; }

    public string FullPath { get; }

    public bool IsImage { get; }

    [ObservableProperty]
    private bool _exists;

    /// <summary>Secondary line: "<c>W×H · size</c>" for an image, just the size otherwise.</summary>
    [ObservableProperty]
    private string _detailsText = string.Empty;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    public bool HasThumbnail => Thumbnail is not null;

    public bool ShowFilePlaceholder => Thumbnail is null;

    partial void OnThumbnailChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasThumbnail));
        OnPropertyChanged(nameof(ShowFilePlaceholder));
    }

    private async Task LoadThumbnailAsync()
    {
        var path = FullPath;
        try
        {
            // Decode off the UI thread so selecting an image-heavy note does not block the UI; the
            // full decode yields the original pixel size for the details line, then is scaled down to a
            // bounded thumbnail. The continuation resumes on the UI thread (assigning Thumbnail is
            // binding-safe there).
            var (bitmap, size) = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                using var full = new Bitmap(stream);
                var original = full.PixelSize;
                var width = Math.Min(ThumbnailWidth, original.Width);
                var height = Math.Max(1, (int)Math.Round(original.Height * (double)width / original.Width));
                return (full.CreateScaledBitmap(new PixelSize(width, height)), original);
            });

            if (_disposed)
            {
                bitmap.Dispose();
                return;
            }

            Thumbnail = bitmap;
            DetailsText = $"{size.Width}×{size.Height} · {_sizeText}";
        }
        catch (Exception ex)
        {
            // A corrupt or unsupported image is recoverable — the row just shows no preview — but it
            // is unexpected for a file we classified as an image, so it is recorded rather than swallowed.
            _log.Warn("Could not decode attachment thumbnail", new { path }, ex);
            Thumbnail = null;
        }
    }

    private static long FileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        string[] units = { "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = -1;
        do
        {
            value /= 1024;
            unit++;
        }
        while (value >= 1024 && unit < units.Length - 1);

        return value >= 100 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }

    public void Dispose()
    {
        _disposed = true;
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}
