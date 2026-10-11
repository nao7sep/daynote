using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.Core.Models;
using DayNote.I18n;
using DayNote.Logging;
using SkiaSharp;

namespace DayNote.ViewModels;

/// <summary>
/// A row in the attachments pane. Images are decoded to a bounded thumbnail; other files and failed
/// previews use the view's generic document placeholder. A details line shows the image dimensions
/// (for images) and the file size, in the current language (<see cref="Retranslate"/>).
/// </summary>
public sealed partial class AttachmentItemViewModel : ObservableObject, IDisposable
{
    private const int ThumbnailWidth = 240;
    // A note can contain many camera images. Own one physical decode through completion, including
    // after its row is disposed; queued obsolete rows never start another full native decode.
    private static readonly SemaphoreSlim DecodeAdmission = new(1, 1);
    private const string UnavailableKey = "attachment.unavailableMessage";

    private readonly IAppLogger _log;
    private long _size;
    private PixelSize? _dimensions;
    private bool _disposed;
    private int _thumbnailGeneration;
    private readonly Func<string, Task<(Bitmap Bitmap, PixelSize Size)>> _decodeThumbnail;

    internal Task ThumbnailTask { get; private set; } = Task.CompletedTask;

    public AttachmentItemViewModel(Attachment attachment, IAppLogger log)
        : this(attachment, log, DecodeThumbnailAsync) { }

    internal AttachmentItemViewModel(Attachment attachment, IAppLogger log,
        Func<string, Task<(Bitmap Bitmap, PixelSize Size)>> decodeThumbnail)
    {
        _decodeThumbnail = decodeThumbnail;
        _log = log;
        Attachment = attachment;
        FileName = attachment.FileName;
        FullPath = attachment.FullPath;
        IsImage = attachment.IsImage;
        // On the UI thread, as the note's rows are built: these files sit in the open binder's own folder,
        // and when that volume stalls the binder cannot be used anyway.
        Exists = File.Exists(attachment.FullPath);
        _size = FileSize(attachment.FullPath);

        if (!Exists)
        {
            _log.Warn("Attachment is unavailable", new { path = FullPath });
            Result = new OperationResultViewModel(
                OperationResultKind.Warning,
                Message.Of(UnavailableKey));
        }

        if (IsImage && Exists)
        {
            ThumbnailTask = LoadThumbnailAsync();
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
            Message.Of("attachment.openFailed"));

    /// <summary>Called by the main window's view model when the language changes.</summary>
    internal void Retranslate()
    {
        OnPropertyChanged(nameof(DetailsText));
        Result?.Retranslate();
    }

    public void ShowUnavailable()
    {
        _thumbnailGeneration++;
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
            Message.Of(UnavailableKey));
    }

    public void ClearOpenResult()
    {
        if (_disposed)
            return;
        _thumbnailGeneration++;
        _size = FileSize(FullPath);
        Exists = true;
        OnPropertyChanged(nameof(DetailsText));
        if (Result?.Kind is OperationResultKind.Warning or OperationResultKind.Error)
        {
            Result = null;
        }

        if (IsImage && Thumbnail is null)
        {
            ThumbnailTask = LoadThumbnailAsync();
        }
    }

    private async Task LoadThumbnailAsync()
    {
        var path = FullPath;
        var generation = ++_thumbnailGeneration;
        try
        {
            await DecodeAdmission.WaitAsync();
            Bitmap bitmap;
            PixelSize size;
            try
            {
                if (_disposed || generation != _thumbnailGeneration || !Exists)
                    return;
                (bitmap, size) = await _decodeThumbnail(path);
            }
            finally
            {
                DecodeAdmission.Release();
            }

            if (_disposed || generation != _thumbnailGeneration || !Exists)
            {
                bitmap.Dispose();
                return;
            }

            Thumbnail?.Dispose();
            Thumbnail = bitmap;
            _dimensions = size;
            OnPropertyChanged(nameof(DetailsText));
        }
        catch (Exception ex)
        {
            // A corrupt or unsupported image is recoverable — the row just shows no preview — but it
            // is unexpected for a file we classified as an image, so it is recorded rather than swallowed.
            _log.Warn("Could not decode attachment thumbnail", new { path }, ex);
            if (!_disposed && generation == _thumbnailGeneration)
            {
                Thumbnail?.Dispose();
                Thumbnail = null;
            }
        }
    }

    internal static Task<(Bitmap Bitmap, PixelSize Size)> DecodeThumbnailAsync(string path) => Task.Run(() =>
    {
        // Codec metadata supplies the original dimensions without a full pixel allocation or an
        // in-memory copy of the encoded file. Some formats still need a full raster; admission above
        // prevents several of those allocations at once, rather than claiming a per-image memory cap.
        using var codec = SKCodec.Create(File.OpenRead(path)) ?? throw new InvalidDataException("Image codec could not be opened.");
        var original = new PixelSize(codec.Info.Width, codec.Info.Height);
        var scale = Math.Min(1d, ThumbnailWidth / (double)Math.Max(original.Width, original.Height));
        var dimensions = codec.GetScaledDimensions((float)scale);
        var info = new SKImageInfo(dimensions.Width, dimensions.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(info);
        if (codec.GetPixels(info, decoded.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("Image pixels could not be decoded.");
        var size = new PixelSize(Math.Max(1, (int)Math.Round(original.Width * scale)), Math.Max(1, (int)Math.Round(original.Height * scale)));
        using var preview = decoded.Resize(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKFilterMode.Linear)) ?? throw new InvalidDataException("Image preview could not be resized.");
        return (new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, preview.GetPixels(), size, new Vector(96, 96), preview.RowBytes), original);
    });

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
        _thumbnailGeneration++;
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}
