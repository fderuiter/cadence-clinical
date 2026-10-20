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

    /// <summary>A kernel error as RFC 9457 problem details, with its stable code (K-ERR-1) and any field errors.</summary>
    internal static ProblemHttpResult Problem(KernelError error) => KernelErrorEndpoints.Problem(error);

    private static string IdempotencyKey(HttpContext context) => context.Request.Headers[IdempotencyKeyHeader].ToString().Trim();

    private static ObservationId? ParseObservation(string text) =>
        NodeRef.TryParse(text, out var node) && node.Kind == NodeKinds.Observation ? ObservationId.FromGuid(node.Id) : null;

    private static ProblemHttpResult ObservationRefInvalid(string text) =>
        KernelProblems.BadRequest("Invalid NodeRef", $"'{text}' is not an observation NodeRef (observation:<uuid>).");

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
    IReadOnlyList<ObservationAuditView> Audit)
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
            record.Audit.ToList());
}
