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
/// <remarks>
/// The memory cache is bounded (least recently used pictures are dropped first): scrolling through the whole
/// Browse list or paging through full-size screenshots would otherwise keep every decoded picture alive.
/// Dropped bitmaps are not disposed, since a view may still show them; they are freed once nothing uses them,
/// and come back from the disk cache when needed again.
/// </remarks>
public sealed class ImageCache(HttpClient http) : IImageCache
{
    private static readonly string Folder = Path.Combine(AppPaths.CacheFolder, "Images");
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Decoded pixels kept in memory (BGRA, 4 bytes per pixel).</summary>
    private const long MemoryBudgetBytes = 160L * 1024 * 1024;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _memory = new(StringComparer.Ordinal);
    /// <summary>Most recently used first.</summary>
    private readonly LinkedList<Entry> _recent = new();
    private long _memoryBytes;

    private readonly SemaphoreSlim _downloads = new(6, 6);

    public Task<Bitmap?> GetAsync(string url, int decodeSize)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Task.FromResult<Bitmap?>(null);

        var key = $"{decodeSize}|{url}";
        Entry entry;
        lock (_lock)
        {
            if (_memory.TryGetValue(key, out var existing))
            {
                _recent.Remove(existing.Node);
                _recent.AddFirst(existing.Node);
                return existing.Task;
            }

            entry = new Entry(key);
            _recent.AddFirst(entry.Node);
            _memory[key] = entry;

            // Always off the UI thread: icons are requested while the list scrolls, and a picture already on disk
            // would otherwise be read and resized (high quality) on the UI thread before the first real await.
            entry.Task = Task.Run(() => LoadAsync(url, decodeSize));
        }

        _ = entry.Task.ContinueWith(task => OnLoaded(entry, task.Result), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
        return entry.Task;
    }

    /// <summary>Accounts for a decoded picture and drops the least recently used ones past the budget.</summary>
    private void OnLoaded(Entry entry, Bitmap? bitmap)
    {
        lock (_lock)
        {
            // Already dropped while it was loading.
            if (!_memory.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry))
                return;

            entry.Size = bitmap is null ? 0 : (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
            entry.IsLoaded = true;
            _memoryBytes += entry.Size;

            var node = _recent.Last;
            while (_memoryBytes > MemoryBudgetBytes && node is not null)
            {
                var previous = node.Previous;
                var candidate = node.Value;
                // Pictures still loading are not counted yet; the newest picture always stays.
                if (candidate.IsLoaded && !ReferenceEquals(candidate, entry))
                {
                    _recent.Remove(node);
                    _memory.Remove(candidate.Key);
                    _memoryBytes -= candidate.Size;
                }

                node = previous;
            }
        }
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

    private sealed class Entry
    {
        public Entry(string key)
        {
            Key = key;
            Node = new LinkedListNode<Entry>(this);
        }

        public string Key { get; }
        public LinkedListNode<Entry> Node { get; }
        public Task<Bitmap?> Task { get; set; } = null!;
        public long Size { get; set; }
        public bool IsLoaded { get; set; }
    }
}
