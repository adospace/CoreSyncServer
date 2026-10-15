using CoreSync;
using CoreSync.Http;
using CoreSyncServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CoreSyncServer.Filters;

/// <summary>
/// Builds the responses the sync API returns for a failure it recognises: the status code the client
/// always received for that case, plus the <see cref="SyncHttpHeaders.ErrorCode"/> and
/// <see cref="SyncHttpHeaders.TraceId"/> headers and a <see cref="SyncErrorDetails"/> body that a
/// current <c>CoreSync.Http.Client</c> turns into a typed failure the app can explain to the user.
/// </summary>
/// <remarks>
/// Status codes are deliberately unchanged from what the controller returned before the signal
/// existed (404 for an unknown endpoint or session, 503 for an offline agent, 422 for an unsupported
/// configuration): an older client only looks at the status, and must keep seeing what it saw. The
/// body also keeps the host's <c>UseStatusCodePagesWithReExecute</c> from replacing a 404 with the
/// HTML not-found page, which has no content type a sync client could use.
/// </remarks>
public static class SyncErrorResponses
{
    public static ObjectResult EndpointNotFound(HttpContext httpContext, Guid endpointId)
        => Create(httpContext, StatusCodes.Status404NotFound, SyncHttpErrorCodes.EndpointNotFound,
            $"Synchronization endpoint '{endpointId}' does not exist or is not published.");

    public static ObjectResult SessionExpired(HttpContext httpContext, Guid sessionId)
        => Create(httpContext, StatusCodes.Status404NotFound, SyncHttpErrorCodes.SessionExpired,
            $"Synchronization session '{sessionId}' is no longer available on the server. Start a new synchronization.");

    public static ObjectResult AgentUnavailable(HttpContext httpContext, string message)
        => Create(httpContext, StatusCodes.Status503ServiceUnavailable, SyncHttpErrorCodes.AgentUnavailable, message);

    /// <summary>
    /// The 503 an agent failure has always produced, with <c>agent-error</c> when the agent answered
    /// with a failure and <c>agent-unavailable</c> when it did not answer at all. Neither code is
    /// permanent, so clients keep retrying as they did.
    /// </summary>
    public static ObjectResult AgentFailure(HttpContext httpContext, AgentOfflineException exception)
        => Create(
            httpContext,
            StatusCodes.Status503ServiceUnavailable,
            exception is AgentOperationFailedException ? SyncHttpErrorCodes.AgentError : SyncHttpErrorCodes.AgentUnavailable,
            exception.Message);

    public static ObjectResult ConfigurationError(HttpContext httpContext, string message)
        => Create(httpContext, StatusCodes.Status422UnprocessableEntity, SyncHttpErrorCodes.ConfigurationError, message);

    public static ObjectResult Create(HttpContext httpContext, int statusCode, string code, string message, string? tableName = null)
    {
        var details = new SyncErrorDetails
        {
            Code = code,
            Message = message,
            TableName = tableName,
            TraceId = httpContext.TraceIdentifier
        };

        httpContext.Response.Headers[SyncHttpHeaders.ErrorCode] = code;
        httpContext.Response.Headers[SyncHttpHeaders.TraceId] = details.TraceId;

        return new ObjectResult(details) { StatusCode = statusCode };
    }

    /// <summary>
    /// Arranges for the error code and trace headers to be on the response the host's exception
    /// handler produces for an exception this API does not handle, without handling it: the exception
    /// keeps propagating to <c>UseExceptionHandler</c>, the Sentry middleware and the request logger
    /// exactly as before.
    /// </summary>
    /// <remarks>
    /// The exception handler middleware clears every header already set when it takes over, so the
    /// headers are added from an <see cref="HttpResponse.OnStarting"/> callback, which it does not
    /// clear and which runs when its error response starts. A host without any handler lets Kestrel
    /// write a bare 500, which runs no OnStarting callbacks: that response carries no header, and the
    /// status alone still tells the client the server answered. Nothing is registered once the
    /// response has started, because that would throw and replace the real failure.
    /// </remarks>
    public static void TagUnhandled(HttpContext httpContext, Exception exception)
    {
        var response = httpContext.Response;
        if (response.HasStarted)
            return;

        var code = Classify(exception);
        var traceId = httpContext.TraceIdentifier;

        response.OnStarting(() =>
        {
            if (!response.Headers.ContainsKey(SyncHttpHeaders.ErrorCode))
            {
                response.Headers[SyncHttpHeaders.ErrorCode] = code;
                response.Headers[SyncHttpHeaders.TraceId] = traceId;
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Picks the error code for an exception the controller did not expect: a provider's
    /// <see cref="SynchronizationException"/> means the store refused a change; anything else is an
    /// unclassified server failure, which still tells the client the device is online.
    /// </summary>
    public static string Classify(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is SynchronizationException)
                return SyncHttpErrorCodes.DataError;

            if (current is NotSupportedException)
                return SyncHttpErrorCodes.ConfigurationError;
        }

        return SyncHttpErrorCodes.ServerError;
    }
}
