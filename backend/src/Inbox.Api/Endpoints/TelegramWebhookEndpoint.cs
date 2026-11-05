using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inbox.Application.UseCases;
using Inbox.Infrastructure.Serialization;
using Inbox.Infrastructure.Telegram;

namespace Inbox.Api.Endpoints;

/// <summary>
/// Telegram intake on the webhook port (contracts §2, ADR-003): 401 on a bad secret, 200 for ignored updates,
/// 200 only after the broker confirmed the publish, 503 when it did not (Telegram retries).
/// </summary>
internal static partial class TelegramWebhookEndpoint
{
    public const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    public static IEndpointRouteBuilder MapTelegramWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost(RegisterWebhookHandler.WebhookPath, HandleAsync);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        WebhookSettings settings,
        AcceptIncomingMessageHandler handler,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(TelegramWebhookEndpoint));
        if (!SecretMatches(request.Headers[SecretHeader].ToString(), settings.SecretToken))
        {
            LogUnauthorized(logger);
            return TypedResults.Unauthorized();
        }

        TelegramUpdate? update;
        try
        {
            update = await request.ReadFromJsonAsync<TelegramUpdate>(InboxJson.Options, ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            LogUnparseable(logger);
            return TypedResults.Ok();
        }

        var incoming = TelegramUpdateMapper.TryMap(update);
        if (incoming is null)
        {
            LogIgnored(logger, update?.UpdateId);
            return TypedResults.Ok();
        }

        try
        {
            await handler.HandleAsync(incoming, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LogPublishFailed(logger, ex, incoming.UpdateId);
            return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        LogAccepted(logger, incoming.UpdateId);
        return TypedResults.Ok();
    }

    /// <summary>Constant-time comparison; hashing first also hides the secret's length.</summary>
    private static bool SecretMatches(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook call rejected: missing or wrong secret token")]
    private static partial void LogUnauthorized(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook body is not a valid Telegram update; ignored")]
    private static partial void LogUnparseable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update {UpdateId} is not a private-chat text message; ignored")]
    private static partial void LogIgnored(ILogger logger, long? updateId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Update {UpdateId} could not be queued; answering 503 so Telegram retries")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, long updateId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update {UpdateId} queued")]
    private static partial void LogAccepted(ILogger logger, long updateId);
}
