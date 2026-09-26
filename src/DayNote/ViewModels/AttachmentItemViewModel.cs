using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.Core.Models;
using DayNote.I18n;
using DayNote.Logging;

namespace DayNote.ViewModels;

/// <summary>
/// A row in the attachments pane. Images are decoded to a bounded thumbnail; other files and failed
/// previews use the view's generic document placeholder. A details line shows the image dimensions
/// (for images) and the file size, in the current language (<see cref="Retranslate"/>).
/// </summary>
public sealed partial class AttachmentItemViewModel : ObservableObject, IDisposable
{
    private const int ThumbnailWidth = 240;
    private const string UnavailableKey = "attachment.unavailableMessage";

    private readonly IAppLogger _log;
    private long _size;
    private PixelSize? _dimensions;
    private bool _disposed;

    public AttachmentItemViewModel(Attachment attachment, IAppLogger log)
    {
        _log = log;
        Attachment = attachment;
        FileName = attachment.FileName;
        FullPath = attachment.FullPath;
        IsImage = attachment.IsImage;
        Exists = File.Exists(attachment.FullPath);
        _size = FileSize(attachment.FullPath);

        if (!Exists)
        {
            _log.Warn("Attachment is unavailable", new { path = FullPath });
            Result = new OperationResultViewModel(
                OperationResultKind.Warning,
                Message.Of(UnavailableKey),
                isPersistent: true);
        }

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
    [NotifyPropertyChangedFor(nameof(DetailsText))]
    private bool _exists;

    /// <summary>
    /// Secondary line: "<c>W×H · size</c>" for an image whose dimensions have loaded, just the size
    /// otherwise, and a word saying so when the file is gone.
    /// </summary>
    public string DetailsText => !Exists
        ? Localizer.T("attachment.unavailable")
        : _dimensions is { } size
            ? $"{size.Width}×{size.Height} · {FormatSize(_size)}"
            : FormatSize(_size);

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private OperationResultViewModel? _result;

    public bool HasResult => Result is not null;

    public bool HasThumbnail => Thumbnail is not null;

    public bool ShowFilePlaceholder => Thumbnail is null;

    partial void OnThumbnailChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasThumbnail));
        OnPropertyChanged(nameof(ShowFilePlaceholder));
    }

    partial void OnResultChanged(OperationResultViewModel? value) =>
        OnPropertyChanged(nameof(HasResult));

    public void ShowOpenFailure() =>
        Result = new OperationResultViewModel(
            OperationResultKind.Error,
            Message.Of("attachment.openFailed"),
            isPersistent: true);

    /// <summary>Called by the main window's view model when the language changes.</summary>
    internal void Retranslate()
    {
        OnPropertyChanged(nameof(DetailsText));
        Result?.Retranslate();
    }

    public void ShowUnavailable()
    {
        Exists = false;
        Thumbnail?.Dispose();
        Thumbnail = null;
        _dimensions = null;
        if (Result is { Kind: OperationResultKind.Warning, Message.Key: UnavailableKey })
        {
            return;
        }

        Result = new OperationResultViewModel(
            OperationResultKind.Warning,
            Message.Of(UnavailableKey),
            isPersistent: true);
    }

    public void ClearOpenResult()
    {
        _size = FileSize(FullPath);
        Exists = true;
        OnPropertyChanged(nameof(DetailsText));
        if (Result?.Kind is OperationResultKind.Warning or OperationResultKind.Error)
        {
            Result = null;
        }

        if (IsImage && Thumbnail is null)
        {
            _ = LoadThumbnailAsync();
        }
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
            _dimensions = size;
            OnPropertyChanged(nameof(DetailsText));
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

    // The unit is catalogue text, since some languages abbreviate it their own way (French writes
    // "Ko"), and the number is written with the reader's decimal mark.
    private static string FormatSize(long bytes)
    {
        var culture = Localizer.Current.Culture;
        if (bytes < 1024)
        {
            return Localizer.T("size.bytes", ("size", bytes.ToString("N0", culture)));
        }

        string[] units = ["size.kilobytes", "size.megabytes", "size.gigabytes", "size.terabytes"];
        double value = bytes;
        var unit = -1;
        do
        {
            value /= 1024;
            unit++;
        }
        while (value >= 1024 && unit < units.Length - 1);

        return Localizer.T(units[unit], ("size", value.ToString(value >= 100 ? "0" : "0.0", culture)));
    }

    public void Dispose()
    {
        _disposed = true;
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}
