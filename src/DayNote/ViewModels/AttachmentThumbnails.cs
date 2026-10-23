using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DayNote.Logging;
using SkiaSharp;

namespace DayNote.ViewModels;

/// <summary>
/// An image attachment's thumbnail, made once and kept on disk, so showing a row never decodes the
/// original again (volume-handling-conventions, Pixels). The thumbnail's longest edge is 360 px, which
/// stays sharp in its row on high-density displays, stored as WebP at quality 75, a few kilobytes each,
/// under <see cref="DayNote.Core.Storage.AppPaths.ThumbnailsDirectory"/>.
///
/// A thumbnail is named after the file it came from: the SHA-256 of its full path, size and modified
/// time, plus <see cref="FormatVersion"/>, so a replaced or edited file gets a new one. A hash of the
/// bytes would need each image read whole every time a note is shown, since DayNote records no file
/// hashes. A file replaced with the same
/// size and modified time keeps the old picture, which is only cosmetic. Nothing sweeps old thumbnails:
/// disk use is out of scope (developer decision), and each is a few kilobytes.
///
/// Making one decodes the original once. JPEG and WebP decode at a reduced size; PNG and GIF decode
/// whole, since Skia scales neither. A decode that fails, as when its memory cannot be allocated, leaves
/// the row on its file icon, and the next showing tries again.
/// </summary>
internal static class AttachmentThumbnails
{
    internal const int Size = 360;
    internal const int FormatVersion = 1;
    private const int Quality = 75;

    // Full native decodes can allocate the source dimensions (PNG/GIF). Only that physical work
    // occupies this slot; cached previews and header reads remain available while it runs.
    internal static readonly SemaphoreSlim DecodeAdmission = new(1, 1);

    private static readonly SemaphoreSlim PreparationAdmission = new(Math.Max(2, Environment.ProcessorCount));

    /// <summary>The thumbnail of <paramref name="path"/> and the image's own dimensions.</summary>
    internal static async Task<(Bitmap Bitmap, PixelSize Size)> LoadAsync(string path, string directory,
        IAppLogger log, CancellationToken cancellationToken = default)
    {
        // Admission happens before dispatch: stalled source/header reads cannot occupy an unbounded
        // number of workers as rows are scrolled away and back. Cached loads share this light budget.
        await PreparationAdmission.WaitAsync(cancellationToken).ConfigureAwait(false);
        (SKCodec Codec, PixelSize Original, string Kept, Bitmap? Cached) prepared;
        try
        {
            prepared = await Task.Run(() => Prepare(path, directory, log), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            PreparationAdmission.Release();
        }

        using var codec = prepared.Codec;
        if (prepared.Cached is { } cached)
            return (cached, prepared.Original);

        // Release preparation capacity before waiting for a native decode, so cached rows can load.
        await DecodeAdmission.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            SKBitmap made;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                made = Make(codec, prepared.Original);
            }
            finally
            {
                DecodeAdmission.Release();
            }
            using var owned = made;
            Keep(made, prepared.Kept, log);
            return (new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, made.GetPixels(),
                new PixelSize(made.Width, made.Height), new Vector(96, 96), made.RowBytes), prepared.Original);
        }).ConfigureAwait(false);
    }

    private static (SKCodec Codec, PixelSize Original, string Kept, Bitmap? Cached) Prepare(
        string path, string directory, IAppLogger log)
    {
        var file = new FileInfo(path);
        file.Refresh();
        // The codec reads only the header here and owns its source stream until the decode settles.
        var codec = SKCodec.Create(File.OpenRead(path))
            ?? throw new InvalidDataException("Image codec could not be opened.");
        try
        {
            var original = new PixelSize(codec.Info.Width, codec.Info.Height);
            var kept = Path.Combine(directory, NameFor(file));
            if (File.Exists(kept))
            {
                try
                {
                    return (codec, original, kept, new Bitmap(kept));
                }
                catch (Exception ex)
                {
                    // A damaged thumbnail is made again from the original.
                    log.Warn("Could not read a kept attachment thumbnail", new { path = kept }, ex);
                }
            }
            return (codec, original, kept, null);
        }
        catch
        {
            codec.Dispose();
            throw;
        }
    }

    /// <summary>The file name a thumbnail of <paramref name="file"/> is kept under.</summary>
    internal static string NameFor(FileInfo file)
    {
        var identity = $"{Path.GetFullPath(file.FullName)}\n{file.Length}\n{file.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return $"{hash}-v{FormatVersion}.webp";
    }

    private static SKBitmap Make(SKCodec codec, PixelSize original)
    {
        var scale = Math.Min(1d, Size / (double)Math.Max(original.Width, original.Height));
        var decodedSize = codec.GetScaledDimensions((float)scale);
        var size = new PixelSize(
            Math.Max(1, (int)Math.Round(original.Width * scale)),
            Math.Max(1, (int)Math.Round(original.Height * scale)));
        var info = new SKImageInfo(decodedSize.Width, decodedSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(info);
        if (codec.GetPixels(info, decoded.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("Image pixels could not be decoded.");
        return decoded.Resize(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKFilterMode.Linear)) ?? throw new InvalidDataException("Image preview could not be resized.");
    }

    // Written whole under a temporary name and renamed, so a kept thumbnail is never half written. A
    // thumbnail that cannot be kept is still shown, and is made again next time.
    private static void Keep(SKBitmap thumbnail, string kept, IAppLogger log)
    {
        var temporary = kept + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
            using (var image = SKImage.FromBitmap(thumbnail))
            using (var encoded = image.Encode(SKEncodedImageFormat.Webp, Quality) ?? throw new InvalidDataException("Thumbnail could not be encoded."))
            using (var stream = File.Create(temporary))
            {
                encoded.SaveTo(stream);
            }

            File.Move(temporary, kept, overwrite: true);
        }
        catch (Exception ex)
        {
            log.Warn("Could not keep an attachment thumbnail", new { path = kept }, ex);
            try
            {
                File.Delete(temporary);
            }
            catch (Exception)
            {
                // What could not be written leaves at most a temporary file in the thumbnails folder.
            }
        }
    }
}
