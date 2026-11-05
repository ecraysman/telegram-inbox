using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inbox.Application.Abstractions;
using Inbox.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;

namespace Inbox.Infrastructure.Ngrok;

/// <summary>Discovers the https tunnel through the ngrok agent API (<c>GET /api/tunnels</c>). Returns <c>null</c> until it exists.</summary>
internal sealed partial class NgrokPublicUrlProvider(HttpClient http, ILogger<NgrokPublicUrlProvider> logger) : IPublicUrlProvider
{
    public async Task<Uri?> GetPublicUrlAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.GetFromJsonAsync<TunnelList>("api/tunnels", InboxJson.Options, cancellationToken);
            var https = response?.Tunnels?.FirstOrDefault(t =>
                t.PublicUrl is not null && t.PublicUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
            if (https?.PublicUrl is null)
            {
                LogNoTunnel(logger);
                return null;
            }

            return new Uri(https.PublicUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnreachable(logger, ex);
            return null;
        }
    }

    private sealed record TunnelList([property: JsonPropertyName("tunnels")] IReadOnlyList<Tunnel>? Tunnels);

    private sealed record Tunnel(
        [property: JsonPropertyName("public_url")] string? PublicUrl,
        [property: JsonPropertyName("proto")] string? Proto);

    [LoggerMessage(Level = LogLevel.Information, Message = "ngrok is up but has no https tunnel yet")]
    private static partial void LogNoTunnel(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ngrok agent API not reachable yet")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);
}
