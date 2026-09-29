using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Compliance;

/// <summary>
/// <see cref="ISiteComplianceApplicationService"/> (MIG-SUBJ-020, `02_technical_design.md` §3.1-§3.3,
/// `01_spec.md` §1.1). Orchestrates <see cref="SiteComplianceCache"/> (MIG-SUBJ-006) through
/// <see cref="ISiteComplianceRepository"/> (MIG-SUBJ-013, extended by this ticket with the write path it
/// deferred — see that interface's own doc comment) and <see cref="IAuditLogWriter"/> (MIG-SUBJ-017).
/// <para>
/// Three deliberately asymmetric behaviors, all load-bearing acceptance criteria — do not "fix" any of them:
/// <list type="bullet">
/// <item><see cref="ApplyEtmfWebhookAsync"/> writes an <c>ETMF_COMPLIANCE_UPDATE</c> audit entry whenever it
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
    ICurrentAuditContextAccessor auditContext) : ISiteComplianceApplicationService
{
    // Matches SiteComplianceCacheConfiguration.ToTable("site_compliance_caches") -- the audit chain's
    // per-tableName scoping key (EfAuditLogWriter) for every audit entry this service writes.
    private const string SiteComplianceTableName = "site_compliance_caches";
    private const string SiteActivationMilestone = "SITE_ACTIVATION";

    public async Task<Result<bool>> ApplyEtmfWebhookAsync(EtmfCompletenessWebhookPayload payload, CancellationToken ct)
    {
        var studyCode = new StudyCode(payload.StudyId);
        SiteCode? siteCode = payload.SiteId is null ? null : new SiteCode(payload.SiteId);
        IReadOnlyList<string> missingDocuments = payload.MissingArtifacts ?? Array.Empty<string>();

        var existing = await compliance.GetAsync(studyCode, siteCode, payload.Milestone, ct);
        SiteComplianceCache? changed = null;
        if (existing is not null)
        {
            existing.ApplyWebhookUpdate(payload.IsComplete, missingDocuments);
            changed = existing;
        }
        else if (siteCode is { } concreteSiteCode)
        {
            var created = SiteComplianceCache.Create(
                studyCode, concreteSiteCode, payload.Milestone, payload.IsComplete, missingDocuments);
            await compliance.AddAsync(created, ct);
            changed = created;
        }
        // else: SiteId is null (a study-level milestone, legitimate per EtmfCompletenessWebhookPayload's own
        // doc comment) and no existing row was found. SiteComplianceCache.SiteCode is non-nullable (MIG-SUBJ-006),
        // so there is no way to construct a row for this combination today -- a pre-existing gap between
        // already-merged tickets (see this ticket's own final report), not something MIG-SUBJ-020 can fix
        // without touching the aggregate itself. Rather than throw or fabricate a placeholder site, this is a
        // documented no-op: the call still "always upserts" in the sense of never failing or throwing.

        if (changed is not null)
        {
            // The legacy wrote no audit entry here (01_spec.md §1.1); #4767 closes that gap so every
            // compliance change is attributable to the integration that reported it.
            await auditLog.WriteAsync(SiteComplianceTableName, changed.Id.ToString(), "ETMF_COMPLIANCE_UPDATE",
                auditContext.UserId, auditContext.ChangeReason, oldValues: null,
                newValues: new Dictionary<string, object?>
                {
                    ["milestone"] = payload.Milestone,
                    ["is_complete"] = payload.IsComplete,
                    ["missing_documents"] = missingDocuments,
                }, ct);
        }

        await compliance.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Result<bool>> ActivateSiteAsync(string siteId, SiteActivationRequest request, CancellationToken ct)
    {
        var studyCode = new StudyCode(request.StudyId);
        var siteCode = new SiteCode(siteId);

        var gate = await compliance.GetAsync(studyCode, siteCode, SiteActivationMilestone, ct);
        if (gate is not { IsComplete: true })
        {
            // The BLOCKED_ACTIVATION audit entry must be written -- and committed -- before the failure
            // result is returned (01_spec.md §1.1, raise site #9). IAuditLogWriter.WriteAsync does not
            // itself call SaveChangesAsync, so the explicit SaveChangesAsync below is what actually commits
            // it; both calls are awaited in order, ahead of the `return`.
            await auditLog.WriteAsync(SiteComplianceTableName, siteId, "BLOCKED_ACTIVATION",
                auditContext.UserId, auditContext.ChangeReason, oldValues: null, newValues: null, ct);
            await compliance.SaveChangesAsync(ct);

            return SubjectErrors.BlockedActivation(siteId);
        }

        // This endpoint does not itself flip SiteComplianceCache.IsComplete -- that only happens via
        // ApplyEtmfWebhookAsync above (01_spec.md §1.1's explicit note). Only the audit trail is written here.
        await auditLog.WriteAsync(SiteComplianceTableName, siteId, "SITE_ACTIVATION",
            auditContext.UserId, auditContext.ChangeReason, oldValues: null, newValues: null, ct);
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
            ? new ComplianceStatusResponse(studyCode, siteCode, milestone, IsComplete: false, MissingDocuments: Array.Empty<string>())
            : new ComplianceStatusResponse(cache.StudyCode, cache.SiteCode, cache.Milestone, cache.IsComplete, cache.MissingDocuments);
    }
}
