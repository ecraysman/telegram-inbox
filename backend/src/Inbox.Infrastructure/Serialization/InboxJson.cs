using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inbox.Infrastructure.Serialization;

/// <summary>
/// The single source of JSON settings (contracts §7.1): web defaults (camelCase) plus camelCase string enums.
/// Used by HTTP, SignalR and every RabbitMQ serializer.
/// </summary>
public static class InboxJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Applies the inbox conventions to an options instance owned by a framework (HTTP JSON, SignalR protocol).</summary>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
