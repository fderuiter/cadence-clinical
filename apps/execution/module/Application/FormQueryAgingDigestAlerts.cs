using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Modules.Execution.Application;

/// <summary>
/// De-identified alert payload dispatched to study leads (<c>study_lead</c>) and site investigators (<c>site_investigator</c>)
/// when a daily <see cref="FormQueryAgingDigest"/> commits (<c>PRD-QRY-002</c>). Blinded treatment items are always redacted
/// (<see cref="FormQueryAgingDigestEntry.MaskBlinded"/>), and recipients are identified strictly by pseudonymous actor IDs.
/// </summary>
public sealed record FormQueryAgingDigestAlert(
    string IdempotencyKey,
    TenantId TenantId,
    Guid DigestId,
    StudyId StudyId,
    StudySiteId? StudySiteId,
    string ScopeKey,
    string RecipientRole,
    IReadOnlyList<string> RecipientActorIds,
    DateOnly DigestDate,
    DateTimeOffset GeneratedAt,
    string StudyCode,
    string? SiteCode,
    int EscalatedCount,
    IReadOnlyList<FormQueryAgingDigestEntry> Entries);

/// <summary>
/// Outbound port for delivering idempotent, de-identified query aging digest alerts within the kernel composition host
/// without routing through standalone <c>apps/notifications</c>.
/// </summary>
public interface IFormQueryAgingDigestAlertDispatcher
{
    /// <summary>
    /// Dispatches <paramref name="alert"/> once per <see cref="FormQueryAgingDigestAlert.IdempotencyKey"/>; returns
    /// <see langword="true"/> on first delivery and <see langword="false"/> when a prior delivery already recorded it.
    /// </summary>
    Task<bool> SendAsync(FormQueryAgingDigestAlert alert, CancellationToken cancellationToken = default);

    /// <summary>Returns recorded de-identified alerts, optionally filtered by tenant and study.</summary>
    IReadOnlyList<FormQueryAgingDigestAlert> GetSentAlerts(TenantId? tenantId = null, StudyId? studyId = null);
}

/// <summary>
/// Default host-local alert sink that deduplicates by <see cref="FormQueryAgingDigestAlert.IdempotencyKey"/> and emits a
/// structured PII-free operational log entry on initial dispatch.
/// </summary>
internal sealed partial class InMemoryFormQueryAgingDigestAlertDispatcher(
    ILogger<InMemoryFormQueryAgingDigestAlertDispatcher> logger) : IFormQueryAgingDigestAlertDispatcher
{
    private readonly ConcurrentDictionary<string, FormQueryAgingDigestAlert> _alerts = new(StringComparer.Ordinal);

    public Task<bool> SendAsync(FormQueryAgingDigestAlert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);
        if (string.IsNullOrWhiteSpace(alert.IdempotencyKey))
        {
            throw new ArgumentException("Alert idempotency key is required.", nameof(alert));
        }

        var added = _alerts.TryAdd(alert.IdempotencyKey, alert);
        if (added)
        {
            LogAlertDispatched(
                logger,
                alert.TenantId.Value,
                alert.DigestId,
                alert.StudyCode,
                alert.ScopeKey,
                alert.RecipientRole,
                alert.RecipientActorIds.Count,
                alert.EscalatedCount);
        }

        return Task.FromResult(added);
    }

    public IReadOnlyList<FormQueryAgingDigestAlert> GetSentAlerts(TenantId? tenantId = null, StudyId? studyId = null) =>
    [
        .. _alerts.Values
            .Where(a => (tenantId is null || a.TenantId == tenantId.Value)
                && (studyId is null || a.StudyId == studyId.Value))
            .OrderBy(a => a.DigestDate)
            .ThenBy(a => a.ScopeKey, StringComparer.Ordinal),
    ];

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Dispatched de-identified query aging digest alert {DigestId} in tenant {TenantId} for study {StudyCode} ({ScopeKey}, role={RecipientRole}, recipients={RecipientCount}, escalated={EscalatedCount}).")]
    private static partial void LogAlertDispatched(
        ILogger logger,
        Guid tenantId,
        Guid digestId,
        string studyCode,
        string scopeKey,
        string recipientRole,
        int recipientCount,
        int escalatedCount);
}

/// <summary>
/// Outbox consumer for <see cref="FormQueryAgingDigestGenerated"/>: loads the persisted digest, redacts all blinded
/// treatment items unconditionally (<c>K-AUTH-4</c>), resolves pseudonymous site/study lead recipients via
/// <see cref="ILeadAlertRecipientDirectory"/>, and dispatches an idempotent de-identified alert (<c>PRD-QRY-002</c>).
/// </summary>
internal sealed class SendFormQueryAgingDigestAlerts(
    IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
    ICollectionInstanceRecords forms,
    IFormQueryAgingDigestAlertDispatcher dispatcher,
    ILeadAlertRecipientDirectory? leadRecipients = null) : KernelEventConsumer<FormQueryAgingDigestGenerated>
{
    /// <summary>The consumer's name, and so its service account <c>svc:execution-query-digest-alerts</c>.</summary>
    public const string Name = "execution-query-digest-alerts";

    protected override async Task HandleAsync(
        FormQueryAgingDigestGenerated payload,
        KernelEventDelivery delivery,
        CancellationToken cancellationToken)
    {
        FormQueryAgingDigest? digest;
        await using (var db = contexts.Create(payload.TenantId))
        {
            digest = await db.FormQueryAgingDigests
                .AsNoTracking()
                .SingleOrDefaultAsync(d => d.Id == payload.DigestId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (digest is null)
        {
            return;
        }

        var redactedEntries = (await FormQueryDigestBlinding.RedactBatchAsync(
                payload.TenantId,
                [digest.Entries],
                forms,
                contexts,
                access: null,
                alwaysMaskBlinded: true,
                cancellationToken)
            .ConfigureAwait(false))[0];

        IReadOnlyList<string> recipients = [];
        if (leadRecipients is not null)
        {
            recipients = digest.StudySiteId is { } siteId
                ? await leadRecipients.ListSiteLeadsAsync(payload.TenantId, digest.StudyId, siteId, cancellationToken).ConfigureAwait(false)
                : await leadRecipients.ListStudyLeadsAsync(payload.TenantId, digest.StudyId, cancellationToken).ConfigureAwait(false);
        }

        var alert = new FormQueryAgingDigestAlert(
            IdempotencyKey: $"query-digest-alert:{payload.TenantId.Value:D}:{digest.Id:D}",
            TenantId: payload.TenantId,
            DigestId: digest.Id,
            StudyId: digest.StudyId,
            StudySiteId: digest.StudySiteId,
            ScopeKey: digest.ScopeKey,
            RecipientRole: digest.RecipientRole,
            RecipientActorIds: recipients,
            DigestDate: digest.DigestDate,
            GeneratedAt: digest.GeneratedAt,
            StudyCode: digest.StudyCode,
            SiteCode: digest.SiteCode,
            EscalatedCount: digest.EscalatedCount,
            Entries: redactedEntries);

        await dispatcher.SendAsync(alert, cancellationToken).ConfigureAwait(false);
    }
}
