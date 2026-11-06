using System.Text.Json;
using Inbox.Application.Errors;
using Inbox.Domain.Common;
using Inbox.Infrastructure.Persistence;
using RabbitMQ.Client.Exceptions;

namespace Inbox.Infrastructure.Messaging;

public enum ConsumerErrorClass
{
    /// <summary>Can never succeed: dead-letter it.</summary>
    Poison,

    /// <summary>A dependency is down (transient database error, broker, timeout): retry in process without limit.</summary>
    Infrastructure,

    /// <summary>Anything else (including a non-transient database error): 3 retries, then dead-letter it (D-093).</summary>
    Unexpected,
}

/// <summary>Consumer error classes of contracts §7.5 (revision 4, D-076, D-084, D-087, D-089).</summary>
public static class ConsumerErrorClassifier
{
    private const int MaxInfrastructureBackoffSeconds = 30;

    /// <summary>Delay before the next attempt after an infrastructure failure: 1, 2, 4, 8, 16, then 30 s forever.</summary>
    public static TimeSpan InfrastructureBackoff(int failuresSoFar) =>
        TimeSpan.FromSeconds(failuresSoFar >= 5 ? MaxInfrastructureBackoffSeconds : 1 << failuresSoFar);

    public static ConsumerErrorClass Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is JsonException or PoisonMessageException or DomainException)
        {
            return ConsumerErrorClass.Poison;
        }

        // Only transient database errors are outages; a constraint violation or a SQL bug is Unexpected (bounded, then DLQ).
        // Writes arrive as TransientPersistenceException (unit of work); reads as raw provider errors (TransientErrors).
        if (exception is TransientPersistenceException || TransientErrors.IsTransient(exception))
        {
            return ConsumerErrorClass.Infrastructure;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is RabbitMQClientException or BrokerUnreachableException)
            {
                return ConsumerErrorClass.Infrastructure;
            }
        }

        return ConsumerErrorClass.Unexpected;
    }
}
