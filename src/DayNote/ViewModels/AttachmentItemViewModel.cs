using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.Core.Models;
using DayNote.I18n;
using DayNote.Logging;

namespace DayNote.ViewModels;

/// <summary>
/// A row in the attachments pane. Images show a thumbnail kept on disk (<see cref="AttachmentThumbnails"/>);
/// other files and failed previews, including a decode whose memory could not be allocated, use the
/// view's generic document placeholder. A details line shows the image dimensions
/// (for images) and the file size, in the current language (<see cref="Retranslate"/>).
/// </summary>
public sealed partial class AttachmentItemViewModel : ObservableObject, IDisposable
{
    // File metadata can stall on a mounted volume. Bound physical reads while keeping them off the UI.
    private static readonly SemaphoreSlim MetadataAdmission = new(Math.Max(2, Environment.ProcessorCount));
    private const string UnavailableKey = "attachment.unavailableMessage";

    private readonly IAppLogger _log;
    private long? _size;
    private PixelSize? _dimensions;
    private bool _disposed;
    private bool _active;
    private CancellationTokenSource? _refreshCancellation;
    private int _thumbnailGeneration;
    private readonly Func<string, CancellationToken, Task<(Bitmap Bitmap, PixelSize Size)>> _decodeThumbnail;

    internal Task ThumbnailTask { get; private set; } = Task.CompletedTask;

    public AttachmentItemViewModel(Attachment attachment, IAppLogger log, string thumbnailsDirectory)
        : this(attachment, log, (path, token) => AttachmentThumbnails.LoadAsync(path, thumbnailsDirectory, log, token)) { }

    internal AttachmentItemViewModel(Attachment attachment, IAppLogger log,
        Func<string, Task<(Bitmap Bitmap, PixelSize Size)>> decodeThumbnail)
        : this(attachment, log, (path, _) => decodeThumbnail(path)) { }

    private AttachmentItemViewModel(Attachment attachment, IAppLogger log,
        Func<string, CancellationToken, Task<(Bitmap Bitmap, PixelSize Size)>> decodeThumbnail)
    {
        _decodeThumbnail = decodeThumbnail;
        _log = log;
        Attachment = attachment;
        FileName = attachment.FileName;
        FullPath = attachment.FullPath;
        IsImage = attachment.IsImage;
    }

    public Attachment Attachment { get; }

    public string FileName { get; }

    public string FullPath { get; }

    public bool IsImage { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText))]
    private bool _exists = true;

    /// <summary>
    /// Secondary line: "<c>W×H · size</c>" for an image whose dimensions have loaded, just the size
    /// otherwise, and a word saying so when the file is gone.
    /// </summary>
    public string DetailsText => !Exists
        ? Localizer.T("attachment.unavailable")
        : _dimensions is { } size
            ? $"{size.Width}×{size.Height} · {SizeText}"
            : SizeText;

    private string SizeText => _size is { } bytes ? FormatSize(bytes) : Localizer.T("attachment.sizeUnavailable");

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
        CancelRefresh();
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

    /// <summary>Starts work only while a list container is realized.</summary>
    public void Activate()
    {
        if (_disposed || _active)
            return;
        _active = true;
        Refresh();
    }

    public void Deactivate()
    {
        _active = false;
        CancelRefresh();
        ClearThumbnail();
    }

    public void ClearOpenResult()
    {
        if (_disposed)
            return;
        if (Result?.Kind is OperationResultKind.Warning or OperationResultKind.Error)
            Result = null;
        // Opening can reveal that an existing source has changed. Always consult its current
        // fingerprint, even when a preview is already present.
        if (_active)
            Refresh();
    }

    private void Refresh()
    {
        CancelRefresh();
        var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        var work = LoadAttachmentAsync(_thumbnailGeneration, cancellation);
        // A canceled caller still owns any native or filesystem work already in flight.
        ThumbnailTask = ThumbnailTask.IsCompleted ? work : Task.WhenAll(ThumbnailTask, work);
    }

    private void CancelRefresh()
    {
        _thumbnailGeneration++;
        _refreshCancellation?.Cancel();
    }

    private bool IsCurrent(int generation) => !_disposed && _active && generation == _thumbnailGeneration;

    private async Task LoadAttachmentAsync(int generation, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            long? size = null;
            var available = true;
            try
            {
                await MetadataAdmission.WaitAsync(token);
                try
                {
                    size = await Task.Run(() => new FileInfo(FullPath).Length, token);
                }
                finally
                {
                    MetadataAdmission.Release();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                available = ex is not (FileNotFoundException or DirectoryNotFoundException);
                _log.Warn("Could not read attachment metadata", new { path = FullPath }, ex);
            }

            if (!IsCurrent(generation))
                return;
            _size = size;
            Exists = available;
            OnPropertyChanged(nameof(DetailsText));
            if (!available)
            {
                ShowUnavailable();
                return;
            }
            if (Result is { Kind: OperationResultKind.Warning, Message.Key: UnavailableKey })
                Result = null;
            if (!IsImage)
                return;

            var (bitmap, dimensions) = await _decodeThumbnail(FullPath, token);
            if (!IsCurrent(generation))
            {
                bitmap.Dispose();
                return;
            }
            ClearThumbnail();
            Thumbnail = bitmap;
            _dimensions = dimensions;
            OnPropertyChanged(nameof(DetailsText));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _log.Warn("Could not decode attachment thumbnail", new { path = FullPath }, ex);
            if (IsCurrent(generation))
                ClearThumbnail();
        }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, cancellation))
                _refreshCancellation = null;
            cancellation.Dispose();
        }
    }

    private void ClearThumbnail()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
        _dimensions = null;
        OnPropertyChanged(nameof(DetailsText));
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
        Deactivate();
    }
}
