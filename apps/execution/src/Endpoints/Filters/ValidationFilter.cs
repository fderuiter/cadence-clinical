using System;
using System.Threading.Tasks;

using FluentValidation;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Execution.Endpoints.Filters;

/// <summary>
/// Generic <see cref="IEndpointFilter"/> that resolves <see cref="IValidator{T}"/> for
/// <typeparamref name="TRequest"/> from the request's DI container (FluentValidation's standard
/// registration pattern), validates the bound <typeparamref name="TRequest"/> argument, and
/// short-circuits with a <c>400</c> validation-problem response when validation fails, otherwise forwards
/// to <c>next</c> unchanged.
/// <para>
/// The <typeparamref name="TRequest"/>-typed argument is located by scanning
/// <see cref="EndpointFilterInvocationContext.Arguments"/> for its runtime type rather than assuming
/// index <c>0</c>, so this filter behaves correctly regardless of where <typeparamref name="TRequest"/>
/// falls among the endpoint delegate's other parameters — e.g.
/// <c>(string subjectId, SubjectConsentRequest request, ...)</c>, where the DTO is not the first
/// parameter.
/// </para>
/// New, reusable presentation-layer infrastructure introduced by <c>MIG-SUBJ-022</c> (verified not to
/// already exist anywhere in <c>packages/</c>). Applied by <c>MIG-SUBJ-023</c>/<c>-024</c>'s route
/// registrations to <c>SubjectCreateRequest</c>, <c>SubjectConsentRequest</c>,
/// <c>SubjectStateUpdateRequest</c>, and <c>EtmfCompletenessWebhookPayload</c> alike
/// (<c>MIG-SUBJ-009</c>'s validators) — generic enough for other slices' endpoints to reuse unmodified.
/// @req:PRD-SUB-001
/// </summary>
public sealed class ValidationFilter<TRequest> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.GetArgument<TRequest>(FindRequestArgumentIndex(context));
        var validator = context.HttpContext.RequestServices.GetRequiredService<IValidator<TRequest>>();

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        if (!result.IsValid)
        {
            return Results.ValidationProblem(result.ToDictionary());
        }

        return await next(context);
    }

    /// <summary>
    /// Scans the invocation's bound arguments for the one assignable to <typeparamref name="TRequest"/>
    /// and returns its index. Boundary case per this ticket's Acceptance Criteria: the endpoint delegate
    /// may declare <typeparamref name="TRequest"/> at any parameter position, not only index 0.
    /// </summary>
    private static int FindRequestArgumentIndex(EndpointFilterInvocationContext context)
    {
        for (var index = 0; index < context.Arguments.Count; index++)
        {
            if (context.Arguments[index] is TRequest)
            {
                return index;
            }
        }

        throw new InvalidOperationException(
            $"ValidationFilter<{typeof(TRequest).Name}> found no {typeof(TRequest).Name}-typed argument " +
            "among the endpoint's bound parameters. Verify the filter is applied to a route whose delegate " +
            "declares a parameter of this type.");
    }
}
