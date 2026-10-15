using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.Core.Models;
using DayNote.I18n;
using DayNote.Logging;
using DayNote.Services;
using SkiaSharp;

namespace DayNote.ViewModels;

/// <summary>
/// A row in the attachments pane. Images show a thumbnail kept on disk (<see cref="AttachmentThumbnails"/>);
/// other files, failed previews and images too large for the memory free now use the view's generic
/// document placeholder. A details line shows the image dimensions
/// (for images) and the file size, in the current language (<see cref="Retranslate"/>).
/// </summary>
public sealed partial class AttachmentItemViewModel : ObservableObject, IDisposable
{
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

    public AttachmentItemViewModel(Attachment attachment, IAppLogger log, string thumbnailsDirectory)
        : this(attachment, log, path => AttachmentThumbnails.LoadAsync(path, thumbnailsDirectory, AvailableMemory.Bytes, log)) { }

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
        catch (AttachmentThumbnails.NotEnoughMemoryException ex)
        {
            // Not a failure of the file: the row shows the file icon, and showing it again tries again.
            _log.Info("Attachment thumbnail waits for free memory", new { path, cost = ex.Cost });
            ClearThumbnail(generation);
        }
        catch (Exception ex)
        {
            // A corrupt or unsupported image is recoverable — the row just shows no preview — but it
            // is unexpected for a file we classified as an image, so it is recorded rather than swallowed.
            _log.Warn("Could not decode attachment thumbnail", new { path }, ex);
            ClearThumbnail(generation);
        }
    }

    private void ClearThumbnail(int generation)
    {
        if (!_disposed && generation == _thumbnailGeneration)
        {
            Thumbnail?.Dispose();
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
        _thumbnailGeneration++;
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}
