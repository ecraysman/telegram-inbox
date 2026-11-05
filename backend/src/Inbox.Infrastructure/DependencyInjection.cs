using Inbox.Application.Abstractions;
using Inbox.Infrastructure.Configuration;
using Inbox.Infrastructure.Messaging;
using Inbox.Infrastructure.Ngrok;
using Inbox.Infrastructure.Persistence;
using Inbox.Infrastructure.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Inbox.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringKey = "ConnectionStrings:Inbox";

    /// <summary>EF Core + Npgsql, repositories and read projections.</summary>
    public static IServiceCollection AddInboxPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = InboxSettings.Required(configuration, ConnectionStringKey);
        services.AddDbContext<InboxDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IContactRepository, ContactRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IInboxQueries, InboxQueries>();
        return services;
    }

    /// <summary>Shared connection (declares the topology), confirmed publishers for both work queues.</summary>
    public static IServiceCollection AddInboxMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new RabbitMqOptions
        {
            Host = InboxSettings.Required(configuration, $"{RabbitMqOptions.Section}:Host"),
            Port = int.Parse(InboxSettings.Required(configuration, $"{RabbitMqOptions.Section}:Port", InboxSettings.PortFormat()), System.Globalization.CultureInfo.InvariantCulture),
            User = InboxSettings.Required(configuration, $"{RabbitMqOptions.Section}:User"),
            Password = InboxSettings.Required(configuration, $"{RabbitMqOptions.Section}:Password"),
        };
        services.AddSingleton(Options.Create(options));
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<RabbitMqPublisher>();
        services.AddSingleton<IIncomingMessagePublisher, IncomingMessagePublisher>();
        services.AddSingleton<IOutgoingMessagePublisher, OutgoingMessagePublisher>();
        return services;
    }

    /// <summary>Worker side of <see cref="IInboxNotifier"/>: events go to the inbox.events fanout.</summary>
    public static IServiceCollection AddRabbitMqInboxNotifier(this IServiceCollection services) =>
        services.AddSingleton<IInboxNotifier, RabbitMqInboxNotifier>();

    public static IServiceCollection AddTelegramGateway(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new TelegramOptions
        {
            BotToken = InboxSettings.Required(configuration, $"{TelegramOptions.Section}:BotToken", InboxSettings.BotTokenFormat()),
        };
        services.AddSingleton(Options.Create(options));
        services.AddHttpClient<ITelegramGateway, TelegramGateway>(client =>
            {
                client.BaseAddress = TelegramOptions.ApiBaseAddress;
                client.Timeout = TelegramOptions.RequestTimeout;
            })
            .RemoveAllLoggers(); // The default loggers print request URLs, which contain the bot token.
        return services;
    }

    public static IServiceCollection AddNgrokPublicUrlProvider(this IServiceCollection services, IConfiguration configuration)
    {
        var apiUrl = InboxSettings.RequiredAbsoluteUri(configuration, "Ngrok:ApiUrl");
        services.AddHttpClient<IPublicUrlProvider, NgrokPublicUrlProvider>(client =>
        {
            client.BaseAddress = apiUrl;
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        return services;
    }
}
