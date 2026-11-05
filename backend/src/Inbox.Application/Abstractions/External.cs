namespace Inbox.Application.Abstractions;

/// <summary>The outcome of one <c>sendMessage</c> attempt, classified per contracts §7.6.</summary>
public abstract record SendMessageResult
{
    private SendMessageResult()
    {
    }

    public sealed record Delivered(long TelegramMessageId, DateTimeOffset Date) : SendMessageResult;

    /// <summary>Rate limited or unavailable; <see cref="RetryAfter"/> is Telegram's hint (429) when given.</summary>
    public sealed record Transient(string Description, TimeSpan? RetryAfter) : SendMessageResult;

    public sealed record Rejected(string Description) : SendMessageResult;
}

public interface ITelegramGateway
{
    Task<SendMessageResult> SendMessageAsync(long chatId, string text, CancellationToken cancellationToken);

    /// <summary>Registers the webhook. Throws when Telegram refuses or cannot be reached.</summary>
    Task SetWebhookAsync(Uri url, string secretToken, CancellationToken cancellationToken);

    /// <summary>The webhook URL Telegram currently has, or <c>null</c> when none is set.</summary>
    Task<Uri?> GetWebhookUrlAsync(CancellationToken cancellationToken);
}

/// <summary>Discovers the public https base URL of the tunnel (ngrok).</summary>
public interface IPublicUrlProvider
{
    Task<Uri?> GetPublicUrlAsync(CancellationToken cancellationToken);
}
