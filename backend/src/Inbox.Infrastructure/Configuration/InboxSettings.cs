using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Inbox.Infrastructure.Configuration;

/// <summary>
/// Reads required configuration and fails fast when a key is missing or malformed (contracts §6).
/// Errors name the key in its environment-variable form and never include the value.
/// </summary>
public static partial class InboxSettings
{
    public static string Required(IConfiguration configuration, string key, Regex? format = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value) || (format is not null && !format.IsMatch(value)))
        {
            throw new InvalidOperationException($"Configuration key '{ToEnvironmentName(key)}' is missing or malformed.");
        }

        return value;
    }

    public static int OptionalPositiveInt(IConfiguration configuration, string key, int defaultValue)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new InvalidOperationException($"Configuration key '{ToEnvironmentName(key)}' is malformed (expected a positive integer).");
    }

    public static Uri RequiredAbsoluteUri(IConfiguration configuration, string key) =>
        Uri.TryCreate(Required(configuration, key), UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException($"Configuration key '{ToEnvironmentName(key)}' is missing or malformed.");

    [GeneratedRegex("^[0-9]+:[A-Za-z0-9_-]+$")]
    public static partial Regex BotTokenFormat();

    [GeneratedRegex("^[A-Za-z0-9_-]{16,256}$")]
    public static partial Regex WebhookSecretFormat();

    [GeneratedRegex("^[0-9]{1,5}$")]
    public static partial Regex PortFormat();

    private static string ToEnvironmentName(string key) => key.Replace(":", "__", StringComparison.Ordinal);
}
