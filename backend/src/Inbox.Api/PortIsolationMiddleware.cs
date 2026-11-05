using Inbox.Application.UseCases;

namespace Inbox.Api;

/// <summary>
/// Security boundary (contracts §7.3, D-044): on the webhook port only <c>POST /telegram/webhook</c> exists, and on any
/// other port the webhook path does not exist. Enforced by <see cref="ConnectionInfo.LocalPort"/>, never by the Host
/// header, which behind ngrok is the public domain.
/// </summary>
internal sealed class PortIsolationMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var onWebhookPort = context.Connection.LocalPort == ApiPorts.Webhook;
        var isWebhookPath = context.Request.Path.Equals(RegisterWebhookHandler.WebhookPath, StringComparison.OrdinalIgnoreCase);
        var allowed = onWebhookPort
            ? isWebhookPath && HttpMethods.IsPost(context.Request.Method)
            : !isWebhookPath;

        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
