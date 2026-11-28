using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Commands;

internal static class KernelCommandAuditAndOutboxStager
{
    public static void AddProvenance(
        KernelDbContext db,
        CommandExecution execution,
        string outcome,
        ChangedAggregate? aggregate,
        AuthorizationDecision? decision,
        NodeRef? proposal) =>
        db.ProvenanceActivities.Add(new ProvenanceActivityRecord
        {
            Id = execution.ActivityId,
            TenantId = execution.Command.TenantId,
            CommandType = execution.CommandType,
            ActorId = execution.Actor.Id,
            ActorKind = execution.Actor.Kind,
            RequestedBy = execution.Actor.RequestedBy?.Id,
            RecordedAt = execution.RecordedAt,
            ReasonForChange = execution.Command.ReasonForChange,
            IdempotencyKey = execution.Command.IdempotencyKey,
            Outcome = outcome,
            AggregateType = aggregate?.Type,
            AggregateId = aggregate?.Id,
            CorrelationId = execution.Metadata.CorrelationId,
            CausationId = execution.Metadata.CausationId,
            ProposalId = outcome == "Committed" ? proposal?.Id : null,
            Authorization = decision is null
                ? null
                : JsonSerializer.Serialize(
                    new { permitted = decision.Permitted, reason = decision.Reason, conditions = Conditions(decision) },
                    KernelCommandPipelineJson.Options),
        });

    public static void AddEvent(
        KernelDbContext db,
        CommandExecution execution,
        string type,
        int schemaVersion,
        JsonElement payload,
        DateTimeOffset? occurredValid,
        NodeRef? subject = null)
    {
        var envelope = new EventEnvelope(
            Guid.CreateVersion7(),
            type,
            schemaVersion,
            execution.Command.TenantId,
            occurredValid,
            execution.RecordedAt,
            execution.Actor.Id,
            execution.ActivityId,
            execution.Metadata.CorrelationId,
            execution.Metadata.CausationId,
            payload,
            subject);

        db.DomainEvents.Add(new DomainEventRecord
        {
            EventId = envelope.EventId,
            TenantId = envelope.Tenant,
            Type = envelope.Type,
            SchemaVersion = envelope.SchemaVersion,
            OccurredValid = envelope.OccurredValid,
            Recorded = envelope.Recorded,
            ActorId = envelope.Actor,
            ActivityId = envelope.ActivityId,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            Payload = payload.GetRawText(),
        });
        db.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = Guid.CreateVersion7(),
            EventId = envelope.EventId,
            TenantId = envelope.Tenant,
            Type = envelope.Type,
            Envelope = JsonSerializer.Serialize(envelope, KernelCommandPipelineJson.Options),
            EnqueuedAt = envelope.Recorded,
        });
    }

    /// <summary>
    /// Appends one link to the tenant's chain. The head row is created if missing and locked <c>FOR UPDATE</c>, so
    /// concurrent commands for one tenant serialise here and the chain never forks.
    /// </summary>
    public static async Task<AuditEntryRecord> AppendAuditAsync(
        KernelDbContext db,
        CommandExecution execution,
        string action,
        ChangedAggregate? aggregate,
        CancellationToken cancellationToken,
        string? changes = null)
    {
        var tenant = execution.Command.TenantId.Value;
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO kernel.audit_chain_heads (tenant_id, last_sequence, last_hash) VALUES ({tenant}, 0, '') ON CONFLICT (tenant_id) DO NOTHING",
            cancellationToken).ConfigureAwait(false);
        var head = await db.AuditChainHeads
            .FromSql($"SELECT * FROM kernel.audit_chain_heads WHERE tenant_id = {tenant} FOR UPDATE")
            .SingleAsync(cancellationToken).ConfigureAwait(false);

        var link = new AuditEntryRecord
        {
            Id = Guid.CreateVersion7(),
            TenantId = execution.Command.TenantId,
            Sequence = head.LastSequence + 1,
            RecordedAt = execution.RecordedAt,
            ActorId = execution.Actor.Id,
            Action = action,
            AggregateType = aggregate?.Type,
            AggregateId = aggregate?.Id,
            AggregateVersion = aggregate?.Version,
            ReasonForChange = execution.Command.ReasonForChange,
            ActivityId = execution.ActivityId,
            Changes = changes ?? aggregate?.Changes ?? "{}",
            PredecessorHash = head.LastHash,
        };
        link.Hash = KernelAuditHash.Compute(link);

        db.AuditEntries.Add(link);
        head.LastSequence = link.Sequence;
        head.LastHash = link.Hash;
        return link;
    }

    public static async Task StageBiostatExportAuditAsync(
        KernelDbContext db,
        CommandExecution execution,
        string outcome,
        string auditEntryHash,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        switch (execution.Command)
        {
            case ExportStudySnapshot studyExport:
                {
                    var sha = outcome == "Committed"
                        ? NormalizeSha256Hex(studyExport.Content.ContentHash, auditEntryHash)
                        : auditEntryHash;
                    var details = Serialize(new
                    {
                        command = execution.CommandType,
                        outcome,
                        artifactId = studyExport.ArtifactId.Value,
                        artifactVersionId = studyExport.ArtifactVersionId.Value,
                        format = studyExport.Format,
                        lockFilter = studyExport.LockFilter,
                        validAt = studyExport.ValidAt,
                        knownAt = studyExport.KnownAt,
                        isUnblinded = studyExport.IncludesUnblindedData,
                        contentHash = studyExport.Content.ContentHash,
                        auditEntryHash,
                        reasonForChange = studyExport.ReasonForChange,
                        failureReason,
                    });
                    db.BiostatExportAuditLogs.Add(new BiostatExportAuditLog
                    {
                        Id = Guid.CreateVersion7(execution.RecordedAt),
                        TenantId = studyExport.TenantId,
                        StudyId = studyExport.StudyId,
                        EnrollmentId = null,
                        ArtifactId = studyExport.ArtifactId,
                        ActivityId = execution.ActivityId,
                        Timestamp = execution.RecordedAt,
                        UserId = execution.Actor.Id,
                        Action = "EXPORT_STUDY_SNAPSHOT",
                        Outcome = outcome,
                        Format = studyExport.Format,
                        LockFilter = studyExport.LockFilter,
                        ValidAt = studyExport.ValidAt,
                        KnownAt = studyExport.KnownAt,
                        IsUnblinded = studyExport.IncludesUnblindedData,
                        DetailsJson = details,
                        Sha256Hash = sha,
                        CreatedAt = execution.RecordedAt,
                    });
                    break;
                }

            case ExportEnrollmentCasebook casebookExport:
                {
                    var resolvedStudyId = casebookExport.StudyId;
                    if (resolvedStudyId.IsEmpty)
                    {
                        var foundStudyId = await db.Enrollments.AsNoTracking()
                            .Where(e => e.Id == casebookExport.EnrollmentId)
                            .Select(e => (StudyId?)e.StudyId)
                            .FirstOrDefaultAsync(cancellationToken)
                            .ConfigureAwait(false);
                        if (foundStudyId is { } sid)
                        {
                            resolvedStudyId = sid;
                        }
                    }

                    var sha = outcome == "Committed"
                        ? NormalizeSha256Hex(casebookExport.Content.ContentHash, auditEntryHash)
                        : auditEntryHash;
                    var details = Serialize(new
                    {
                        command = execution.CommandType,
                        outcome,
                        enrollmentId = casebookExport.EnrollmentId.Value,
                        artifactId = casebookExport.ArtifactId.Value,
                        artifactVersionId = casebookExport.ArtifactVersionId.Value,
                        format = casebookExport.Format,
                        validAt = casebookExport.ValidAt,
                        knownAt = casebookExport.KnownAt,
                        isUnblinded = casebookExport.IncludesUnblindedData,
                        contentHash = casebookExport.Content.ContentHash,
                        auditEntryHash,
                        reasonForChange = casebookExport.ReasonForChange,
                        failureReason,
                    });
                    db.BiostatExportAuditLogs.Add(new BiostatExportAuditLog
                    {
                        Id = Guid.CreateVersion7(execution.RecordedAt),
                        TenantId = casebookExport.TenantId,
                        StudyId = resolvedStudyId,
                        EnrollmentId = casebookExport.EnrollmentId,
                        ArtifactId = casebookExport.ArtifactId,
                        ActivityId = execution.ActivityId,
                        Timestamp = execution.RecordedAt,
                        UserId = execution.Actor.Id,
                        Action = "EXPORT_ENROLLMENT_CASEBOOK",
                        Outcome = outcome,
                        Format = casebookExport.Format,
                        LockFilter = null,
                        ValidAt = casebookExport.ValidAt,
                        KnownAt = casebookExport.KnownAt,
                        IsUnblinded = casebookExport.IncludesUnblindedData,
                        DetailsJson = details,
                        Sha256Hash = sha,
                        CreatedAt = execution.RecordedAt,
                    });
                    break;
                }
        }
    }

    public static JsonElement BuildDenialPayload(CommandExecution execution, AuthorizationDecision denial) =>
        JsonSerializer.SerializeToElement(
            new
            {
                command = execution.CommandType,
                idempotencyKey = execution.Command.IdempotencyKey,
                reason = denial.Reason,
                conditions = Conditions(denial),
            },
            KernelCommandPipelineJson.Options);

    public static JsonElement BuildRefusalPayload(CommandExecution execution, KernelError refusal) =>
        JsonSerializer.SerializeToElement(
            new
            {
                command = execution.CommandType,
                idempotencyKey = execution.Command.IdempotencyKey,
                code = refusal.Code,
                reason = refusal.Message,
                producer = refusal.Explanation?.Producer,
                conditions = refusal.Explanation?.Conditions.Select(c => new
                {
                    c.Code,
                    c.Description,
                    c.Met,
                    evidence = c.Evidence.Select(e => e.ToString()),
                }),
            },
            KernelCommandPipelineJson.Options);

    public static KernelEventAttribute EventAttribute(Type eventType) =>
        eventType.GetCustomAttribute<KernelEventAttribute>()
        ?? throw new InvalidOperationException($"{eventType.Name} must declare [KernelEvent(type, schemaVersion)].");

    private static object Conditions(AuthorizationDecision decision) =>
        decision.Conditions.Select(c => new { c.Code, c.Description, c.Met, evidence = c.Evidence.Select(e => e.ToString()) });

    private static string Serialize(object value) => JsonSerializer.Serialize(value, KernelCommandPipelineJson.Options);

    private static string NormalizeSha256Hex(string? candidate, string fallback)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return fallback;
        }

        var trimmed = candidate.StartsWith(ContentHash.Prefix, StringComparison.OrdinalIgnoreCase)
            ? candidate[ContentHash.Prefix.Length..]
            : candidate;
        return trimmed.Length <= 64 ? trimmed : trimmed[..64];
    }
}

/// <summary>JSON options shared by kernel persistence for event payloads, envelopes and audit change sets.</summary>
internal static class KernelCommandPipelineJson
{
    // jsonb reorders object members, so a polymorphic value's "type" member may come back after its other members.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };
}
