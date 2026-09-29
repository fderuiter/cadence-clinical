using System;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// Coverage for `MIG-SUBJ-022`'s <see cref="ChangeReasonHeaderFilter"/> — the mandatory
/// `X-Change-Reason` header check (21 CFR Part 11 change-justification) applied ahead of
/// `PATCH .../state`, `PUT .../demographics`, and `DELETE .../demographics` per
/// `02_technical_design.md` §3.3 (route registration itself is `MIG-SUBJ-023`'s job — no hosted route
/// exists yet). Exercises the filter directly via `DefaultEndpointFilterInvocationContext`, the
/// documented way to unit test an `IEndpointFilter` without a `WebApplicationFactory`.
/// </summary>
public class ChangeReasonHeaderFilterTests
{
    private static readonly object NextSentinel = new();

    private static EndpointFilterInvocationContext CreateContext(string? headerValue)
    {
        var httpContext = new DefaultHttpContext();
        if (headerValue is not null)
        {
            httpContext.Request.Headers["X-Change-Reason"] = headerValue;
        }

        return new DefaultEndpointFilterInvocationContext(httpContext);
    }

    private static (EndpointFilterDelegate Next, Func<bool> Invoked) CreateTrackingNext()
    {
        var invoked = false;
        ValueTask<object?> Next(EndpointFilterInvocationContext _)
        {
            invoked = true;
            return ValueTask.FromResult<object?>(NextSentinel);
        }

        return (Next, () => invoked);
    }

    [Fact]
    public async Task InvokeAsync_HeaderAbsent_ShortCircuitsWith400AndNeverInvokesNext()
    {
        var context = CreateContext(headerValue: null);
        var (next, invoked) = CreateTrackingNext();

        var result = await new ChangeReasonHeaderFilter().InvokeAsync(context, next);

        invoked().Should().BeFalse();
        var problem = result.Should().BeOfType<ProblemHttpResult>().Which;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Detail.Should().Be("X-Change-Reason header is required.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvokeAsync_HeaderPresentButWhitespaceOnly_ShortCircuitsWith400AndNeverInvokesNext(string headerValue)
    {
        // "" (present but empty) must be rejected identically to an absent header — 01_spec.md §3's
        // whitespace-trap guidance generalized to this header: an explicitly-empty value is not the same
        // as "the caller didn't send one, so we'll let it slide."
        var context = CreateContext(headerValue);
        var (next, invoked) = CreateTrackingNext();

        var result = await new ChangeReasonHeaderFilter().InvokeAsync(context, next);

        invoked().Should().BeFalse();
        var problem = result.Should().BeOfType<ProblemHttpResult>().Which;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task InvokeAsync_HeaderPresentAndPopulated_InvokesNextAndReturnsItsResultUnchanged()
    {
        // AC error condition: "the two filters are independent and composable on the same route" — a
        // populated header must yield exactly next's result, untouched; this filter neither replaces nor
        // wraps a downstream (e.g. ValidationFilter<T>) result.
        var context = CreateContext("Protocol Amendment 3 requires re-baseline.");
        var (next, invoked) = CreateTrackingNext();

        var result = await new ChangeReasonHeaderFilter().InvokeAsync(context, next);

        invoked().Should().BeTrue();
        result.Should().BeSameAs(NextSentinel);
    }
}
