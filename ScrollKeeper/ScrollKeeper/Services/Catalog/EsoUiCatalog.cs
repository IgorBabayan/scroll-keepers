using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScrollKeeper.Services.Settings;

namespace ScrollKeeper.Services.Catalog;

/// <summary>
/// Reads the ESOUI (MMOUI) v3 API, the addon source Minion uses:
/// <c>globalconfig.json</c> → <c>game/ESO/gameconfig.json</c> → file list, file details and category list feeds.
/// The last downloaded lists are kept in <c>%LOCALAPPDATA%\ScrollKeeper\Cache</c> for offline starts.
/// </summary>
public sealed class EsoUiCatalog(HttpClient http) : IAddonCatalog
{
    private const string GameId = "ESO";
    private const string GlobalConfigUrl = "https://api.mmoui.com/v3/globalconfig.json";

    // Used when the config documents cannot be read.
    private const string DefaultFileListUrl = "https://api.mmoui.com/v3/game/ESO/filelist.json";
    private const string DefaultFileDetailsUrl = "https://api.mmoui.com/v3/game/ESO/filedetails/";
    private const string DefaultCategoryListUrl = "https://api.mmoui.com/v3/game/ESO/categorylist.json";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private static readonly string FileListCachePath = Path.Combine(AppPaths.CacheFolder, "filelist.json");
    private static readonly string CategoryListCachePath = Path.Combine(AppPaths.CacheFolder, "categorylist.json");

    private readonly SemaphoreSlim _feedsLock = new(1, 1);
    private Feeds? _feeds;

    public async Task<AddonCatalogSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var feeds = await GetFeedsAsync(cancellationToken).ConfigureAwait(false);

            var fileListTask = GetBytesAsync(feeds.FileList, cancellationToken);
            var categoryListTask = GetBytesAsync(feeds.CategoryList, cancellationToken);
            var fileList = await fileListTask.ConfigureAwait(false);
            byte[]? categoryList;
            try
            {
                categoryList = await categoryListTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNetworkError(ex))
            {
                // Categories only add titles and fallback icons.
                Debug.WriteLine($"Category list failed: {ex.Message}");
                categoryList = TryReadCache(CategoryListCachePath);
            }

            var snapshot = Parse(fileList, categoryList, isFromCache: false);

            TryWriteCache(FileListCachePath, fileList);
            if (categoryList is not null)
                TryWriteCache(CategoryListCachePath, categoryList);

            return snapshot;
        }
        catch (Exception ex) when (IsNetworkError(ex) && !cancellationToken.IsCancellationRequested)
        {
            var cachedList = TryReadCache(FileListCachePath);
            if (cachedList is null)
                throw;

            Debug.WriteLine($"ESOUI unreachable, using the cached catalog: {ex.Message}");
            return Parse(cachedList, TryReadCache(CategoryListCachePath), isFromCache: true);
        }
    }

    public async Task<AddonDetails> GetDetailsAsync(string addonId, CancellationToken cancellationToken = default)
    {
        // Ids are numeric; anything else must not end up in the URL.
        if (string.IsNullOrEmpty(addonId) || !addonId.All(char.IsAsciiDigit))
            throw new ArgumentException($"Invalid addon id '{addonId}'.", nameof(addonId));

        var feeds = await GetFeedsAsync(cancellationToken).ConfigureAwait(false);
        var bytes = await GetBytesAsync($"{feeds.FileDetails}{addonId}.json", cancellationToken).ConfigureAwait(false);
        return ParseDetails(bytes, addonId);
    }

    // ===== Parsing (internal for tests) =====

    internal static AddonCatalogSnapshot Parse(byte[] fileListJson, byte[]? categoryListJson, bool isFromCache)
    {
        ThrowIfApiError(fileListJson);
        var files = JsonSerializer.Deserialize<List<FileListDto>>(fileListJson) ?? [];

        var addons = files
            .Where(f => !string.IsNullOrWhiteSpace(f.Id) && !string.IsNullOrWhiteSpace(f.Name))
            .Select(ToCatalogAddon)
            .ToList();

        var categories = new Dictionary<string, CatalogCategory>(StringComparer.Ordinal);
        if (categoryListJson is not null)
        {
            try
            {
                foreach (var c in JsonSerializer.Deserialize<List<CategoryDto>>(categoryListJson) ?? [])
                {
                    if (string.IsNullOrWhiteSpace(c.Id))
                        continue;

                    categories[c.Id] = new CatalogCategory(
                        c.Id,
                        Clean(c.Title) ?? c.Id,
                        AbsoluteUrlOrNull(c.Icon),
                        c.ParentIds ?? []);
                }
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"Category list is invalid: {ex.Message}");
            }
        }

        return new AddonCatalogSnapshot(addons, categories, isFromCache);
    }

    internal static AddonDetails ParseDetails(byte[] json, string addonId)
    {
        ThrowIfApiError(json);
        var dto = (JsonSerializer.Deserialize<List<FileDetailsDto>>(json) ?? []).FirstOrDefault()
                  ?? throw new InvalidDataException($"ESOUI returned no details for addon {addonId}.");

        return new AddonDetails(
            dto.Id ?? addonId,
            Clean(dto.Version) ?? string.Empty,
            dto.FileName,
            AbsoluteUrlOrNull(dto.Download),
            dto.Description ?? string.Empty,
            dto.ChangeLog ?? string.Empty,
            FlexibleParse.ToInt64(dto.HitCount),
            FlexibleParse.ToInt64(dto.HitCountMonthly),
            dto.Pending is "1" or "true");
    }

    private static CatalogAddon ToCatalogAddon(FileListDto f) => new(
        f.Id!.Trim(),
        f.CategoryId?.Trim() ?? string.Empty,
        Clean(f.Name)!,
        Clean(f.Author) ?? string.Empty,
        Clean(f.Version) ?? string.Empty,
        FlexibleParse.ToDate(f.Date),
        AbsoluteUrlOrNull(f.FileInfoUrl),
        FlexibleParse.ToInt64(f.DownloadTotal),
        FlexibleParse.ToInt64(f.DownloadMonthly),
        FlexibleParse.ToInt64(f.FavoriteTotal),
        (f.Directories ?? []).Select(d => d.Trim().Trim('/', '\\')).Where(d => d.Length > 0).ToList(),
        (f.Thumbnails ?? []).Select(AbsoluteUrlOrNull).OfType<string>().ToList(),
        (f.Images ?? []).Select(AbsoluteUrlOrNull).OfType<string>().ToList(),
        (f.Compatibility ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Version))
            .Select(c => new GameCompatibility(c.Version!.Trim(), Clean(c.Name) ?? string.Empty))
            .ToList());

    /// <summary>The API answers <c>{"ERROR": "..."}</c> instead of a list on failures.</summary>
    private static void ThrowIfApiError(byte[] json)
    {
        var reader = new Utf8JsonReader(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return;

        using var document = JsonDocument.Parse(json);
        var message = document.RootElement.TryGetProperty("ERROR", out var error) ? error.ToString() : "unexpected response";
        throw new InvalidDataException($"ESOUI API error: {message}");
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return System.Net.WebUtility.HtmlDecode(value).Trim();
    }

    private static string? AbsoluteUrlOrNull(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : null;

    // ===== Network =====

    private async Task<Feeds> GetFeedsAsync(CancellationToken cancellationToken)
    {
        if (_feeds is not null)
            return _feeds;

        await _feedsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_feeds is not null)
                return _feeds;

            Feeds feeds;
            try
            {
                var global = JsonSerializer.Deserialize<GlobalConfigDto>(
                    await GetBytesAsync(GlobalConfigUrl, cancellationToken).ConfigureAwait(false));
                var gameConfigUrl = global?.Games?.FirstOrDefault(g => g.GameId == GameId)?.GameConfig;
                var game = gameConfigUrl is null
                    ? null
                    : JsonSerializer.Deserialize<GameConfigDto>(
                        await GetBytesAsync(gameConfigUrl, cancellationToken).ConfigureAwait(false));

                feeds = new Feeds(
                    AbsoluteUrlOrNull(game?.ApiFeeds?.FileList) ?? DefaultFileListUrl,
                    AbsoluteUrlOrNull(game?.ApiFeeds?.FileDetails) ?? DefaultFileDetailsUrl,
                    AbsoluteUrlOrNull(game?.ApiFeeds?.CategoryList) ?? DefaultCategoryListUrl);
            }
            catch (Exception ex) when (ex is JsonException || (IsNetworkError(ex) && !cancellationToken.IsCancellationRequested))
            {
                // The well-known v3 endpoints have been stable for years: try them directly.
                Debug.WriteLine($"ESOUI config failed, using default endpoints: {ex.Message}");
                return new Feeds(DefaultFileListUrl, DefaultFileDetailsUrl, DefaultCategoryListUrl);
            }

            if (!feeds.FileDetails.EndsWith('/'))
                feeds = feeds with { FileDetails = feeds.FileDetails + "/" };

            return _feeds = feeds;
        }
        finally
        {
            _feedsLock.Release();
        }
    }

    private async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        using var response = await http.GetAsync(url, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
    }

    internal static bool IsNetworkError(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException or JsonException;

    private static byte[]? TryReadCache(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryWriteCache(string path, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not cache {path}: {ex.Message}");
        }
    }

    private sealed record Feeds(string FileList, string FileDetails, string CategoryList);

    // ===== ESOUI API payloads (only the fields we use) =====

    private sealed class GlobalConfigDto
    {
        [JsonPropertyName("GAMES")] public List<GameDto>? Games { get; set; }
    }

    private sealed class GameDto
    {
        [JsonPropertyName("GameID")] public string? GameId { get; set; }
        [JsonPropertyName("GameConfig")] public string? GameConfig { get; set; }
    }

    private sealed class GameConfigDto
    {
        [JsonPropertyName("APIFeeds")] public ApiFeedsDto? ApiFeeds { get; set; }
    }

    private sealed class ApiFeedsDto
    {
        [JsonPropertyName("FileList")] public string? FileList { get; set; }
        [JsonPropertyName("FileDetails")] public string? FileDetails { get; set; }
        [JsonPropertyName("CategoryList")] public string? CategoryList { get; set; }
    }

    private sealed class FileListDto
    {
        [JsonPropertyName("UID"), JsonConverter(typeof(FlexibleStringConverter))] public string? Id { get; set; }
        [JsonPropertyName("UICATID"), JsonConverter(typeof(FlexibleStringConverter))] public string? CategoryId { get; set; }
        [JsonPropertyName("UIVersion"), JsonConverter(typeof(FlexibleStringConverter))] public string? Version { get; set; }
        [JsonPropertyName("UIDate"), JsonConverter(typeof(FlexibleStringConverter))] public string? Date { get; set; }
        [JsonPropertyName("UIName"), JsonConverter(typeof(FlexibleStringConverter))] public string? Name { get; set; }
        [JsonPropertyName("UIAuthorName"), JsonConverter(typeof(FlexibleStringConverter))] public string? Author { get; set; }
        [JsonPropertyName("UIFileInfoURL"), JsonConverter(typeof(FlexibleStringConverter))] public string? FileInfoUrl { get; set; }
        [JsonPropertyName("UIDownloadTotal"), JsonConverter(typeof(FlexibleStringConverter))] public string? DownloadTotal { get; set; }
        [JsonPropertyName("UIDownloadMonthly"), JsonConverter(typeof(FlexibleStringConverter))] public string? DownloadMonthly { get; set; }
        [JsonPropertyName("UIFavoriteTotal"), JsonConverter(typeof(FlexibleStringConverter))] public string? FavoriteTotal { get; set; }
        [JsonPropertyName("UIDir"), JsonConverter(typeof(FlexibleStringListConverter))] public List<string>? Directories { get; set; }
        [JsonPropertyName("UIIMG_Thumbs"), JsonConverter(typeof(FlexibleStringListConverter))] public List<string>? Thumbnails { get; set; }
        [JsonPropertyName("UIIMGs"), JsonConverter(typeof(FlexibleStringListConverter))] public List<string>? Images { get; set; }
        [JsonPropertyName("UICompatibility")] public List<CompatibilityDto>? Compatibility { get; set; }
    }

    private sealed class CompatibilityDto
    {
        [JsonPropertyName("version"), JsonConverter(typeof(FlexibleStringConverter))] public string? Version { get; set; }
        [JsonPropertyName("name"), JsonConverter(typeof(FlexibleStringConverter))] public string? Name { get; set; }
    }

    private sealed class CategoryDto
    {
        [JsonPropertyName("UICATID"), JsonConverter(typeof(FlexibleStringConverter))] public string? Id { get; set; }
        [JsonPropertyName("UICATTitle"), JsonConverter(typeof(FlexibleStringConverter))] public string? Title { get; set; }
        [JsonPropertyName("UICATICON"), JsonConverter(typeof(FlexibleStringConverter))] public string? Icon { get; set; }
        [JsonPropertyName("UICATParentIDs"), JsonConverter(typeof(FlexibleStringListConverter))] public List<string>? ParentIds { get; set; }
    }

    private sealed class FileDetailsDto
    {
        [JsonPropertyName("UID"), JsonConverter(typeof(FlexibleStringConverter))] public string? Id { get; set; }
        [JsonPropertyName("UIVersion"), JsonConverter(typeof(FlexibleStringConverter))] public string? Version { get; set; }
        [JsonPropertyName("UIFileName"), JsonConverter(typeof(FlexibleStringConverter))] public string? FileName { get; set; }
        [JsonPropertyName("UIDownload"), JsonConverter(typeof(FlexibleStringConverter))] public string? Download { get; set; }
        [JsonPropertyName("UIDescription"), JsonConverter(typeof(FlexibleStringConverter))] public string? Description { get; set; }
        [JsonPropertyName("UIChangeLog"), JsonConverter(typeof(FlexibleStringConverter))] public string? ChangeLog { get; set; }
        [JsonPropertyName("UIHitCount"), JsonConverter(typeof(FlexibleStringConverter))] public string? HitCount { get; set; }
        [JsonPropertyName("UIHitCountMonthly"), JsonConverter(typeof(FlexibleStringConverter))] public string? HitCountMonthly { get; set; }
        [JsonPropertyName("UIPending"), JsonConverter(typeof(FlexibleStringConverter))] public string? Pending { get; set; }
    }
}
