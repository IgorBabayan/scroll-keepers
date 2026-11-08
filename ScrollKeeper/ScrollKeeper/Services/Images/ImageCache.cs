using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScrollKeeper.Services.Settings;

namespace ScrollKeeper.Services.Images;

/// <summary>
/// Disk cache in <c>%LOCALAPPDATA%\ScrollKeeper\Cache\Images</c> plus an in-memory cache of decoded,
/// frozen bitmaps (safe to use from any thread). Downloads are limited to a few at a time so scrolling
/// through thousands of addons does not flood ESOUI.
/// </summary>
public sealed class ImageCache(HttpClient http) : IImageCache
{
    private static readonly string Folder = Path.Combine(AppPaths.CacheFolder, "Images");
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> _memory = new();
    private readonly SemaphoreSlim _downloads = new(6, 6);

    public Task<ImageSource?> GetAsync(string url, int decodeSize)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Task.FromResult<ImageSource?>(null);

        var key = $"{decodeSize}|{url}";
        return _memory.GetOrAdd(key, _ => new Lazy<Task<ImageSource?>>(() => LoadAsync(url, decodeSize))).Value;
    }

    private async Task<ImageSource?> LoadAsync(string url, int decodeSize)
    {
        try
        {
            var bytes = await GetBytesAsync(url).ConfigureAwait(false);
            return bytes is null ? null : Decode(bytes, decodeSize);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                       or OperationCanceledException or NotSupportedException or ArgumentException
                                       or InvalidOperationException or FileFormatException)
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

    private static ImageSource Decode(byte[] bytes, int decodeSize)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;

        // Pictures are drawn with UniformToFill, so it is the shorter side that must stay sharp.
        // Scaling by width alone made wide screenshots too short and they were blown up again on screen.
        // Smaller images are left as they are: upscaling while decoding adds nothing.
        if (decodeSize > 0)
        {
            var (width, height) = ReadSize(bytes);
            if (Math.Min(width, height) > decodeSize)
            {
                if (width <= height)
                    image.DecodePixelWidth = decodeSize;
                else
                    image.DecodePixelHeight = decodeSize;
            }
        }

        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>Reads the pixel size from the header without decoding the picture.</summary>
    private static (int Width, int Height) ReadSize(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream,
            BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private static string Hash(string url) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
}
