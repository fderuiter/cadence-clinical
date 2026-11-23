using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Compliance;

/// <summary>
/// Application-service contract for the eTMF-completeness webhook, the site-activation gate-check, and the
/// compliance-status query (MIG-SUBJ-020, `02_technical_design.md` §3.1/§3.3, `01_spec.md` §1.1) — closing
/// raise site #9 and the two endpoints `00_phase1_validation_report.md` §2.1 flagged as entirely missing
/// from the original spec draft.
/// <para>
/// <see cref="ApplyEtmfWebhookAsync"/> and <see cref="ActivateSiteAsync"/> both return <c>Result&lt;bool&gt;</c>
/// rather than a meaningful DTO — matching §3.3's endpoint registrations, which discard the success value
/// entirely (<c>.Match(_ =&gt; Results.Ok(...), ...)</c>) and construct their own <c>OperationEnvelopeResponse</c>
/// instead. <c>bool</c> (always <see langword="true"/> on success) is used in place of a dedicated marker
/// type purely because none exists in <c>Cadence.Core.Domain</c> today; introducing one is outside this
/// ticket's scope. <see cref="ApplyEtmfWebhookAsync"/> in particular has no path that ever constructs a
/// <c>Result&lt;bool&gt;.Failure</c> — it always upserts — but keeps the <c>Result&lt;T&gt;</c> shape for
/// uniformity with the other two site-compliance endpoints' `.Match` handling.
/// </para>
/// <see cref="GetComplianceStatusAsync"/> is deliberately <b>not</b> <c>Result</c>-wrapped: it always
/// succeeds, even when no cache row exists (§1.2 — a missing row is <c>IsComplete: false</c>, never a 404).
/// Legacy Python source being replaced: <c>apps/execution/presentation/routers/subjects.py</c>
/// (<c>etmf_completeness_webhook</c>, <c>activate_site_endpoint</c>, <c>get_site_compliance_status</c>).
/// @req:PRD-SUB-001
/// </summary>
public interface ISiteComplianceApplicationService
{
    /// <summary>
    /// Upserts the <c>SiteComplianceCache</c> row keyed by
    /// <c>(payload.StudyId, payload.SiteId, payload.Milestone)</c>. Writes <b>no</b> audit-log entry — the
    /// one mutation in this slice that does not audit-log (`01_spec.md` §1.1) — and has no documented
    /// failure path; it always upserts.
    /// </summary>
    Task<Result<bool>> ApplyEtmfWebhookAsync(EtmfCompletenessWebhookPayload payload, CancellationToken ct);

    /// <summary>
    /// Re-reads the <c>SITE_ACTIVATION</c> compliance-cache row for <paramref name="siteId"/> /
    /// <paramref name="request"/>'s study. If missing or incomplete, writes a <c>BLOCKED_ACTIVATION</c>
    /// audit entry <b>before</b> returning <c>SubjectErrors.BlockedActivation</c>
    /// (raise site #9); otherwise writes a <c>SITE_ACTIVATION</c> audit entry and succeeds. Never itself
    /// flips <c>SiteComplianceCache.IsComplete</c> — only
    /// <see cref="ApplyEtmfWebhookAsync"/> does that.
    /// </summary>
    Task<Result<bool>> ActivateSiteAsync(string siteId, SiteActivationRequest request, CancellationToken ct);

    /// <summary>
    /// Always returns HTTP 200: a missing cache row is reported as <c>IsComplete: false</c>,
    /// <c>MissingDocuments: []</c> rather than a 404. Has no failure path — hence no <c>Result&lt;T&gt;</c>
    /// wrapper, unlike every other method in this slice.
    /// </summary>
    Task<ComplianceStatusResponse> GetComplianceStatusAsync(string siteId, string studyId, string milestone, CancellationToken ct);

    /// <summary>
    /// Point-in-time bitemporal compliance-status reconstruction (<c>as_of(valid_at, known_at)</c>).
    /// </summary>
    Task<ComplianceStatusResponse> GetComplianceStatusAsOfAsync(
        string siteId,
        string studyId,
        string milestone,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken ct) => GetComplianceStatusAsync(siteId, studyId, milestone, ct);
}
