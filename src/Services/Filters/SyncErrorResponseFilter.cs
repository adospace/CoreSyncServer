using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace CoreSyncServer.Filters;

/// <summary>
/// Makes every failure escaping <c>SyncController</c> classifiable by the client.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// <see cref="NotSupportedException"/> - a store configured in a way the provider cannot synchronize
/// (composite primary key, reserved column name...) - becomes 422 Unprocessable Entity with the
/// <c>configuration-error</c> code and a <see cref="CoreSync.Http.SyncErrorDetails"/> body carrying the
/// provider's message, the status this API has always returned for it.
/// </description></item>
/// <item><description>
/// Everything else is left unhandled, so the host's exception handler, logging and error reporting
/// (Sentry) see it exactly as before; only the error code and trace headers are attached to the host's
/// response (see <see cref="SyncErrorResponses.TagUnhandled"/>).
/// </description></item>
/// </list>
/// </remarks>
public class SyncErrorResponseFilter(ILogger<SyncErrorResponseFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.ExceptionHandled)
            return;

        if (context.Exception is NotSupportedException notSupported)
        {
            logger.LogWarning(notSupported, "Unsupported sync configuration: {Message}", notSupported.Message);

            context.Result = SyncErrorResponses.ConfigurationError(context.HttpContext, notSupported.Message);
            context.ExceptionHandled = true;
            return;
        }

        if (context.Exception is OperationCanceledException && context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is no response to tag.
            return;
        }

        SyncErrorResponses.TagUnhandled(context.HttpContext, context.Exception);
    }
}
