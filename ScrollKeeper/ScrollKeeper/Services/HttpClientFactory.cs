using System.Net;
using System.Net.Http;
using System.Reflection;

namespace ScrollKeeper.Services;

/// <summary>Creates the HTTP client shared by the catalog, the addon installer and the image cache.</summary>
public static class HttpClientFactory
{
    public static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            // Pick up DNS changes in a long-running session.
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 8
        };

        // Timeouts are set per call with cancellation tokens (archives may take a while).
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Mozilla/5.0 (Windows NT 10.0; Win64; x64) ScrollKeeper/{version}");
        return client;
    }
}
