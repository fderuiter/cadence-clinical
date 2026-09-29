using Cadence.Apps.Execution.Application.Abstractions;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Turns a <see cref="ConcurrencyConflictException"/> from any endpoint into a 409 Conflict ProblemDetails
/// carrying <c>code: CONCURRENCY_CONFLICT</c> (#4766). Every other exception is left to the default handler.
/// @req:PRD-SUB-001
/// </summary>
public sealed class ConcurrencyConflictExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ConcurrencyConflictException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Concurrency conflict",
                Detail = "The record was changed by another request after it was read. Reload it and apply the change again.",
                Extensions = { ["code"] = ConcurrencyConflictException.Code },
            },
        }).ConfigureAwait(false);
    }
}
