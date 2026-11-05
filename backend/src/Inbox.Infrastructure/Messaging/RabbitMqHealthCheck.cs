using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inbox.Infrastructure.Messaging;

/// <summary>Readiness: the shared AMQP connection is open (contracts §7.9).</summary>
public sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var amqp = await connection.GetAsync(cancellationToken);
            return amqp.IsOpen ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("RabbitMQ connection is closed.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is unreachable.", ex);
        }
    }
}
