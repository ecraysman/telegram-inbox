using System.Text.Json;
using Inbox.Infrastructure.Serialization;

namespace Inbox.Api.Endpoints;

internal static class JsonBody
{
    /// <summary>Reads a JSON body; returns <c>null</c> when it is missing, not JSON or malformed (mapped to 400 by callers).</summary>
    public static async Task<T?> TryReadAsync<T>(HttpRequest request, CancellationToken cancellationToken)
        where T : class
    {
        if (!request.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await request.ReadFromJsonAsync<T>(InboxJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
