using Inbox.Api;
using Inbox.Api.Endpoints;
using Inbox.Api.Errors;
using Inbox.Api.RealTime;
using Inbox.Api.Webhooks;
using Inbox.Application;
using Inbox.Application.Abstractions;
using Inbox.Application.UseCases;
using Inbox.Infrastructure;
using Inbox.Infrastructure.Configuration;
using Inbox.Infrastructure.Messaging;
using Inbox.Infrastructure.Persistence;
using Inbox.Infrastructure.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Fail fast on missing or malformed configuration (contracts §6); messages name the key, never the value.
var webhookSecret = InboxSettings.Required(configuration, "Telegram:WebhookSecret", InboxSettings.WebhookSecretFormat());
var watchdogSeconds = InboxSettings.OptionalPositiveInt(configuration, "Webhook:WatchdogIntervalSeconds", 60);

builder.Services
    .AddInboxApplication()
    .AddInboxPersistence(configuration)
    .AddInboxMessaging(configuration)
    .AddTelegramGateway(configuration)
    .AddNgrokPublicUrlProvider(configuration);

builder.Services.AddSingleton(new WebhookSettings(webhookSecret));
builder.Services.AddSingleton(new WebhookWatchdogSettings(TimeSpan.FromSeconds(watchdogSeconds)));
builder.Services.AddSingleton<IInboxNotifier, SignalRInboxNotifier>();
builder.Services.AddSingleton<IInboxEventSink, SignalRInboxEventSink>();
builder.Services.AddHostedService<InboxEventsSubscriber>();
builder.Services.AddHostedService<WebhookRegistrationService>();

builder.Services.ConfigureHttpJsonOptions(options => InboxJson.Configure(options.SerializerOptions));
builder.Services.AddSignalR().AddJsonProtocol(options => InboxJson.Configure(options.PayloadSerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<InboxDbContext>("postgres")
    .AddCheck<RabbitMqHealthCheck>("rabbitmq");

var app = builder.Build();

app.UseMiddleware<PortIsolationMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapInboxEndpoints();
app.MapHub<InboxHub>(InboxHub.Path);
app.MapTelegramWebhook();

// "Healthy" implies "migrated" (contracts §7.2), and the broker topology exists before any request (contracts §4).
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<InboxDbContext>().Database.MigrateAsync();
}

await app.Services.GetRequiredService<RabbitMqConnection>().EnsureConnectedAsync(CancellationToken.None);

await app.RunAsync();
