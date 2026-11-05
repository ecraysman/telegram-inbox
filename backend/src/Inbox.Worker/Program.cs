using Inbox.Application;
using Inbox.Infrastructure;
using Inbox.Infrastructure.Configuration;
using Inbox.Worker.Consumers;
using Inbox.Worker.Outbox;

var builder = Host.CreateApplicationBuilder(args);
var configuration = builder.Configuration;

// Fail fast on missing or malformed configuration (contracts §6); messages name the key, never the value.
var sweepSeconds = InboxSettings.OptionalPositiveInt(configuration, "Outbox:SweepIntervalSeconds", 30);
var staleSeconds = InboxSettings.OptionalPositiveInt(configuration, "Outbox:StaleAfterSeconds", 60);

builder.Services
    .AddInboxApplication()
    .AddInboxPersistence(configuration)
    .AddInboxMessaging(configuration)
    .AddRabbitMqInboxNotifier()
    .AddTelegramGateway(configuration);

builder.Services.AddSingleton(new OutboxSettings(TimeSpan.FromSeconds(sweepSeconds), TimeSpan.FromSeconds(staleSeconds)));
builder.Services.AddHostedService<IncomingMessageConsumer>();
builder.Services.AddHostedService<OutgoingMessageConsumer>();
builder.Services.AddHostedService<OutboxSweeperService>();

await builder.Build().RunAsync();
