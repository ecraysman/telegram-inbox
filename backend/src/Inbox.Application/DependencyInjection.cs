using Inbox.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Inbox.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddInboxApplication(this IServiceCollection services)
    {
        services.AddScoped<EventPayloads>();
        services.AddScoped<AcceptIncomingMessageHandler>();
        services.AddScoped<ProcessIncomingMessageHandler>();
        services.AddScoped<GetContactsHandler>();
        services.AddScoped<GetConversationHandler>();
        services.AddScoped<MarkConversationAsReadHandler>();
        services.AddScoped<QueueReplyHandler>();
        services.AddScoped<RetryReplyHandler>();
        services.AddScoped<DeliverReplyHandler>();
        services.AddScoped<RequeueStalePendingRepliesHandler>();
        services.AddScoped<RegisterWebhookHandler>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
