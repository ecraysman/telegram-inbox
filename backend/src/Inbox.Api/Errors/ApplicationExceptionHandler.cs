using Inbox.Application.Errors;
using Microsoft.AspNetCore.Diagnostics;

namespace Inbox.Api.Errors;

/// <summary>Maps expected use-case failures to RFC 9457 problem details with a <c>code</c> field (contracts §2).</summary>
internal sealed class ApplicationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ApplicationErrorException error)
        {
            return false;
        }

        var result = error switch
        {
            ValidationFailedException validation => Problems.Validation(validation.Field, validation.Message),
            ContactNotFoundException or MessageNotFoundException => Problems.Create(StatusCodes.Status404NotFound, error.Code, error.Message),
            MessageNotFailedException => Problems.Create(StatusCodes.Status409Conflict, error.Code, error.Message),
            _ => Problems.Create(StatusCodes.Status400BadRequest, error.Code, error.Message),
        };
        await result.ExecuteAsync(httpContext);
        return true;
    }
}

internal static class Problems
{
    public static IResult Create(int status, string code, string detail) =>
        TypedResults.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult Validation(string field, string error) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [error] },
            extensions: new Dictionary<string, object?> { ["code"] = "validation_failed" });
}
