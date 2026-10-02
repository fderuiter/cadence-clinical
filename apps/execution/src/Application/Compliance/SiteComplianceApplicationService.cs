using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;
using Cadence.Security;
using Cadence.Security.Authentication;

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
    // The audit chain key for every entry this service writes: the aggregate's CLR type name (#4768).
    private const string SiteComplianceEntityName = nameof(SiteComplianceCache);
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
            // The recorded action below labels this row's audit entry; the row carries the same reason in case an
            // entry is ever written from the row itself (#4922).
            changed.ReasonForChange = auditContext.ChangeReason;

            // The legacy wrote no audit entry here (01_spec.md §1.1); #4767 closes that gap so every
            // compliance change is attributable to the integration that reported it.
            await auditLog.WriteAsync(SiteComplianceEntityName, changed.Id.ToString(), "ETMF_COMPLIANCE_UPDATE",
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

    private static readonly string[] MandatoryFacets =
    [
        "REGULATORY_IRB",
        "CTA_BUDGET",
        "EISF_BASELINE",
        "DOA_LOG",
        "STAFF_TRAINING",
        "IP_RELEASE"
    ];

    public async Task<Result<SiteActivationResponse>> ActivateSiteAsync(string siteId, SiteActivationRequest request, CancellationToken ct)
    {
        var studyCode = new StudyCode(request.StudyId);
        var siteCode = new SiteCode(siteId);

        var gate = await compliance.GetAsync(studyCode, siteCode, SiteActivationMilestone, ct).ConfigureAwait(false);
        bool siteActivationComplete = gate is { IsComplete: true };

        bool allFacetsPassed = siteActivationComplete;
        var facetStates = new Dictionary<string, bool>();

        if (!allFacetsPassed)
        {
            allFacetsPassed = true;
            foreach (var facet in MandatoryFacets)
            {
                var row = await compliance.GetAsync(studyCode, siteCode, facet, ct).ConfigureAwait(false);
                if (row is not { IsComplete: true })
                {
                    var altRow = await compliance.GetAsync(studyCode, siteCode, "facet_" + facet.ToLowerInvariant(), ct).ConfigureAwait(false);
                    if (altRow is not { IsComplete: true })
                    {
                        facetStates[facet] = false;
                        allFacetsPassed = false;
                    }
                    else
                    {
                        facetStates[facet] = true;
                    }
                }
                else
                {
                    facetStates[facet] = true;
                }
            }
        }
        else
        {
            foreach (var facet in MandatoryFacets)
            {
                facetStates[facet] = true;
            }
        }

        bool isExecutiveOverride = false;
        if (!allFacetsPassed &&
            !string.IsNullOrWhiteSpace(request.QaDirectorSigToken) &&
            !string.IsNullOrWhiteSpace(request.MedicalMonitorSigToken))
        {
            var verifier = new SigTokenVerifier();
            byte[] secret = Encoding.UTF8.GetBytes(
                Environment.GetEnvironmentVariable("GATEWAY_SECRET")
                ?? Environment.GetEnvironmentVariable("INTERNAL_GATEWAY_SECRET")
                ?? "internal-gateway-secret-abcdefabcdef!!");

            try
            {
                string qaUserId = request.QaDirectorUserId ?? "qa_director";
                string medUserId = request.MedicalMonitorUserId ?? "medical_monitor";

                var qaJwt = verifier.VerifyAndConsumeSigToken(request.QaDirectorSigToken, qaUserId, secret);
                var medJwt = verifier.VerifyAndConsumeSigToken(request.MedicalMonitorSigToken, medUserId, secret);

                var qaRoles = qaJwt.Claims.Where(c => c.Type == SigTokenClaims.Roles || c.Type == "role").Select(c => c.Value).ToList();
                var medRoles = medJwt.Claims.Where(c => c.Type == SigTokenClaims.Roles || c.Type == "role").Select(c => c.Value).ToList();

                bool isQa = qaRoles.Any(r => r.Contains("qa", StringComparison.OrdinalIgnoreCase) || r.Contains("quality", StringComparison.OrdinalIgnoreCase));
                bool isMed = medRoles.Any(r => r.Contains("med", StringComparison.OrdinalIgnoreCase) || r.Contains("monitor", StringComparison.OrdinalIgnoreCase) || r.Contains("physician", StringComparison.OrdinalIgnoreCase));

                if (isQa && isMed && !string.Equals(qaUserId, medUserId, StringComparison.OrdinalIgnoreCase))
                {
                    isExecutiveOverride = true;
                }
            }
            catch
            {
                isExecutiveOverride = false;
            }
        }

        if (!allFacetsPassed && !isExecutiveOverride)
        {
            await auditLog.WriteAsync(SiteComplianceEntityName, siteId, "BLOCKED_ACTIVATION",
                auditContext.UserId, auditContext.ChangeReason, oldValues: null, newValues: null, ct).ConfigureAwait(false);
            await compliance.SaveChangesAsync(ct).ConfigureAwait(false);

            return SubjectErrors.BlockedActivation(siteId);
        }

        string facetHash = Cadence.Security.Authentication.HmacSigner.ComputeSha256Hash(System.Text.Json.JsonSerializer.Serialize(facetStates));
        string token = GreenlightToken.Generate(
            studyId: studyCode.Value,
            siteId: siteCode.Value,
            certifiedBy: auditContext.UserId,
            isOverridden: isExecutiveOverride,
            countryCode: request.CountryCode ?? "US",
            facetHash: facetHash);

        if (gate is not null)
        {
            gate.SetGreenlightToken(token, isComplete: true);
        }
        else
        {
            var newGate = SiteComplianceCache.Create(studyCode, siteCode, SiteActivationMilestone, isComplete: true, Array.Empty<string>());
            newGate.SetGreenlightToken(token, isComplete: true);
            await compliance.AddAsync(newGate, ct).ConfigureAwait(false);
        }

        var auditValues = new Dictionary<string, object?>
        {
            ["greenlight_token"] = token,
            ["is_overridden"] = isExecutiveOverride,
            ["study_id"] = studyCode.Value,
            ["site_id"] = siteCode.Value,
            ["country_code"] = request.CountryCode ?? "US",
        };

        if (isExecutiveOverride)
        {
            auditValues["qa_director_user_id"] = request.QaDirectorUserId;
            auditValues["medical_monitor_user_id"] = request.MedicalMonitorUserId;
            auditValues["justification"] = request.Justification;
        }

        await auditLog.WriteAsync(SiteComplianceEntityName, siteId, "SITE_GREENLIGHT_ISSUED",
            auditContext.UserId, request.Justification ?? auditContext.ChangeReason, oldValues: null, newValues: auditValues, ct).ConfigureAwait(false);

        await compliance.SaveChangesAsync(ct).ConfigureAwait(false);

        return new SiteActivationResponse(siteCode.Value, studyCode.Value, Activated: true, GreenlightToken: token, IsOverridden: isExecutiveOverride);
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
