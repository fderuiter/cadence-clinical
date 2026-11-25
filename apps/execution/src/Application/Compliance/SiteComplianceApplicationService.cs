using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;
using Cadence.Kernel.Time;

namespace Cadence.Apps.Execution.Application.Compliance;

/// <summary>
/// <see cref="ISiteComplianceApplicationService"/> (MIG-SUBJ-020, `02_technical_design.md` §3.1-§3.3,
/// `01_spec.md` §1.1). Orchestrates <see cref="SiteComplianceCache"/> (MIG-SUBJ-006) through
/// <see cref="ISiteComplianceRepository"/> (MIG-SUBJ-013, extended by this ticket with the write path it
/// deferred — see that interface's own doc comment) and <see cref="IAuditLogWriter"/> (MIG-SUBJ-017).
/// <para>
/// Three deliberately asymmetric behaviors, all load-bearing acceptance criteria — do not "fix" any of them:
/// <list type="bullet">
/// <item><see cref="ApplyEtmfWebhookAsync(EtmfCompletenessWebhookPayload, CancellationToken)"/> writes an <c>ETMF_COMPLIANCE_UPDATE</c> audit entry whenever it
/// changes a row, attributed to the calling eTMF integration (#4767; the legacy wrote none).</item>
/// <item><see cref="ActivateSiteAsync"/>'s blocked path writes its <c>BLOCKED_ACTIVATION</c> audit entry (and
/// commits it via <see cref="ISiteComplianceRepository.SaveChangesAsync"/>) <b>before</b> returning the
/// failure result — matching the legacy's "own nested session, committed independently of the outer one"
/// note.</item>
/// <item><see cref="GetComplianceStatusAsync"/> has no failure path whatsoever: it returns
/// <see cref="ComplianceStatusResponse"/> directly, always succeeding, even when no cache row
/// exists.</item>
/// </list>
/// </para>
/// Legacy Python source being replaced: <c>apps/execution/presentation/routers/subjects.py</c>
/// (<c>etmf_completeness_webhook</c>, <c>activate_site_endpoint</c>, <c>get_site_compliance_status</c>).
/// @req:PRD-SUB-001
/// </summary>
public sealed class SiteComplianceApplicationService(
    ISiteComplianceRepository compliance,
    IAuditLogWriter auditLog,
    ICurrentAuditContextAccessor auditContext,
    IClock clock,
    IKernelLockGuard? lockGuard = null) : ISiteComplianceApplicationService
{
    // The audit chain key for every entry this service writes: the aggregate's CLR type name (#4768).
    private const string SiteComplianceEntityName = nameof(SiteComplianceCache);
    private const string SiteActivationMilestone = "SITE_ACTIVATION";

    public Task<Result<bool>> ApplyEtmfWebhookAsync(EtmfCompletenessWebhookPayload payload, CancellationToken ct) =>
        ApplyEtmfWebhookAsync(payload.StudyId, payload.Milestone, batchSites: null, singlePayload: payload, ct);

    public Task<Result<bool>> ApplyEtmfWebhookBatchAsync(EtmfCompletenessBatchWebhookPayload payload, CancellationToken ct) =>
        ApplyEtmfWebhookAsync(payload.StudyId.Value, payload.Milestone, batchSites: payload.Sites, singlePayload: null, ct);

    private async Task<Result<bool>> ApplyEtmfWebhookAsync(
        string studyId,
        string milestone,
        IReadOnlyList<EtmfSiteCompletenessItem>? batchSites,
        EtmfCompletenessWebhookPayload? singlePayload,
        CancellationToken ct)
    {
        if (singlePayload is not null)
        {
            if (lockGuard is not null)
            {
                var lockCheck = await lockGuard.EnsureUnlockedAsync(singlePayload.StudyId, singlePayload.SiteId, cancellationToken: ct)
                    .ConfigureAwait(false);
                if (lockCheck.IsFailure)
                {
                    return Result<bool>.Failure(lockCheck.Error!);
                }
            }

            var studyCode = new StudyCode(singlePayload.StudyId);
            SiteCode? siteCode = singlePayload.SiteId is null ? null : new SiteCode(singlePayload.SiteId);
            IReadOnlyList<string> missingDocuments = singlePayload.MissingArtifacts ?? Array.Empty<string>();

            var existing = await compliance.GetAsync(studyCode, siteCode, singlePayload.Milestone, ct).ConfigureAwait(false);
            SiteComplianceCache? changed = null;
            if (existing is not null)
            {
                existing.ApplyWebhookUpdate(singlePayload.IsComplete, missingDocuments);
                changed = existing;
            }
            else if (siteCode is { } concreteSiteCode)
            {
                var created = SiteComplianceCache.Create(
                    studyCode, concreteSiteCode, singlePayload.Milestone, singlePayload.IsComplete, missingDocuments);
                await compliance.AddAsync(created, ct).ConfigureAwait(false);
                changed = created;
            }

            if (changed is not null)
            {
                changed.ReasonForChange = auditContext.ChangeReason;
                await compliance.RecordActivationPeriodAsync(changed, clock.UtcNow, auditContext.ChangeReason, ct).ConfigureAwait(false);
                await auditLog.WriteAsync(SiteComplianceEntityName, changed.Id.ToString(), "ETMF_COMPLIANCE_UPDATE",
                    auditContext.UserId, auditContext.ChangeReason, oldValues: null,
                    newValues: new Dictionary<string, object?>
                    {
                        ["milestone"] = singlePayload.Milestone,
                        ["is_complete"] = singlePayload.IsComplete,
                        ["missing_documents"] = missingDocuments,
                    }, ct).ConfigureAwait(false);
            }
        }
        else
        {
            var items = batchSites ?? [];
            var normalizedOrder = new List<SiteCode>(items.Count);
            var latestBySite = new Dictionary<SiteCode, EtmfSiteCompletenessItem>(items.Count);
            foreach (var item in items)
            {
                var siteCode = item.SiteId;
                if (!latestBySite.ContainsKey(siteCode))
                {
                    normalizedOrder.Add(siteCode);
                }

                latestBySite[siteCode] = item;
            }

            if (lockGuard is not null)
            {
                var siteIdStrings = normalizedOrder.Select(s => s.Value).ToList();
                var lockCheck = await lockGuard.EnsureUnlockedBatchAsync(studyId, siteIdStrings, ct)
                    .ConfigureAwait(false);
                if (lockCheck.IsFailure)
                {
                    return Result<bool>.Failure(lockCheck.Error!);
                }
            }

            if (normalizedOrder.Count == 0)
            {
                return true;
            }

            var studyCode = new StudyCode(studyId);
            var existingBySite = await compliance.GetBySitesAsync(studyCode, normalizedOrder, milestone, ct)
                .ConfigureAwait(false);

            var changedCaches = new List<(SiteComplianceCache Cache, bool IsComplete, IReadOnlyList<string> MissingDocuments)>(normalizedOrder.Count);
            var existingModifiedIds = new HashSet<Guid>();

            foreach (var siteCode in normalizedOrder)
            {
                var item = latestBySite[siteCode];
                IReadOnlyList<string> missingDocuments = item.MissingArtifacts ?? Array.Empty<string>();

                if (existingBySite.TryGetValue(siteCode, out var existing))
                {
                    if (existing.IsComplete == item.IsComplete
                        && existing.MissingDocuments.SequenceEqual(missingDocuments, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    existing.ApplyWebhookUpdate(item.IsComplete, missingDocuments);
                    existing.ReasonForChange = auditContext.ChangeReason;
                    existingModifiedIds.Add(existing.Id);
                    changedCaches.Add((existing, item.IsComplete, missingDocuments));
                }
                else
                {
                    var created = SiteComplianceCache.Create(
                        studyCode, siteCode, milestone, item.IsComplete, missingDocuments);
                    created.ReasonForChange = auditContext.ChangeReason;
                    await compliance.AddAsync(created, ct).ConfigureAwait(false);
                    changedCaches.Add((created, item.IsComplete, missingDocuments));
                }
            }

            if (changedCaches.Count == 0)
            {
                return true;
            }

            DateTimeOffset now = clock.UtcNow;
            await compliance.RecordActivationPeriodsBatchAsync(
                changedCaches.Select(x => x.Cache).ToList(),
                existingModifiedIds,
                now,
                auditContext.ChangeReason,
                ct).ConfigureAwait(false);

            foreach (var (cache, isComplete, missingDocuments) in changedCaches)
            {
                await auditLog.WriteAsync(SiteComplianceEntityName, cache.Id.ToString(), "ETMF_COMPLIANCE_UPDATE",
                    auditContext.UserId, auditContext.ChangeReason, oldValues: null,
                    newValues: new Dictionary<string, object?>
                    {
                        ["milestone"] = milestone,
                        ["is_complete"] = isComplete,
                        ["missing_documents"] = missingDocuments,
                    }, ct).ConfigureAwait(false);
            }
        }

        await compliance.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<Result<bool>> ActivateSiteAsync(string siteId, SiteActivationRequest request, CancellationToken ct)
    {
        if (lockGuard is not null)
        {
            var lockCheck = await lockGuard.EnsureUnlockedAsync(request.StudyId, siteId, cancellationToken: ct)
                .ConfigureAwait(false);
            if (lockCheck.IsFailure)
            {
                return Result<bool>.Failure(lockCheck.Error!);
            }
        }

        var studyCode = new StudyCode(request.StudyId);
        var siteCode = new SiteCode(siteId);

        var gate = await compliance.GetAsync(studyCode, siteCode, SiteActivationMilestone, ct);
        if (gate is not { IsComplete: true })
        {
            // The BLOCKED_ACTIVATION audit entry must be written -- and committed -- before the failure
            // result is returned (01_spec.md §1.1, raise site #9). IAuditLogWriter.WriteAsync does not
            // itself call SaveChangesAsync, so the explicit SaveChangesAsync below is what actually commits
            // it; both calls are awaited in order, ahead of the `return`.
            await auditLog.WriteAsync(SiteComplianceEntityName, siteId, "BLOCKED_ACTIVATION",
                auditContext.UserId, auditContext.ChangeReason, oldValues: null, newValues: null, ct);
            await compliance.SaveChangesAsync(ct);

            return SubjectErrors.BlockedActivation(siteId);
        }

        // Activation is stateful and idempotent: if already activated, return success without writing a
        // duplicate SITE_ACTIVATION audit row. ActivateSiteAsync never flips IsComplete or increments Version.
        if (!gate.MarkActivated(clock.UtcNow, auditContext.UserId))
        {
            return true;
        }

        gate.ReasonForChange = auditContext.ChangeReason;
        await compliance.RecordActivationPeriodAsync(gate, clock.UtcNow, auditContext.ChangeReason, ct);
        await auditLog.WriteAsync(SiteComplianceEntityName, gate.Id.ToString(), "SITE_ACTIVATION",
            auditContext.UserId, auditContext.ChangeReason, oldValues: null,
            newValues: new Dictionary<string, object?>
            {
                ["site_id"] = siteId,
                ["is_activated"] = true,
                ["activated_at"] = gate.ActivatedAt,
                ["activated_by"] = gate.ActivatedBy,
            }, ct);
        await compliance.SaveChangesAsync(ct);

        return true;
    }

    public async Task<ComplianceStatusResponse> GetComplianceStatusAsync(
        string siteId, string studyId, string milestone, CancellationToken ct)
    {
        var studyCode = new StudyCode(studyId);
        var siteCode = new SiteCode(siteId);

        var cache = await compliance.GetAsync(studyCode, siteCode, milestone, ct);

        // Always succeeds -- a missing row is IsComplete: false / MissingDocuments: [], never a failure.
        return cache is null
            ? new ComplianceStatusResponse(
                studyCode,
                siteCode,
                milestone,
                IsComplete: false,
                MissingDocuments: Array.Empty<string>(),
                IsActivated: false,
                ActivatedAt: null,
                ActivatedBy: null)
            : new ComplianceStatusResponse(
                cache.StudyCode,
                cache.SiteCode,
                cache.Milestone,
                cache.IsComplete,
                cache.MissingDocuments,
                cache.IsActivated,
                cache.ActivatedAt,
                cache.ActivatedBy);
    }

    public async Task<ComplianceStatusResponse> GetComplianceStatusAsOfAsync(
        string siteId,
        string studyId,
        string milestone,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken ct)
    {
        var studyCode = new StudyCode(studyId);
        var siteCode = new SiteCode(siteId);

        var period = await compliance.GetPeriodAsOfAsync(studyCode, siteCode, milestone, validAt, knownAt, ct);
        return period is null
            ? new ComplianceStatusResponse(
                studyCode,
                siteCode,
                milestone,
                IsComplete: false,
                MissingDocuments: Array.Empty<string>(),
                IsActivated: false,
                ActivatedAt: null,
                ActivatedBy: null)
            : new ComplianceStatusResponse(
                period.StudyCode,
                period.SiteCode,
                period.Milestone,
                period.IsComplete,
                period.MissingDocuments,
                period.IsActivated,
                period.ActivatedAt,
                period.ActivatedBy);
    }
}
