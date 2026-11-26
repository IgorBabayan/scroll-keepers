using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using ScrollKeeper.Services.Settings;
using SkiaSharp;

namespace ScrollKeeper.Services.Images;

/// <summary>
/// Disk cache in <c>%LOCALAPPDATA%\ScrollKeeper\Cache\Images</c> (<c>~/.local/share</c> on Linux,
/// <c>~/Library/Application Support</c> on macOS) plus an in-memory cache of decoded bitmaps (immutable, safe to use
/// from any thread). Downloads are limited to a few at a time so scrolling
/// through thousands of addons does not flood ESOUI.
/// </summary>
public sealed class ImageCache(HttpClient http) : IImageCache
{
    private static readonly string Folder = Path.Combine(AppPaths.CacheFolder, "Images");
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Lazy<Task<Bitmap?>>> _memory = new();
    private readonly SemaphoreSlim _downloads = new(6, 6);

    public Task<Bitmap?> GetAsync(string url, int decodeSize)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Task.FromResult<Bitmap?>(null);

        var key = $"{decodeSize}|{url}";
        return _memory.GetOrAdd(key, _ => new Lazy<Task<Bitmap?>>(() => LoadAsync(url, decodeSize))).Value;
    }

    private async Task<Bitmap?> LoadAsync(string url, int decodeSize)
    {
        try
        {
            var bytes = await GetBytesAsync(url).ConfigureAwait(false);
            return bytes is null ? null : Decode(bytes, decodeSize);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                       or OperationCanceledException or NotSupportedException or ArgumentException
                                       or InvalidOperationException or InvalidDataException)
        {
            Debug.WriteLine($"Image {url} failed: {ex.Message}");
            return null;
        }
    }

    private async Task<byte[]?> GetBytesAsync(string url)
    {
        var path = Path.Combine(Folder, Hash(url));
        if (File.Exists(path))
            return await File.ReadAllBytesAsync(path).ConfigureAwait(false);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return null;

        await _downloads.WaitAsync().ConfigureAwait(false);
        try
        {
            using var timeout = new CancellationTokenSource(DownloadTimeout);
            using var response = await http.GetAsync(uri, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);

            try
            {
                Directory.CreateDirectory(Folder);
                await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Two windows may cache the same image at once; the image itself is fine.
                Debug.WriteLine($"Could not cache {url}: {ex.Message}");
            }

            return bytes;
        }
        finally
        {
            _downloads.Release();
        }
    }

    private static Bitmap Decode(byte[] bytes, int decodeSize)
    {
        using var stream = new MemoryStream(bytes);

        // Pictures are drawn with UniformToFill, so it is the shorter side that must stay sharp.
        // Scaling by width alone made wide screenshots too short and they were blown up again on screen.
        // Smaller images are left as they are: upscaling while decoding adds nothing.
        if (decodeSize > 0 && ReadSize(bytes) is var (width, height) && Math.Min(width, height) > decodeSize)
        {
            return width <= height
                ? Bitmap.DecodeToWidth(stream, decodeSize, BitmapInterpolationMode.HighQuality)
                : Bitmap.DecodeToHeight(stream, decodeSize, BitmapInterpolationMode.HighQuality);
        }

        return new Bitmap(stream);
    }

    /// <summary>Reads the pixel size from the header without decoding the picture.</summary>
    private static (int Width, int Height) ReadSize(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data)
                          ?? throw new InvalidDataException("The picture format is not supported.");
        return (codec.Info.Width, codec.Info.Height);
    }

    private static string Hash(string url) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
}
