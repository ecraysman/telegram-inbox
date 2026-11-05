using Inbox.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Inbox.Application.UseCases;

public sealed record WebhookSettings(string SecretToken);

public enum WebhookRegistrationOutcome
{
    /// <summary>No public tunnel URL yet.</summary>
    TunnelNotReady,
    AlreadyRegistered,
    Registered,
}

/// <summary>
/// Points Telegram at the public tunnel URL (architecture §5). With <c>force</c> it always calls setWebhook
/// (startup: the secret may have changed); otherwise it only re-registers when Telegram's URL differs (watchdog).
/// </summary>
public sealed partial class RegisterWebhookHandler(
    IPublicUrlProvider publicUrl,
    ITelegramGateway telegram,
    WebhookSettings settings,
    ILogger<RegisterWebhookHandler> logger)
{
    public const string WebhookPath = "/telegram/webhook";

    public async Task<WebhookRegistrationOutcome> HandleAsync(bool force, CancellationToken cancellationToken)
    {
        var baseUrl = await publicUrl.GetPublicUrlAsync(cancellationToken);
        if (baseUrl is null)
        {
            return WebhookRegistrationOutcome.TunnelNotReady;
        }

        var target = new Uri(baseUrl, WebhookPath);
        if (!force && await telegram.GetWebhookUrlAsync(cancellationToken) == target)
        {
            return WebhookRegistrationOutcome.AlreadyRegistered;
        }

        await telegram.SetWebhookAsync(target, settings.SecretToken, cancellationToken);
        var registered = await telegram.GetWebhookUrlAsync(cancellationToken);
        if (registered != target)
        {
            throw new InvalidOperationException($"Telegram reports webhook '{registered}' instead of '{target}' after setWebhook.");
        }

        LogRegistered(logger, target);
        return WebhookRegistrationOutcome.Registered;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram webhook registered at {WebhookUrl}")]
    private static partial void LogRegistered(ILogger logger, Uri webhookUrl);
}
