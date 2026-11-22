using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Api;

/// <summary>
/// Observation reads and writes (spec §8.1, ADR-2620). Every write is a kernel command sent through
/// <see cref="IKernelCommandSender"/>: the reason comes from the signed identity, the idempotency key from the
/// <c>Idempotency-Key</c> header, and the tenant from <see cref="KernelTenantFilter"/>.
/// </summary>
public static class KernelObservationEndpoints
{
    /// <summary>The header carrying the command's idempotency key (K-TX-3).</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Maps <c>/observations</c> on the kernel group.</summary>
    public static RouteGroupBuilder MapKernelObservations(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup("/observations").AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel observations");

        group.MapGet("/{observationRef}", GetAsync)
            .WithName("KernelGetObservation")
            .WithSummary("An observation with its revisions, the locks covering it, the assertions about it and its kernel audit entries.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost(string.Empty, RecordAsync)
            .WithName("KernelRecordObservation")
            .WithSummary("Records an observation for the participant of an enrollment (observation.record).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<RecordObservation>();

        group.MapPost("/{observationRef}/corrections", CorrectAsync)
            .WithName("KernelCorrectObservation")
            .WithSummary("Corrects an observation's value with the signed reason for change (observation.correct).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<CorrectObservation>();

        group.MapPost("/{observationRef}/verifications", VerifyAsync)
            .WithName("KernelVerifyObservation")
            .WithSummary("Verifies the current revision of an observation against source with the signed reason for change (observation.verify).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<VerifyObservation>();

        var assertions = kernel.MapGroup("/assertions").AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel observations");
        assertions.MapPost("/{assertionRef}/retraction", RetractAssertionAsync)
            .WithName("KernelRetractAssertion")
            .WithSummary("Retracts a verification, answer or query assertion with the signed reason for change (assertion.retract).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<RetractAssertion>();

        return kernel;
    }

    internal static async Task<Results<Ok<KernelObservationResponse>, ProblemHttpResult>> GetAsync(
        string observationRef,
        HttpContext context,
        IObservationRecords observations,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (ParseObservation(observationRef) is not { } observation)
        {
            return ObservationRefInvalid(observationRef);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await observations.FindAsync(tenant, observation, cancellationToken).ConfigureAwait(false) is not { } record)
        {
            return KernelProblems.NotFound(NodeRef.For(observation));
        }

        // An observation is seen by whoever may see its enrollment (ADR-2457).
        var enrollment = NodeRef.For(record.EnrollmentId);
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [enrollment], cancellationToken).ConfigureAwait(false);
        return masks.For(enrollment) is null
            ? TypedResults.Ok(KernelObservationResponse.From(record))
            : KernelProblems.NotVisible($"You may not see {NodeRef.For(observation)}.");
    }

    internal static async Task<Results<Created<KernelObservationStateResponse>, ProblemHttpResult>> RecordAsync(
        KernelRecordObservationRequest request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (request is null
            || !NodeRef.TryParse(request.EnrollmentRef, out var enrollment) || enrollment.Kind != NodeKinds.Enrollment
            || !TryParseConcept(request.Concept, out var concept))
        {
            return KernelProblems.BadRequest(
                "Invalid observation", "Name the enrollment as an enrollment NodeRef and the concept as <concept id>@<version>.");
        }

        var observation = request.ObservationId is { } given ? ObservationId.FromGuid(given) : ObservationId.New();
        var result = await sender.SendAsync(
            new RecordObservation(
                KernelRequestTenant.Get(context),
                observation,
                EnrollmentId.FromGuid(enrollment.Id),
                concept,
                request.Value!,
                request.ObservedAt,
                IdempotencyKey(context),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state
            ? TypedResults.Created($"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/observations/{NodeRef.For(state.ObservationId)}", KernelObservationStateResponse.From(state))
            : Problem(result.Error!);
    }

    internal static async Task<Results<Ok<KernelObservationStateResponse>, ProblemHttpResult>> CorrectAsync(
        string observationRef,
        KernelCorrectObservationRequest request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (ParseObservation(observationRef) is not { } observation)
        {
            return ObservationRefInvalid(observationRef);
        }

        if (request is null || !NodeRef.TryParse(request.EnrollmentRef, out var enrollment) || enrollment.Kind != NodeKinds.Enrollment)
        {
            return KernelProblems.BadRequest("Invalid correction", "Name the observation's enrollment as an enrollment NodeRef.");
        }

        var result = await sender.SendAsync(
            new CorrectObservation(
                KernelRequestTenant.Get(context),
                observation,
                EnrollmentId.FromGuid(enrollment.Id),
                request.Value!,
                request.ExpectedVersion,
                IdempotencyKey(context),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state ? TypedResults.Ok(KernelObservationStateResponse.From(state)) : Problem(result.Error!);
    }

    internal static async Task<Results<Created<KernelAssertionStateResponse>, ProblemHttpResult>> VerifyAsync(
        string observationRef,
        KernelVerifyObservationRequest request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (ParseObservation(observationRef) is not { } observation)
        {
            return ObservationRefInvalid(observationRef);
        }

        if (request is null
            || !NodeRef.TryParse(request.EnrollmentRef, out var enrollment)
            || enrollment.Kind != NodeKinds.Enrollment
            || request.Revision < 1)
        {
            return KernelProblems.BadRequest(
                "Invalid verification",
                "Name the observation's enrollment as an enrollment NodeRef and the revision verified (from 1).");
        }

        var assertion = request.AssertionId is { } given && given != Guid.Empty ? AssertionId.FromGuid(given) : AssertionId.New();
        var result = await sender.SendAsync(
            new VerifyObservation(
                KernelRequestTenant.Get(context),
                assertion,
                EnrollmentId.FromGuid(enrollment.Id),
                observation,
                request.Revision,
                IdempotencyKey(context),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state
            ? TypedResults.Created(
                $"{KernelApiEndpointRouteBuilderExtensions.RoutePrefix}/observations/{NodeRef.For(observation)}",
                KernelAssertionStateResponse.From(state))
            : Problem(result.Error!);
    }

    internal static async Task<Results<Ok<KernelAssertionStateResponse>, ProblemHttpResult>> RetractAssertionAsync(
        string assertionRef,
        KernelRetractAssertionRequest request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (ParseAssertion(assertionRef) is not { } retracted)
        {
            return AssertionRefInvalid(assertionRef);
        }

        if (request is null
            || !NodeRef.TryParse(request.EnrollmentRef, out var enrollment)
            || enrollment.Kind != NodeKinds.Enrollment)
        {
            return KernelProblems.BadRequest(
                "Invalid retraction",
                "Name the assertion's enrollment as an enrollment NodeRef.");
        }

        var assertion = request.AssertionId is { } given && given != Guid.Empty ? AssertionId.FromGuid(given) : AssertionId.New();
        var result = await sender.SendAsync(
            new RetractAssertion(
                KernelRequestTenant.Get(context),
                assertion,
                EnrollmentId.FromGuid(enrollment.Id),
                retracted,
                IdempotencyKey(context),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state ? TypedResults.Ok(KernelAssertionStateResponse.From(state)) : Problem(result.Error!);
    }

    /// <summary>A kernel error as RFC 9457 problem details, with its stable code (K-ERR-1) and any field errors.</summary>
    internal static ProblemHttpResult Problem(KernelError error) => KernelErrorEndpoints.Problem(error);

    private static string IdempotencyKey(HttpContext context) => context.Request.Headers[IdempotencyKeyHeader].ToString().Trim();

    private static ObservationId? ParseObservation(string text) =>
        NodeRef.TryParse(text, out var node) && node.Kind == NodeKinds.Observation ? ObservationId.FromGuid(node.Id) : null;

    private static AssertionId? ParseAssertion(string text) =>
        NodeRef.TryParse(text, out var node) && node.Kind == NodeKinds.Assertion && node.Version is null ? AssertionId.FromGuid(node.Id) : null;

    private static ProblemHttpResult ObservationRefInvalid(string text) =>
        KernelProblems.BadRequest("Invalid NodeRef", $"'{text}' is not an observation NodeRef (observation:<uuid>).");

    private static ProblemHttpResult AssertionRefInvalid(string text) =>
        KernelProblems.BadRequest("Invalid NodeRef", $"'{text}' is not an assertion NodeRef (assertion:<uuid>).");

    private static bool TryParseConcept(string? text, out ConceptRef concept)
    {
        concept = default;
        var parts = (text ?? string.Empty).Split('@');
        if (parts.Length != 2 || !ConceptId.TryParse(parts[0], out var id) || id.IsEmpty
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var version) || version < 1)
        {
            return false;
        }

        concept = new ConceptRef(id, version);
        return true;
    }
}

/// <summary>Body of <c>POST /observations</c>.</summary>
/// <param name="ObservationId">The new observation's id; the kernel assigns one when absent. Retrying with the same idempotency key must send the same id.</param>
/// <param name="EnrollmentRef">The enrollment the fact is about, as a NodeRef.</param>
/// <param name="Concept">What the value means, as <c>&lt;concept id&gt;@&lt;version&gt;</c>.</param>
/// <param name="Value">The value as entered, typed by its <c>type</c> member (ADR-2623).</param>
/// <param name="ObservedAt">When the fact held in the trial world.</param>
public sealed record KernelRecordObservationRequest(Guid? ObservationId, string EnrollmentRef, string Concept, ObservationValue? Value, DateTimeOffset ObservedAt);

/// <summary>Body of <c>POST /observations/{ref}/corrections</c>.</summary>
/// <param name="EnrollmentRef">The observation's enrollment, as a NodeRef; the correction is authorized against it.</param>
/// <param name="Value">The corrected value.</param>
/// <param name="ExpectedVersion">The observation version the corrector saw; another version is refused with 409.</param>
public sealed record KernelCorrectObservationRequest(string EnrollmentRef, ObservationValue? Value, long ExpectedVersion);

/// <summary>Body of <c>POST /observations/{ref}/verifications</c>.</summary>
/// <param name="EnrollmentRef">The observation's enrollment, as a NodeRef; the verification is authorized against it.</param>
/// <param name="Revision">The current revision of the observation being verified against source.</param>
/// <param name="AssertionId">The new assertion's id; the kernel assigns one when absent.</param>
public sealed record KernelVerifyObservationRequest(string EnrollmentRef, int Revision, Guid? AssertionId = null);

/// <summary>Body of <c>POST /assertions/{ref}/retraction</c>.</summary>
/// <param name="EnrollmentRef">The assertion's enrollment, as a NodeRef; the retraction is authorized against it.</param>
/// <param name="AssertionId">The retracting assertion's id; the kernel assigns one when absent.</param>
public sealed record KernelRetractAssertionRequest(string EnrollmentRef, Guid? AssertionId = null);

/// <summary>An assertion after a verification or retraction command.</summary>
/// <param name="Ref">The recorded assertion, as a NodeRef.</param>
/// <param name="Kind">The assertion kind (<c>Verified</c> or the retracted assertion's kind).</param>
/// <param name="Target">What the assertion targets, as a NodeRef.</param>
/// <param name="Revision">The observation revision judged, when applicable.</param>
/// <param name="Retracts">The assertion retracted, as a NodeRef, when this assertion is a retraction.</param>
/// <param name="AssertedAt">When the assertion was recorded.</param>
/// <param name="AssertedBy">Who recorded the assertion.</param>
public sealed record KernelAssertionStateResponse(
    string Ref,
    string Kind,
    string Target,
    int? Revision,
    string? Retracts,
    DateTimeOffset AssertedAt,
    string AssertedBy)
{
    internal static KernelAssertionStateResponse From(AssertionState state) =>
        new(
            NodeRef.For(state.AssertionId).ToString(),
            state.Kind.ToString(),
            state.Target.ToString(),
            state.Revision,
            state.Retracts is { } retracts ? NodeRef.For(retracts).ToString() : null,
            state.AssertedAt,
            state.AssertedBy);
}

/// <summary>An observation's value after a command.</summary>
/// <param name="Ref">The observation, as a NodeRef at its new version.</param>
/// <param name="EnrollmentRef">Its enrollment.</param>
/// <param name="Value">The current value.</param>
/// <param name="Version">The version to name as expected in the next correction.</param>
/// <param name="Revision">The number of the revision holding the value.</param>
public sealed record KernelObservationStateResponse(string Ref, string EnrollmentRef, ObservationValue Value, long Version, int Revision)
{
    internal static KernelObservationStateResponse From(ObservationState state) =>
        new(NodeRef.For(state.ObservationId, state.Version).ToString(), NodeRef.For(state.EnrollmentId).ToString(), state.Value, state.Version, state.Revision);
}

/// <summary>An observation with its correction history, the locks covering it, the assertions about it and its audit trail.</summary>
/// <param name="Ref">The observation, as a NodeRef.</param>
/// <param name="EnrollmentRef">Its enrollment.</param>
/// <param name="Concept">What it means, as <c>&lt;concept id&gt;@&lt;version&gt;</c>.</param>
/// <param name="Value">The current value.</param>
/// <param name="ObservedAt">When the fact held in the trial world.</param>
/// <param name="Version">The version a correction names as expected.</param>
/// <param name="Locks">The locks in force at its study, study site, enrollment or itself, widest scope first (ADR-2621); empty when none covers it.</param>
/// <param name="Revisions">Every revision, oldest first.</param>
/// <param name="Assertions">Verifications, queries and answers about it, retracted ones included, oldest first.</param>
/// <param name="Audit">The kernel audit-chain entries about the observation, oldest first.</param>
/// <param name="FormRef">Owning collection instance NodeRef when the observation belongs to an eCRF form.</param>
/// <param name="ItemId">Owning collection item identifier when the observation belongs to an eCRF form.</param>
/// <param name="ExpectedValueKind">Expected value kind declared by the owning collection item.</param>
/// <param name="ExpectedUnit">Expected UCUM unit declared by the owning collection item.</param>
/// <param name="ExpectedCodeSystemVersionId">Expected code-system version declared by the owning collection item.</param>
public sealed record KernelObservationResponse(
    string Ref,
    string EnrollmentRef,
    string Concept,
    ObservationValue Value,
    DateTimeOffset ObservedAt,
    long Version,
    IReadOnlyList<LockView> Locks,
    IReadOnlyList<ObservationRevisionView> Revisions,
    IReadOnlyList<AssertionView> Assertions,
    IReadOnlyList<ObservationAuditView> Audit,
    string? FormRef = null,
    string? ItemId = null,
    string? ExpectedValueKind = null,
    string? ExpectedUnit = null,
    Guid? ExpectedCodeSystemVersionId = null)
{
    internal static KernelObservationResponse From(ObservationRecord record) =>
        new(
            NodeRef.For(record.ObservationId).ToString(),
            NodeRef.For(record.EnrollmentId).ToString(),
            record.Concept.ToString(),
            record.Value,
            record.ObservedAt,
            record.Version,
            record.Locks.ToList(),
            record.Revisions.ToList(),
            record.Assertions.ToList(),
            record.Audit.ToList(),
            record.CollectionInstanceId is { } formId ? NodeRef.For(formId).ToString() : null,
            record.ItemId,
            record.ExpectedValueKind?.ToString(),
            record.ExpectedUnit,
            record.ExpectedCodeSystemVersionId?.Value);
}
