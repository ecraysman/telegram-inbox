using System.Text.Json.Serialization;

namespace Inbox.Infrastructure.Telegram;

// Bot API wire models (snake_case via explicit names). Only the fields this inbox uses are mapped.

internal sealed record TelegramResponse<T>(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] T? Result,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("error_code")] int? ErrorCode,
    [property: JsonPropertyName("parameters")] TelegramResponseParameters? Parameters);

internal sealed record TelegramResponseParameters([property: JsonPropertyName("retry_after")] int? RetryAfter);

internal sealed record SendMessageRequest(
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("text")] string Text);

internal sealed record SentMessage(
    [property: JsonPropertyName("message_id")] long MessageId,
    [property: JsonPropertyName("date")] long Date);

internal sealed record SetWebhookRequest(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("secret_token")] string SecretToken,
    [property: JsonPropertyName("allowed_updates")] IReadOnlyList<string> AllowedUpdates,
    [property: JsonPropertyName("drop_pending_updates")] bool DropPendingUpdates);

internal sealed record WebhookInfo([property: JsonPropertyName("url")] string? Url);

/// <summary>The subset of Telegram's <c>Update</c> the webhook reads.</summary>
public sealed record TelegramUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TelegramMessage? Message);

public sealed record TelegramMessage(
    [property: JsonPropertyName("message_id")] long MessageId,
    [property: JsonPropertyName("date")] long Date,
    [property: JsonPropertyName("chat")] TelegramChat? Chat,
    [property: JsonPropertyName("from")] TelegramUser? From,
    [property: JsonPropertyName("text")] string? Text);

public sealed record TelegramChat(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("first_name")] string? FirstName,
    [property: JsonPropertyName("last_name")] string? LastName,
    [property: JsonPropertyName("username")] string? Username);

public sealed record TelegramUser(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("first_name")] string? FirstName,
    [property: JsonPropertyName("last_name")] string? LastName,
    [property: JsonPropertyName("username")] string? Username);
