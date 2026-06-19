using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.Core.Models;
using DayNote.Desktop.Logging;

namespace DayNote.Desktop.ViewModels;

/// <summary>
/// A row in the attachments pane. Images are decoded to a bounded thumbnail; other files and failed
/// previews use the view's generic document placeholder.
/// </summary>
public sealed partial class AttachmentItemViewModel : ObservableObject, IDisposable
{
    private const int ThumbnailWidth = 240;

    private readonly IAppLogger _log;
    private bool _disposed;

    public AttachmentItemViewModel(Attachment attachment, IAppLogger log)
    {
        _log = log;
        Attachment = attachment;
        FileName = attachment.FileName;
        FullPath = attachment.FullPath;
        IsImage = attachment.IsImage;
        Exists = File.Exists(attachment.FullPath);

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
            // Decode off the UI thread so selecting an image-heavy note does not block the UI;
            // the continuation resumes on the UI thread, where assigning Thumbnail is binding-safe.
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, ThumbnailWidth);
            });

            if (_disposed)
            {
                bitmap.Dispose();
                return;
            }

            Thumbnail = bitmap;
        }
        catch (Exception ex)
        {
            // A corrupt or unsupported image is recoverable — the tile just shows no preview — but it
            // is unexpected for a file we classified as an image, so it is recorded rather than swallowed.
            _log.Warn("Could not decode attachment thumbnail", new { path }, ex);
            Thumbnail = null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}
