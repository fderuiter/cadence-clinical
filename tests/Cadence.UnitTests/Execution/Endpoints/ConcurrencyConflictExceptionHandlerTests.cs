using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// A lost optimistic-concurrency race reaches the caller as 409 Conflict with the stable code
/// <c>CONCURRENCY_CONFLICT</c>, never as a 500 or a silent overwrite (#4766).
/// </summary>
public sealed class ConcurrencyConflictExceptionHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (ConcurrencyConflictExceptionHandler Handler, DefaultHttpContext Context) Create()
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        return (new ConcurrencyConflictExceptionHandler(services.GetRequiredService<IProblemDetailsService>()), context);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ConcurrencyConflict_IsWrittenAs409WithTheStableCode()
    {
        var (handler, context) = Create();
        var conflict = new ConcurrencyConflictException(["CecDossier"], new DbUpdateConcurrencyException("stale"));

        bool handled = await handler.TryHandleAsync(context, conflict, Ct);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: Ct);
        body.RootElement.GetProperty("status").GetInt32().Should().Be(409);
        body.RootElement.GetProperty("code").GetString().Should().Be(ConcurrencyConflictException.Code);
    }

    [Fact]
    public async Task OtherExceptions_AreLeftToTheDefaultHandler()
    {
        var (handler, context) = Create();

        bool handled = await handler.TryHandleAsync(context, new InvalidOperationException(), Ct);

        handled.Should().BeFalse();
    }
}
