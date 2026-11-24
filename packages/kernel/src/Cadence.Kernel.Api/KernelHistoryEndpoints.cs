using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Audit;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Api;

/// <summary>
/// On-demand object history (<c>GET /api/v1/kernel/nodes/{nodeRef}/history</c>, Canvas PX-12) and individual
/// tamper-evident audit entry inspection (<c>GET /api/v1/kernel/audit-entries/{auditRef}</c>). Both read directly from
/// the kernel's audit, provenance, lineage, revision, assertion, and attestation tables and mask restricted or blinded
/// records in place via <see cref="INodeVisibility"/> (<c>K-AUTH-4</c>, ADR-2471).
/// </summary>
internal static class KernelHistoryEndpoints
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;

    internal static void Map(RouteGroupBuilder reads)
    {
        reads.MapGet("/nodes/{nodeRef}/history", GetNodeHistoryAsync)
            .WithName("KernelGetNodeHistory")
            .WithSummary("The audit, correction, supersession, verification, and attestation timeline of an object.")
            .WithDescription("Reads directly from kernel audit entries, provenance activities, revisions, assertions, and attestations with optional valid_at and known_at bitemporal filters. Entries the caller lacks scope or enrollment.unblind to see are returned as masked placeholders in place (K-AUTH-4, ADR-2471).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reads.MapGet("/audit-entries/{auditRef}", GetAuditEntryAsync)
            .WithName("KernelGetAuditEntry")
            .WithSummary("One tamper-evident kernel audit entry with its provenance, field changes, attestation, and chain hashes.")
            .WithDescription("Returns the audit entry detail or an in-place masked placeholder when the caller lacks scope or enrollment.unblind for the audited object (K-AUTH-4, ADR-2471).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    internal static async Task<Results<Ok<KernelNodeHistoryResponse>, ProblemHttpResult>> GetNodeHistoryAsync(
        string nodeRef,
        [FromQuery(Name = "valid_at")] DateTimeOffset? validAt,
        [FromQuery(Name = "known_at")] DateTimeOffset? knownAt,
        [FromQuery] int? limit,
        HttpContext context,
        IKernelHistoryReader history,
        ICurrentActor currentActor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(nodeRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(nodeRef);
        }

        var pageSize = limit ?? DefaultLimit;
        if (pageSize is < 1 or > MaxLimit)
        {
            return KernelProblems.BadRequest("Invalid limit", $"limit must be between 1 and {MaxLimit}.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var result = await history.ReadNodeHistoryAsync(
                tenant,
                currentActor.Actor,
                node,
                new NodeHistoryWindow(validAt, knownAt, pageSize),
                cancellationToken)
            .ConfigureAwait(false);
        return result is null
            ? KernelProblems.NotFound(node)
            : TypedResults.Ok(KernelNodeHistoryResponse.From(result));
    }

    internal static async Task<Results<Ok<KernelAuditEntryDetailResponse>, ProblemHttpResult>> GetAuditEntryAsync(
        string auditRef,
        HttpContext context,
        IKernelHistoryReader history,
        ICurrentActor currentActor,
        CancellationToken cancellationToken)
    {
        if (!TryParseAuditEntry(auditRef, out var auditEntryId))
        {
            return KernelProblems.BadRequest(
                "Invalid audit entry reference",
                $"'{auditRef}' is not a valid audit_entry NodeRef (expected 'audit_entry:<uuid>').");
        }

        var tenant = KernelRequestTenant.Get(context);
        var detail = await history.FindAuditEntryAsync(tenant, currentActor.Actor, auditEntryId, cancellationToken)
            .ConfigureAwait(false);
        return detail is null
            ? KernelProblems.NotFound(NodeRef.For(auditEntryId))
            : TypedResults.Ok(KernelAuditEntryDetailResponse.From(detail));
    }

    private static bool TryParseAuditEntry(string? text, out AuditEntryId auditEntryId)
    {
        if (NodeRef.TryParse(text, out var node) && node.Kind == NodeKinds.AuditEntry && node.Version is null)
        {
            auditEntryId = AuditEntryId.FromGuid(node.Id);
            return true;
        }

        auditEntryId = default;
        return false;
    }
}

/// <summary>One field-level before/after diff inside an audit or revision entry.</summary>
public sealed record KernelHistoryFieldChangeResponse(
    string Field,
    string? Before,
    string? After)
{
    internal static KernelHistoryFieldChangeResponse From(KernelHistoryFieldChange change) =>
        new(change.Field, change.Before, change.After);
}

/// <summary>An electronic attestation signature linked to an audit or history entry.</summary>
public sealed record KernelHistoryAttestationResponse(
    string AttestationRef,
    string ActorId,
    string? ActorRef,
    string Meaning,
    string Method,
    DateTimeOffset AuthenticatedAt,
    DateTimeOffset RecordedAt,
    string TargetContentHash,
    string? Reason)
{
    internal static KernelHistoryAttestationResponse From(KernelHistoryAttestation attestation) =>
        new(
            attestation.AttestationRef.ToString(),
            attestation.ActorId,
            attestation.ActorRef?.ToString(),
            attestation.Meaning,
            attestation.Method,
            attestation.AuthenticatedAt,
            attestation.RecordedAt,
            attestation.TargetContentHash,
            attestation.Reason);
}

/// <summary>One audit, correction, supersession, verification, or attestation event on an object's history timeline.</summary>
public sealed record KernelHistoryEntryResponse(
    string EntryId,
    string? AuditEntryRef,
    string EventKind,
    string Action,
    string? CommandType,
    string? Outcome,
    DateTimeOffset RecordedAt,
    DateTimeOffset? ValidAt,
    string? ActorId,
    string? ActorKind,
    string? ActorRef,
    string TargetKind,
    string? TargetRef,
    long? Version,
    string? ReasonForChange,
    string? Summary,
    IReadOnlyList<KernelHistoryFieldChangeResponse> FieldChanges,
    KernelHistoryAttestationResponse? Attestation,
    long? Sequence,
    string? Hash,
    string? PredecessorHash,
    string? Mask)
{
    internal static KernelHistoryEntryResponse From(KernelHistoryEntry entry) =>
        new(
            entry.EntryId,
            entry.AuditEntryRef?.ToString(),
            entry.EventKind,
            entry.Action,
            entry.CommandType,
            entry.Outcome,
            entry.RecordedAt,
            entry.ValidAt,
            entry.ActorId,
            entry.ActorKind,
            entry.ActorRef?.ToString(),
            entry.TargetKind,
            entry.TargetRef?.ToString(),
            entry.Version,
            entry.ReasonForChange,
            entry.Summary,
            [.. entry.FieldChanges.Select(KernelHistoryFieldChangeResponse.From)],
            entry.Attestation is { } att ? KernelHistoryAttestationResponse.From(att) : null,
            entry.Sequence,
            entry.Hash,
            entry.PredecessorHash,
            entry.Mask?.ToString());
}

/// <summary>The bitemporal history timeline of a kernel object, with restricted entries masked in place.</summary>
public sealed record KernelNodeHistoryResponse(
    string Node,
    string? Mask,
    DateTimeOffset? ValidAt,
    DateTimeOffset? KnownAt,
    IReadOnlyList<KernelHistoryEntryResponse> Entries)
{
    internal static KernelNodeHistoryResponse From(KernelNodeHistory history) =>
        new(
            history.Node.ToString(),
            history.NodeMask?.ToString(),
            history.ValidAt,
            history.KnownAt,
            [.. history.Entries.Select(KernelHistoryEntryResponse.From)]);
}

/// <summary>Detailed view of one tamper-evident kernel audit entry, or an in-place masked placeholder.</summary>
public sealed record KernelAuditEntryDetailResponse(
    string? AuditEntryRef,
    string TargetKind,
    string? TargetRef,
    string? Action,
    string? CommandType,
    string? Outcome,
    DateTimeOffset RecordedAt,
    DateTimeOffset? ValidAt,
    string? ActorId,
    string? ActorKind,
    string? ActorRef,
    long? Version,
    string? ReasonForChange,
    string? Summary,
    IReadOnlyList<KernelHistoryFieldChangeResponse> FieldChanges,
    KernelHistoryAttestationResponse? Attestation,
    Guid? ActivityId,
    Guid? CorrelationId,
    long? Sequence,
    string? Hash,
    string? PredecessorHash,
    string? Mask)
{
    internal static KernelAuditEntryDetailResponse From(KernelAuditEntryDetail detail) =>
        new(
            detail.AuditEntryRef?.ToString(),
            detail.TargetKind,
            detail.TargetRef?.ToString(),
            detail.Action,
            detail.CommandType,
            detail.Outcome,
            detail.RecordedAt,
            detail.ValidAt,
            detail.ActorId,
            detail.ActorKind,
            detail.ActorRef?.ToString(),
            detail.Version,
            detail.ReasonForChange,
            detail.Summary,
            [.. detail.FieldChanges.Select(KernelHistoryFieldChangeResponse.From)],
            detail.Attestation is { } att ? KernelHistoryAttestationResponse.From(att) : null,
            detail.ActivityId,
            detail.CorrelationId,
            detail.Sequence,
            detail.Hash,
            detail.PredecessorHash,
            detail.Mask?.ToString());
}
