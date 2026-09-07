using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Testing;

using FluentAssertions;

using FluentValidation;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// Coverage for `MIG-SUBJ-022`'s generic <see cref="ValidationFilter{TRequest}"/> — resolves
/// `IValidator&lt;TRequest&gt;` from the request's DI container and short-circuits with a `400`
/// validation-problem response on failure (`02_technical_design.md` §3.3). Exercised against two
/// unrelated `TRequest` shapes (<see cref="SubjectCreateRequest"/>, <see cref="SubjectConsentRequest"/>,
/// both from `MIG-SUBJ-009`) to prove genericity per this ticket's Verification requirement, plus a
/// boundary case proving the bound-argument lookup is position-independent rather than hardcoded to
/// index 0.
/// @req:PRD-SUB-001
/// </summary>
public class ValidationFilterTests
{
    private static readonly object NextSentinel = new();

    private static IServiceProvider BuildServices<TRequest>(IValidator<TRequest> validator)
    {
        var services = new ServiceCollection();
        services.AddSingleton(validator);
        return services.BuildServiceProvider();
    }

    private static EndpointFilterInvocationContext CreateContext(IServiceProvider services, params object?[] arguments)
    {
        var httpContext = new DefaultHttpContext { RequestServices = services };
        return new DefaultEndpointFilterInvocationContext(httpContext, arguments);
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

    // ---- SubjectCreateRequest (argument at index 0) -----------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task InvokeAsync_ValidSubjectCreateRequest_InvokesNextAndReturnsItsResultUnchanged()
    {
        var services = BuildServices<SubjectCreateRequest>(new SubjectCreateRequestValidator());
        var request = new SubjectCreateRequest("SUBJ-001", "STUDY-001", null, null, null);
        var context = CreateContext(services, request);
        var (next, invoked) = CreateTrackingNext();

        var result = await new ValidationFilter<SubjectCreateRequest>().InvokeAsync(context, next);

        invoked().Should().BeTrue();
        result.Should().BeSameAs(NextSentinel);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task InvokeAsync_InvalidSubjectCreateRequest_ShortCircuitsWith400ValidationProblemAndNeverInvokesNext()
    {
        var services = BuildServices<SubjectCreateRequest>(new SubjectCreateRequestValidator());
        var request = new SubjectCreateRequest("", "STUDY-001", null, null, null); // empty SubjectId fails NotEmpty()
        var context = CreateContext(services, request);
        var (next, invoked) = CreateTrackingNext();

        var result = await new ValidationFilter<SubjectCreateRequest>().InvokeAsync(context, next);

        invoked().Should().BeFalse();
        var problem = result.Should().BeOfType<ProblemHttpResult>().Which;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var validationDetails = problem.ProblemDetails.Should().BeOfType<HttpValidationProblemDetails>().Which;
        validationDetails.Errors.Should().ContainKey("SubjectId");
    }

    // ---- SubjectConsentRequest (a second, unrelated TRequest shape — proves genericity) ------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task InvokeAsync_ValidSubjectConsentRequest_InvokesNextAndReturnsItsResultUnchanged()
    {
        var services = BuildServices<SubjectConsentRequest>(new SubjectConsentRequestValidator());
        var request = new SubjectConsentRequest(
            new ProtocolVersionPayload("STUDY-001", "2.0", 2), IcfSigned: true, IcfSignedDate: null);
        var context = CreateContext(services, request);
        var (next, invoked) = CreateTrackingNext();

        var result = await new ValidationFilter<SubjectConsentRequest>().InvokeAsync(context, next);

        invoked().Should().BeTrue();
        result.Should().BeSameAs(NextSentinel);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task InvokeAsync_InvalidSubjectConsentRequest_ShortCircuitsWith400ValidationProblemAndNeverInvokesNext()
    {
        var services = BuildServices<SubjectConsentRequest>(new SubjectConsentRequestValidator());
        var request = new SubjectConsentRequest(null!, IcfSigned: true, IcfSignedDate: null); // fails NotNull()
        var context = CreateContext(services, request);
        var (next, invoked) = CreateTrackingNext();

        var result = await new ValidationFilter<SubjectConsentRequest>().InvokeAsync(context, next);

        invoked().Should().BeFalse();
        var problem = result.Should().BeOfType<ProblemHttpResult>().Which;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    // ---- Boundary check: TRequest argument not at index 0 -------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task InvokeAsync_RequestArgumentNotAtIndexZero_StillLocatesAndValidatesIt()
    {
        // Boundary AC: the endpoint delegate's TRequest parameter is not always first. Here it sits at
        // index 1, behind a `string subjectId` route parameter, mirroring the real
        // `POST /{subjectId}/consent` delegate shape `(string subjectId, SubjectConsentRequest request,
        // ISubjectApplicationService svc, CancellationToken ct)`.
        var services = BuildServices<SubjectConsentRequest>(new SubjectConsentRequestValidator());
        var request = new SubjectConsentRequest(
            new ProtocolVersionPayload("STUDY-001", "2.0", 2), IcfSigned: true, IcfSignedDate: null);
        var context = CreateContext(services, "SUBJ-001", request, CancellationToken.None);
        var (next, invoked) = CreateTrackingNext();

        var result = await new ValidationFilter<SubjectConsentRequest>().InvokeAsync(context, next);

        invoked().Should().BeTrue();
        result.Should().BeSameAs(NextSentinel);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task InvokeAsync_InvalidRequestArgumentNotAtIndexZero_StillShortCircuitsWith400()
    {
        var services = BuildServices<SubjectConsentRequest>(new SubjectConsentRequestValidator());
        var request = new SubjectConsentRequest(null!, IcfSigned: true, IcfSignedDate: null);
        var context = CreateContext(services, "SUBJ-001", request, CancellationToken.None);
        var (next, invoked) = CreateTrackingNext();

        var result = await new ValidationFilter<SubjectConsentRequest>().InvokeAsync(context, next);

        invoked().Should().BeFalse();
        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task InvokeAsync_NoArgumentOfRequestType_ThrowsInvalidOperationException()
    {
        // Misconfiguration guard: applying ValidationFilter<T> to a route whose delegate never binds a
        // T-typed parameter is a developer/composition-root error, not a normal 400 — it must fail loudly
        // rather than silently validating the wrong (or no) argument.
        var services = BuildServices<SubjectCreateRequest>(new SubjectCreateRequestValidator());
        var context = CreateContext(services, "SUBJ-001", 42);
        var (next, _) = CreateTrackingNext();

        var act = async () => await new ValidationFilter<SubjectCreateRequest>().InvokeAsync(context, next);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
