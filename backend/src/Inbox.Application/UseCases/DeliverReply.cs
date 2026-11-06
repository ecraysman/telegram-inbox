using Inbox.Application.Abstractions;
using Inbox.Application.Contracts;
using Inbox.Application.Errors;
using Inbox.Domain.Messages;
using Microsoft.Extensions.Logging;

namespace Inbox.Application.UseCases;

/// <summary>
/// Worker: sends a Pending reply to Telegram and records the outcome (architecture §6, contracts §7.6).
/// Transient errors are retried in process (429: min(retry_after, 10 s); others: 1, 2, 4 s); after 3 retries the reply is Failed.
/// </summary>
public sealed partial class DeliverReplyHandler(
    IMessageRepository messages,
    IContactRepository contacts,
    IUnitOfWork unitOfWork,
    ITelegramGateway telegram,
    EventPayloads payloads,
    IInboxNotifier notifier,
    TimeProvider clock,
    ILogger<DeliverReplyHandler> logger)
{
    public const int MaxRetries = 3;
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(10);

    public async Task HandleAsync(OutgoingTextMessage command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var message = await messages.GetByIdAsync(command.MessageId, cancellationToken)
            ?? throw new PoisonMessageException($"Outgoing message {command.MessageId} does not exist.");
        if (message.Direction != MessageDirection.Outbound)
        {
            throw new PoisonMessageException($"Message {command.MessageId} is not an outbound reply.");
        }

        if (message.Status != DeliveryStatus.Pending)
        {
            // Redelivery (or a sweeper duplicate) of a reply already settled: re-emit, so a lost event heals (D-078).
            LogAlreadyHandled(logger, message.Id, message.Status);
            await NotifyStatusAsync(message, cancellationToken);
            return;
        }

        var contact = await contacts.GetByIdAsync(message.ContactId, cancellationToken)
            ?? throw new PoisonMessageException($"Contact {message.ContactId} of message {message.Id} does not exist.");

        var outcome = await SendWithRetriesAsync(contact.TelegramChatId, message, cancellationToken);
        switch (outcome)
        {
            case SendMessageResult.Delivered delivered:
                message.MarkSent(delivered.TelegramMessageId, delivered.Date);
                break;
            case SendMessageResult.Rejected rejected:
                message.MarkFailed($"rejected: {rejected.Description}");
                break;
            case SendMessageResult.Transient transient:
                message.MarkFailed($"unavailable: {transient.Description}");
                break;
        }

        // From here on Telegram has a final answer: only the save is retried, sendMessage is never called again
        // within this delivery (D-077). Anything that fails after the save finds the reply settled on re-run.
        await PersistenceRetry.RunAsync(unitOfWork.SaveChangesAsync, clock, logger, message.Id, cancellationToken);
        LogOutcome(logger, message.Id, message.Status);
        await NotifyStatusAsync(message, cancellationToken);
    }

    private async Task NotifyStatusAsync(Message message, CancellationToken cancellationToken)
    {
        var payload = await payloads.ForMessageAsync(message.Id, message.ContactId, cancellationToken);
        await notifier.MessageStatusChangedAsync(payload, cancellationToken);
    }

    private async Task<SendMessageResult> SendWithRetriesAsync(long chatId, Message message, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            LogSending(logger, message.Id, attempt + 1);
            var result = await telegram.SendMessageAsync(chatId, message.Text.Value, cancellationToken);
            if (result is not SendMessageResult.Transient transient || attempt == MaxRetries)
            {
                return result;
            }

            var delay = transient.RetryAfter is { } retryAfter
                ? (retryAfter < MaxRetryAfter ? retryAfter : MaxRetryAfter)
                : TimeSpan.FromSeconds(1 << attempt);
            LogTransient(logger, message.Id, attempt + 1, delay.TotalSeconds, transient.Description);
            await Task.Delay(delay, clock, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Calling sendMessage for reply {MessageId} (attempt {Attempt})")]
    private static partial void LogSending(ILogger logger, Guid messageId, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reply {MessageId} is already {Status}; not sending again, re-emitting its status")]
    private static partial void LogAlreadyHandled(ILogger logger, Guid messageId, DeliveryStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transient Telegram error for reply {MessageId} (retry {Retry}) in {DelaySeconds}s: {Description}")]
    private static partial void LogTransient(ILogger logger, Guid messageId, int retry, double delaySeconds, string description);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reply {MessageId} is now {Status}")]
    private static partial void LogOutcome(ILogger logger, Guid messageId, DeliveryStatus status);
}
