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

    public Task<ImageSource?> GetAsync(string url, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Task.FromResult<ImageSource?>(null);

        var key = $"{decodeWidth}|{url}";
        return _memory.GetOrAdd(key, _ => new Lazy<Task<ImageSource?>>(() => LoadAsync(url, decodeWidth))).Value;
    }

    private async Task<ImageSource?> LoadAsync(string url, int decodeWidth)
    {
        try
        {
            var bytes = await GetBytesAsync(url).ConfigureAwait(false);
            return bytes is null ? null : Decode(bytes, decodeWidth);
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

    private static ImageSource Decode(byte[] bytes, int decodeWidth)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (decodeWidth > 0)
            image.DecodePixelWidth = decodeWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string Hash(string url) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
}
