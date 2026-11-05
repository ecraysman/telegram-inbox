using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inbox.Application.Abstractions;
using Inbox.Infrastructure.Serialization;
using Microsoft.Extensions.Options;

namespace Inbox.Infrastructure.Telegram;

/// <summary>
/// Typed Bot API client. One attempt per call; the caller owns the retry policy. Responses are classified per contracts §7.6.
/// The bot token is part of every URL, so HttpClient's built-in request logging is removed at registration and
/// no URL is ever logged or put in an exception message.
/// </summary>
internal sealed class TelegramGateway(HttpClient http, IOptions<TelegramOptions> options) : ITelegramGateway
{
    private static readonly string[] AllowedUpdates = ["message"];

    public async Task<SendMessageResult> SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(MethodPath("sendMessage"), new SendMessageRequest(chatId, text), InboxJson.Options, cancellationToken);
            var body = await ReadAsync<SentMessage>(response, cancellationToken);

            if (response.IsSuccessStatusCode && body is { Ok: true, Result: { } sent })
            {
                return new SendMessageResult.Delivered(sent.MessageId, DateTimeOffset.FromUnixTimeSeconds(sent.Date));
            }

            var description = Describe(response.StatusCode, body);
            return response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => new SendMessageResult.Transient(
                    description,
                    body?.Parameters?.RetryAfter is { } seconds ? TimeSpan.FromSeconds(seconds) : null),
                >= HttpStatusCode.InternalServerError => new SendMessageResult.Transient(description, null),
                _ => new SendMessageResult.Rejected(description),
            };
        }
        catch (HttpRequestException ex)
        {
            return new SendMessageResult.Transient($"network error ({ex.HttpRequestError})", null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SendMessageResult.Transient("timeout", null);
        }
    }

    public async Task SetWebhookAsync(Uri url, string secretToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        var request = new SetWebhookRequest(url.AbsoluteUri, secretToken, AllowedUpdates, DropPendingUpdates: false);
        using var response = await http.PostAsJsonAsync(MethodPath("setWebhook"), request, InboxJson.Options, cancellationToken);
        var body = await ReadAsync<bool>(response, cancellationToken);
        if (!response.IsSuccessStatusCode || body is not { Ok: true })
        {
            throw new TelegramApiException($"setWebhook failed: {Describe(response.StatusCode, body)}");
        }
    }

    public async Task<Uri?> GetWebhookUrlAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(MethodPath("getWebhookInfo"), cancellationToken);
        var body = await ReadAsync<WebhookInfo>(response, cancellationToken);
        if (!response.IsSuccessStatusCode || body is not { Ok: true, Result: { } info })
        {
            throw new TelegramApiException($"getWebhookInfo failed: {Describe(response.StatusCode, body)}");
        }

        return string.IsNullOrEmpty(info.Url) ? null : new Uri(info.Url);
    }

    // Leading slash: without it "bot123:abc/…" would parse as an absolute URI with scheme "bot123".
    private string MethodPath(string method) => $"/bot{options.Value.BotToken}/{method}";

    private static async Task<TelegramResponse<T>?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TelegramResponse<T>>(InboxJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Describe<T>(HttpStatusCode status, TelegramResponse<T>? body) =>
        string.IsNullOrWhiteSpace(body?.Description)
            ? string.Create(CultureInfo.InvariantCulture, $"HTTP {(int)status}")
            : body.Description;
}

/// <summary>Telegram refused a management call (setWebhook, getWebhookInfo). The message never contains the token.</summary>
public sealed class TelegramApiException(string message) : Exception(message);
