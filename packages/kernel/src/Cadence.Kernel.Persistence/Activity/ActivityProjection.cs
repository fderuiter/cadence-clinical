using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Activity;
using Cadence.Kernel.Application.Activity;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Persistence.Audit;
using Cadence.Kernel.Persistence.Background;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace Cadence.Kernel.Persistence.Activity;

/// <summary>
/// One row of <c>kernel.activity_entries</c> (ADR-2485): a domain event as people see it, with the zoom path of its
/// subject at the time it happened, so a study's activity includes everything recorded under it. Insert-only.
/// </summary>
internal sealed class ActivityEntryRecord
{
    public TenantId TenantId { get; init; }
    public Guid EventId { get; init; }
    public DateTimeOffset Recorded { get; init; }
    public DateTimeOffset? OccurredValid { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Family { get; init; } = string.Empty;
    public string? SubjectKind { get; init; }
    public string? Subject { get; init; }

    /// <summary>Unversioned NodeRefs of the subject and every object above it on its zoom path, plus the tenant.</summary>
    public string[] Scope { get; init; } = [];

    public Guid CorrelationId { get; init; }
    public string? ReasonForChange { get; init; }
    public string? AuditEntryRef { get; init; }
    public string? AttestationRef { get; init; }
}

/// <summary>EF Core mapping for <see cref="ActivityEntryRecord"/>; RLS and the insert-only guards come from the migration.</summary>
internal sealed class ActivityEntryConfiguration : IEntityTypeConfiguration<ActivityEntryRecord>
{
    public void Configure(EntityTypeBuilder<ActivityEntryRecord> builder)
    {
        builder.ToTable("activity_entries");
        builder.HasKey(x => new { x.TenantId, x.EventId });
        builder.Property(x => x.ActorId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Family).HasMaxLength(32).IsRequired();
        builder.Property(x => x.SubjectKind).HasMaxLength(48);
        builder.Property(x => x.Subject).HasMaxLength(128);
        builder.Property(x => x.Scope).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(2000);
        builder.Property(x => x.AuditEntryRef).HasMaxLength(128);
        builder.Property(x => x.AttestationRef).HasMaxLength(128);
        builder.HasIndex(x => new { x.TenantId, x.Recorded, x.EventId }).IsDescending(false, true, true);
        builder.HasIndex(x => x.Scope).HasMethod("gin");
        builder.HasOne<Domain.Entities.Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Projects domain events from the outbox into <c>activity_entries</c>, one tenant at a time (ADR-2485 §4.2). It is
/// idempotent (an event already projected is skipped) and re-reads a look-back window before its high-water mark, so an
/// event whose transaction committed late is still projected. It never changes the outbox or the events.
/// </summary>
internal sealed class KernelActivityProjector(
    KernelTenantDbContextFactory contexts,
    IKernelNavigator navigator,
    KernelBackgroundOptions options)
{
    /// <summary>Projects every tenant; returns how many entries were written.</summary>
    public async Task<int> ProjectAllAsync(CancellationToken cancellationToken)
    {
        var written = 0;
        foreach (var tenant in await contexts.ListTenantsAsync(cancellationToken).ConfigureAwait(false))
        {
            written += await ProjectAsync(tenant, cancellationToken).ConfigureAwait(false);
        }

        return written;
    }

    /// <summary>Projects the tenant's pending events, in batches, until none are left; returns how many were written.</summary>
    public async Task<int> ProjectAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        var written = 0;
        while (true)
        {
            var (batchWritten, fullBatch) = await ProjectBatchAsync(tenant, cancellationToken).ConfigureAwait(false);
            written += batchWritten;
            if (!fullBatch)
            {
                return written;
            }
        }
    }

    private async Task<(int Written, bool FullBatch)> ProjectBatchAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var entries = db.Set<ActivityEntryRecord>();
        var highWater = await entries.AsNoTracking()
            .Where(a => a.TenantId == tenant)
            .MaxAsync(a => (DateTimeOffset?)a.Recorded, cancellationToken)
            .ConfigureAwait(false);
        var since = highWater is { } mark ? mark - options.ActivityLookBack : DateTimeOffset.MinValue;

        var pending = await db.OutboxMessages.AsNoTracking()
            .Where(o => o.TenantId == tenant && o.EnqueuedAt >= since
                && !entries.Any(a => a.TenantId == tenant && a.EventId == o.EventId))
            .OrderBy(o => o.EnqueuedAt).ThenBy(o => o.Id)
            .Take(options.ActivityBatchSize)
            .Select(o => o.Envelope)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return (0, false);
        }

        var fullBatch = pending.Count == options.ActivityBatchSize;
        var envelopes = pending
            .Select(json => JsonSerializer.Deserialize<EventEnvelope>(json, KernelCommandPipelineJson.Options)
                ?? throw new InvalidOperationException("An outbox row holds no event envelope."))
            .ToList();

        var activityIds = envelopes
            .Where(e => e.ActivityId != Guid.Empty)
            .Select(e => e.ActivityId)
            .Distinct()
            .ToList();

        var provenanceReasons = activityIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.ProvenanceActivities.AsNoTracking()
                .Where(p => p.TenantId == tenant && activityIds.Contains(p.Id))
                .Select(p => new { p.Id, p.ReasonForChange })
                .ToDictionaryAsync(p => p.Id, p => p.ReasonForChange, cancellationToken)
                .ConfigureAwait(false);

        var auditRows = activityIds.Count == 0
            ? []
            : await db.AuditEntries.AsNoTracking()
                .Where(a => a.TenantId == tenant && a.ActivityId != Guid.Empty && activityIds.Contains(a.ActivityId))
                .OrderBy(a => a.Sequence)
                .Select(a => new
                {
                    a.Id,
                    a.ActivityId,
                    a.AggregateType,
                    a.AggregateId,
                    a.ReasonForChange,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var auditsByActivity = auditRows
            .GroupBy(a => a.ActivityId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var assertionIds = envelopes
            .Where(e => e.Subject?.Kind == NodeKinds.Assertion)
            .Select(e => AssertionId.FromGuid(e.Subject!.Id))
            .Distinct()
            .ToList();
        var assertionAttestations = assertionIds.Count == 0
            ? new Dictionary<Guid, AttestationId>()
            : (await db.Assertions.AsNoTracking()
                    .Where(a => a.TenantId == tenant && assertionIds.Contains(a.Id) && a.AttestationId != null)
                    .Select(a => new { a.Id, AttestationId = a.AttestationId!.Value })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(a => a.Id.Value, a => a.AttestationId);

        var protocolVersionIds = envelopes
            .Where(e => e.Subject?.Kind == NodeKinds.ProtocolVersion && e.Type == "ProtocolVersionApproved")
            .Select(e => ProtocolVersionId.FromGuid(e.Subject!.Id))
            .Distinct()
            .ToList();
        var protocolAttestations = protocolVersionIds.Count == 0
            ? new Dictionary<Guid, AttestationId>()
            : (await db.ProtocolVersions.AsNoTracking()
                    .Where(p => p.TenantId == tenant && protocolVersionIds.Contains(p.Id) && p.ApprovalAttestationId != null)
                    .Select(p => new { p.Id, AttestationId = p.ApprovalAttestationId!.Value })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(p => p.Id.Value, p => p.AttestationId);

        var paths = new Dictionary<NodeRef, string[]>();
        var staged = new List<ActivityEntryRecord>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            var subject = envelope.Subject;
            var unversioned = subject?.Unversioned();
            if (unversioned is not null && !paths.ContainsKey(unversioned))
            {
                paths[unversioned] = await ScopeAsync(tenant, unversioned, cancellationToken).ConfigureAwait(false);
            }

            string? reasonForChange = null;
            if (provenanceReasons.TryGetValue(envelope.ActivityId, out var provReason)
                && !string.IsNullOrWhiteSpace(provReason))
            {
                reasonForChange = provReason;
            }

            string? auditEntryRef = null;
            string? attestationRef = subject?.Kind == NodeKinds.Attestation
                ? subject.Unversioned().ToString()
                : null;

            if (auditsByActivity.TryGetValue(envelope.ActivityId, out var activityAudits))
            {
                var subjectIdText = subject?.Id.ToString();
                var matchingAudit = activityAudits.FirstOrDefault(a =>
                        subject is not null
                        && a.AggregateId == subjectIdText
                        && KernelAggregateKinds.TryMapAggregateTypeToKind(a.AggregateType) == subject.Kind)
                    ?? activityAudits.FirstOrDefault(a =>
                        KernelAggregateKinds.TryMapAggregateTypeToKind(a.AggregateType) != NodeKinds.Attestation)
                    ?? activityAudits[0];

                auditEntryRef = NodeRef.For(AuditEntryId.FromGuid(matchingAudit.Id)).ToString();
                if (reasonForChange is null && !string.IsNullOrWhiteSpace(matchingAudit.ReasonForChange))
                {
                    reasonForChange = matchingAudit.ReasonForChange;
                }

                if (attestationRef is null)
                {
                    var attAudit = activityAudits.FirstOrDefault(a =>
                        KernelAggregateKinds.TryMapAggregateTypeToKind(a.AggregateType) == NodeKinds.Attestation
                        && Guid.TryParse(a.AggregateId, out var g) && g != Guid.Empty);
                    if (attAudit is not null && AttestationId.TryParse(attAudit.AggregateId, out var attId))
                    {
                        attestationRef = NodeRef.For(attId).ToString();
                    }
                }
            }

            if (attestationRef is null && subject?.Kind == NodeKinds.Assertion
                && assertionAttestations.TryGetValue(subject.Id, out var assertionAttId))
            {
                attestationRef = NodeRef.For(assertionAttId).ToString();
            }
            else if (attestationRef is null && subject?.Kind == NodeKinds.ProtocolVersion
                && protocolAttestations.TryGetValue(subject.Id, out var protocolAttId))
            {
                attestationRef = NodeRef.For(protocolAttId).ToString();
            }

            var record = new ActivityEntryRecord
            {
                TenantId = tenant,
                EventId = envelope.EventId,
                Recorded = envelope.Recorded,
                OccurredValid = envelope.OccurredValid,
                ActorId = envelope.Actor,
                Type = envelope.Type,
                Family = KernelEventFamilies.Of(envelope.Type).ToString(),
                SubjectKind = subject?.Kind,
                Subject = subject?.ToString(),
                Scope = unversioned is null ? [NodeRef.For(tenant).ToString()] : paths[unversioned],
                CorrelationId = envelope.CorrelationId,
                ReasonForChange = reasonForChange,
                AuditEntryRef = auditEntryRef,
                AttestationRef = attestationRef,
            };
            staged.Add(record);
            entries.Add(record);
        }

        try
        {
            using (db.PermitPipelineWrites())
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return (staged.Count, fullBatch);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return (await ProjectIndividuallyAsync(tenant, staged, cancellationToken).ConfigureAwait(false), fullBatch);
        }
    }

    private async Task<int> ProjectIndividuallyAsync(
        TenantId tenant,
        IReadOnlyList<ActivityEntryRecord> staged,
        CancellationToken cancellationToken)
    {
        var written = 0;
        foreach (var record in staged)
        {
            await using var singleDb = contexts.Create(tenant);
            var alreadyProjected = await singleDb.Set<ActivityEntryRecord>().AsNoTracking()
                .AnyAsync(a => a.TenantId == tenant && a.EventId == record.EventId, cancellationToken)
                .ConfigureAwait(false);
            if (alreadyProjected)
            {
                continue;
            }

            singleDb.Set<ActivityEntryRecord>().Add(record);
            try
            {
                using (singleDb.PermitPipelineWrites())
                {
                    await singleDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                written++;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Another concurrent projector inserted this event first; idempotently skip.
            }
        }

        return written;
    }

    /// <summary>The tenant, every object on the subject's zoom path, and the subject itself, unversioned.</summary>
    private async Task<string[]> ScopeAsync(TenantId tenant, NodeRef subject, CancellationToken cancellationToken)
    {
        var scope = new List<string> { NodeRef.For(tenant).ToString() };
        var path = await navigator.GetZoomPathAsync(tenant, subject, cancellationToken).ConfigureAwait(false);
        foreach (var step in path ?? [])
        {
            if (step.Ref?.Unversioned().ToString() is { } node && !scope.Contains(node))
            {
                scope.Add(node);
            }
        }

        var self = subject.ToString();
        if (!scope.Contains(self))
        {
            scope.Add(self);
        }

        return [.. scope];
    }
}

/// <summary>Reads <c>activity_entries</c> through a tenant-bound context (RLS), newest first.</summary>
internal sealed class KernelActivityStore(KernelTenantDbContextFactory contexts) : IActivityStore
{
    public async Task<IReadOnlyList<ActivityEntry>> QueryAsync(TenantId tenant, ActivityQuery query, int take, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var db = contexts.Create(tenant);
        var rows = db.Set<ActivityEntryRecord>().AsNoTracking().Where(a => a.TenantId == tenant);
        if (query.Scope is { } scope)
        {
            var node = scope.Unversioned().ToString();
            rows = rows.Where(a => a.Scope.Contains(node));
        }

        if (query.Families is { Count: > 0 } families)
        {
            var names = families.Select(f => f.ToString()).ToList();
            rows = rows.Where(a => names.Contains(a.Family));
        }

        if (query.ActorId is { } actorId)
        {
            rows = rows.Where(a => a.ActorId == actorId);
        }

        if (query.From is { } from)
        {
            rows = rows.Where(a => a.Recorded >= from);
        }

        if (query.Before is { } before)
        {
            rows = rows.Where(a => a.Recorded < before.Recorded || (a.Recorded == before.Recorded && a.EventId.CompareTo(before.EventId) < 0));
        }

        var page = await rows
            .OrderByDescending(a => a.Recorded).ThenByDescending(a => a.EventId)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. page.Select(a => new ActivityEntry(
            a.EventId,
            a.Type,
            Enum.Parse<EventFamily>(a.Family),
            a.Recorded,
            a.OccurredValid,
            a.ActorId,
            a.SubjectKind,
            a.Subject is null ? null : NodeRef.Parse(a.Subject),
            Masked: null,
            a.CorrelationId,
            a.ReasonForChange,
            a.AuditEntryRef is null ? null : NodeRef.Parse(a.AuditEntryRef),
            a.AttestationRef is null ? null : NodeRef.Parse(a.AttestationRef)))];
    }
}

/// <summary>Runs <see cref="KernelActivityProjector.ProjectAllAsync"/> on <see cref="KernelBackgroundOptions.ActivityInterval"/>.</summary>
internal sealed partial class KernelActivityProjectionService(
    IServiceScopeFactory scopes,
    KernelBackgroundOptions options,
    ILogger<KernelActivityProjectionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<KernelActivityProjector>().ProjectAllAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogPassFailed(logger, ex);
            }

            await KernelOutboxRelayService.Delay(options.ActivityInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Kernel activity projection pass failed; retrying after the activity interval.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);
}

/// <summary>Registers the activity projection.</summary>
public static class KernelActivityPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IActivityStore"/> and the projector. <paramref name="runProjector"/> also runs it as a
    /// hosted service. Requires <c>AddKernelPersistence</c> and the navigator (<c>AddKernelNavigation</c>,
    /// <c>AddKernelScopeGraph</c>).
    /// </summary>
    public static IServiceCollection AddKernelActivityProjection(this IServiceCollection services, bool runProjector = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new KernelBackgroundOptions());
        services.TryAddScoped<IActivityStore, KernelActivityStore>();
        services.TryAddScoped<KernelActivityProjector>();
        if (runProjector)
        {
            services.AddHostedService<KernelActivityProjectionService>();
        }

        return services;
    }
}
