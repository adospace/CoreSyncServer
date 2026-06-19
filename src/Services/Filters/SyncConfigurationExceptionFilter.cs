using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace CoreSyncServer.Filters;

/// <summary>
/// Translates unsupported-configuration errors raised by the sync provider while initializing a
/// store (e.g. a table with a composite primary key or a reserved column name) into a
/// 422 Unprocessable Entity response carrying the provider's message, instead of letting them
/// surface to the sync client as an opaque HTTP 500.
/// </summary>
public class SyncConfigurationExceptionFilter(ILogger<SyncConfigurationExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not NotSupportedException ex)
            return;

        logger.LogWarning(ex, "Unsupported sync configuration: {Message}", ex.Message);

        context.Result = new ObjectResult(ex.Message)
        {
            StatusCode = StatusCodes.Status422UnprocessableEntity
        };
        context.ExceptionHandled = true;
    }
}
