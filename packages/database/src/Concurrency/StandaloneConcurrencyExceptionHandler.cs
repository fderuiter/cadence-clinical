using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Database.Concurrency;

/// <summary>
/// Maps <see cref="DbUpdateConcurrencyException"/> thrown during standalone persistence saves into an RFC 9457
/// HTTP 409 Conflict <see cref="ProblemDetails"/> response carrying <c>code: CONCURRENCY_CONFLICT</c>.
/// Invoked by <c>app.UseExceptionHandler()</c> in <c>CadenceTelemetryExtensions.UseCadenceWebDefaults</c>.
/// </summary>
public sealed class StandaloneConcurrencyExceptionHandler(IProblemDetailsService? problemDetailsService = null) : IExceptionHandler
{
    public const string ConflictCode = "CONCURRENCY_CONFLICT";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is not DbUpdateConcurrencyException
            && exception.InnerException is not DbUpdateConcurrencyException)
        {
            return false;
        }

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Concurrency conflict",
            Detail = "The record was changed by another request after it was read. Reload it and apply the change again.",
            Extensions = { ["code"] = ConflictCode },
        };

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        if (problemDetailsService is not null
            && await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = problemDetails,
            }).ConfigureAwait(false))
        {
            return true;
        }

        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
