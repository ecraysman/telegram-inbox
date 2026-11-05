using Inbox.Api.Errors;
using Inbox.Application.Contracts;
using Inbox.Application.UseCases;

namespace Inbox.Api.Endpoints;

/// <summary>The operator REST API (contracts §2). Endpoints only map input, call one handler and map output.</summary>
internal static class InboxEndpoints
{
    public static IEndpointRouteBuilder MapInboxEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/contacts", async (GetContactsHandler handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(ct)));

        api.MapGet("/contacts/{contactId:guid}/messages", async (Guid contactId, GetConversationHandler handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(contactId, ct)));

        api.MapPost("/contacts/{contactId:guid}/read", async (Guid contactId, HttpRequest request, MarkConversationAsReadHandler handler, CancellationToken ct) =>
        {
            var body = await JsonBody.TryReadAsync<MarkReadRequest>(request, ct);
            if (body?.UpToMessageId is not { } upTo || upTo == Guid.Empty)
            {
                return Problems.Validation(MarkConversationAsReadHandler.UpToMessageIdField, "A valid message id is required.");
            }

            await handler.HandleAsync(contactId, upTo, ct);
            return TypedResults.NoContent();
        });

        api.MapPost("/contacts/{contactId:guid}/messages", async (Guid contactId, HttpRequest request, QueueReplyHandler handler, CancellationToken ct) =>
        {
            var body = await JsonBody.TryReadAsync<QueueReplyRequest>(request, ct);
            if (body is null)
            {
                return Problems.Validation(QueueReplyHandler.TextField, "A JSON body with a text field is required.");
            }

            MessageDto message = await handler.HandleAsync(contactId, body.Text, ct);
            return TypedResults.Accepted((string?)null, message);
        });

        api.MapPost("/messages/{messageId:guid}/retry", async (Guid messageId, RetryReplyHandler handler, CancellationToken ct) =>
            TypedResults.Accepted((string?)null, await handler.HandleAsync(messageId, ct)));

        return app;
    }

    private sealed record MarkReadRequest(Guid? UpToMessageId);

    private sealed record QueueReplyRequest(string? Text);
}
