using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Inbox.Infrastructure.Messaging;

/// <summary>
/// One shared, lazily opened, auto-recovering AMQP connection per process. The topology is declared the first time it opens.
/// </summary>
public sealed partial class RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public bool IsOpen => _connection?.IsOpen == true;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (_connection is { } existing)
        {
            return existing;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not null)
            {
                return _connection;
            }

            var settings = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = settings.Host,
                Port = settings.Port,
                UserName = settings.User,
                Password = settings.Password,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ClientProvidedName = AppDomain.CurrentDomain.FriendlyName,
            };
            var connection = await factory.CreateConnectionAsync(cancellationToken);
            await using (var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken))
            {
                await RabbitMqTopology.DeclareAsync(channel, cancellationToken);
            }

            LogConnected(logger, settings.Host, settings.Port);
            _connection = connection;
            return connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Opens the connection (declaring the topology), retrying until the broker answers.</summary>
    public async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await GetAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < 30)
            {
                LogConnectFailed(logger, ex, attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to RabbitMQ at {Host}:{Port}; topology declared")]
    private static partial void LogConnected(ILogger logger, string host, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RabbitMQ not reachable yet (attempt {Attempt}); retrying")]
    private static partial void LogConnectFailed(ILogger logger, Exception exception, int attempt);
}
