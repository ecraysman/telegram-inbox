namespace Inbox.Api.Errors;

/// <summary>
/// Guarantees a <c>code</c> on every problem response (contracts §2): use cases set their own codes; framework
/// responses (route mismatch, status-code pages, unhandled exceptions) get one derived from the status.
/// </summary>
internal static class ProblemCodes
{
    public static void EnsureCode(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        if (problem.Extensions.ContainsKey("code"))
        {
            return;
        }

        problem.Extensions["code"] = (problem.Status ?? context.HttpContext.Response.StatusCode) switch
        {
            StatusCodes.Status400BadRequest => "bad_request",
            StatusCodes.Status401Unauthorized => "unauthorized",
            StatusCodes.Status404NotFound => "not_found",
            StatusCodes.Status405MethodNotAllowed => "method_not_allowed",
            StatusCodes.Status409Conflict => "conflict",
            StatusCodes.Status415UnsupportedMediaType => "unsupported_media_type",
            StatusCodes.Status503ServiceUnavailable => "service_unavailable",
            >= StatusCodes.Status500InternalServerError => "internal_error",
            var status => $"http_{status}",
        };
    }
}
