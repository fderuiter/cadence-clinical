using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Apps.Imaging.Endpoints;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Security;

/// <summary>
/// Automated sentinels and behavioral HTTP tests enforcing:
/// <list type="number">
/// <item>Every regulated 21 CFR Part 11 endpoint across standalone and kernel hosts carries <see cref="ElectronicSignatureRequired"/>.</item>
/// <item>Missing, mismatched-action, mismatched-batch, and replayed <c>X-Sig-Token</c> headers fail closed with <c>401 Unauthorized</c>.</item>
/// <item>Every mutating Execution route enforces <see cref="ChangeReasonHeaderFilter"/> (<c>400 BadRequest</c> on missing or blank <c>X-Change-Reason</c>)
/// unless explicitly marked with <see cref="AllowWithoutChangeReasonMetadata"/>.</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
public sealed class RegulatedRoutesAndExecutionChangeReasonTests
{
    private static readonly (string Host, string Method, string RoutePattern)[] RegulatedSignatureRoutes =
    [
        ("ctms", "POST", "/api/v1/ctms/doa/sign-off"),
        ("ctms", "POST", "/api/v1/ctms/doa/sites/{site_id}/export-signed-pdf"),
        ("ctms", "POST", "/api/v1/ctms/letters/finalize"),
        ("ctms", "POST", "/api/v1/ctms/monitoring-visits/{visit_id}/sign-off"),
        ("ctms", "POST", "/api/v1/ctms/startup/matrix/batch-certify"),
        ("ctms", "POST", "/api/v1/ctms/startup/sites/{siteId}/greenlight/certify"),
        ("econsent", "POST", "/api/v1/econsent/templates/{template_id}/capture-consent"),
        ("econsent", "POST", "/api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent"),
        ("execution", "POST", "/api/v1/execution/cec/dossiers/{id}/chair-adjudicate"),
        ("execution", "POST", "/api/v1/execution/devices/quarantine/release"),
        ("execution", "POST", "/api/v1/execution/labs/toxicity/review/{review_id}/action"),
        ("execution", "POST", "/api/v1/execution/randomization/schemes/{schemeId:guid}/approve"),
        ("execution", "POST", "/api/v1/execution/rtsm/cgt/bedside-verify"),
        ("execution", "POST", "/api/v1/execution/rtsm/cgt/destruction-certificate"),
        ("execution", "POST", "/api/v1/execution/rtsm/cgt/infuse"),
        ("execution", "POST", "/api/v1/execution/rtsm/cgt/returns"),
        ("execution", "POST", "/api/v1/execution/subjects/{subjectId}/randomize"),
        ("execution", "POST", "/api/v1/execution/subjects/{subjectId}/unblind"),
        ("imaging", "POST", "/api/v1/imaging/adjudications/{sessionId}/chair-adjudicate"),
        ("interop", "POST", "/api/v1/interop/esource/reconcile/accept"),
        ("interop", "POST", "/api/v1/interop/esource/reconcile/batch-accept"),
        ("interop", "POST", "/api/v1/interop/esource/reconcile/batch-reject"),
        ("interop", "POST", "/api/v1/interop/esource/reconcile/reject"),
        ("kernel", "POST", "/api/v1/execution/forms/{formId:guid}/signatures"),
        ("kernel", "POST", "/api/v1/kernel/library-items/{itemRef}/approve"),
        ("kernel", "POST", "/api/v1/kernel/locks"),
        ("kernel", "POST", "/api/v1/kernel/locks/{lockRef}/release"),
        ("kernel", "POST", "/api/v1/kernel/protocol-versions/{versionRef}/approve"),
        ("kernel", "POST", "/api/v1/kernel/validation/findings/{findingRef}/accept"),
        ("kernel", "POST", "/api/v1/kernel/validation/findings/{findingRef}/explain"),
        ("knowledge", "POST", "/api/v1/training/delegations/bulk-signoff"),
        ("payments", "POST", "/api/v1/payments/invoices/{id}/remittance-advice/sign"),
        ("safety", "POST", "/api/v1/safety/dsur/{id}/approve"),
        ("safety", "POST", "/api/v1/safety/periodic-reports/{id}/approve"),
        ("tickets", "POST", "/api/v1/tickets/{ticket_id}/sign"),
    ];

    private static readonly HashSet<string> ExpectedExecutionStatelessOptOuts = new(StringComparer.Ordinal)
    {
        "POST /api/v1/execution/coding/synonyms/validate",
        "POST /api/v1/execution/devices/parse-udi",
        "POST /api/v1/execution/labs/convert-unit",
        "POST /api/v1/execution/labs/import/dry-run",
        "POST /api/v1/execution/labs/toxicity/evaluate",
        "POST /api/v1/execution/rtsm/cgt/verify-barcode",
        "POST /api/v1/execution/rtsm/kits/validate-scan",
    };

    [Fact(DisplayName = "RegulatedRoutes_MustAllCarry_ElectronicSignatureRequiredMetadata")]
    public async Task RegulatedRoutesMustAllCarryElectronicSignatureRequiredMetadata()
    {
        foreach (var group in RegulatedSignatureRoutes.GroupBy(r => r.Host, StringComparer.Ordinal))
        {
            await using var host = ServiceHosts.All[group.Key](null);
            foreach (var (hostName, method, pattern) in group)
            {
                var matching = host.Endpoints.FirstOrDefault(e =>
                    string.Equals((e.RoutePattern.RawText ?? string.Empty).TrimEnd('/'), pattern.TrimEnd('/'), StringComparison.Ordinal)
                    && (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) ?? false));

                matching.Should().NotBeNull($"{hostName} must map regulated endpoint {method} {pattern}");
                matching!.Metadata.GetMetadata<ElectronicSignatureRequired>().Should().NotBeNull(
                    $"{hostName} {method} {pattern} must enforce RequireElectronicSignature (21 CFR Part 11)");
            }
        }
    }

    [Fact(DisplayName = "ExecutionMutatingEndpoints_MustEnforceChangeReasonHeaderFilter_UnlessExplicitlyOptedOut")]
    public async Task ExecutionMutatingEndpointsMustEnforceChangeReasonHeaderFilterUnlessExplicitlyOptedOut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = ServiceHosts.All["execution"](null);

        var mutatingEndpoints = host.Endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(e =>
            {
                var methods = e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
                return methods.Any(m => HttpMethods.IsPost(m) || HttpMethods.IsPut(m) || HttpMethods.IsPatch(m) || HttpMethods.IsDelete(m));
            })
            .ToList();

        mutatingEndpoints.Should().NotBeEmpty();

        var actualOptOuts = new HashSet<string>(StringComparer.Ordinal);
        int enforcedCount = 0;

        foreach (var endpoint in mutatingEndpoints)
        {
            string method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods
                .First(m => HttpMethods.IsPost(m) || HttpMethods.IsPut(m) || HttpMethods.IsPatch(m) || HttpMethods.IsDelete(m));
            string signature = $"{method} {endpoint.RoutePattern.RawText}";

            if (endpoint.Metadata.GetMetadata<AllowWithoutChangeReasonMetadata>() is not null)
            {
                actualOptOuts.Add(signature);
                continue;
            }

            string url = MaterializeRoute(endpoint.RoutePattern);
            string role = ResolveAuthorizedRole(endpoint);

            // 1. Missing X-Change-Reason -> 400 BadRequest ProblemDetails
            var callerMissingReason = BuildExecutionCaller($"noreason-{enforcedCount}", role, endpoint, url, changeReason: null);
            using (var client = host.CreateSignedClient(callerMissingReason))
            using (var req = BuildJsonRequest(method, url, "{}"))
            using (var resp = await client.SendAsync(req, ct))
            {
                string body = await resp.Content.ReadAsStringAsync(ct);
                resp.StatusCode.Should().Be(
                    HttpStatusCode.BadRequest,
                    $"Execution mutating route '{signature}' must reject missing X-Change-Reason with 400 BadRequest (got {(int)resp.StatusCode}: {body})");
                body.Should().Contain(GatewayHeaders.ChangeReason);
            }

            // 2. Whitespace-only X-Change-Reason -> 400 BadRequest ProblemDetails
            var callerBlankReason = BuildExecutionCaller($"blankreason-{enforcedCount}", role, endpoint, url, changeReason: "   ");
            using (var client = host.CreateSignedClient(callerBlankReason))
            using (var req = BuildJsonRequest(method, url, "{}"))
            using (var resp = await client.SendAsync(req, ct))
            {
                string body = await resp.Content.ReadAsStringAsync(ct);
                resp.StatusCode.Should().Be(
                    HttpStatusCode.BadRequest,
                    $"Execution mutating route '{signature}' must reject whitespace X-Change-Reason with 400 BadRequest (got {(int)resp.StatusCode}: {body})");
                body.Should().Contain(GatewayHeaders.ChangeReason);
            }

            enforcedCount++;
        }

        actualOptOuts.Should().BeEquivalentTo(
            ExpectedExecutionStatelessOptOuts,
            "only stateless dry-run/validation Execution helpers may opt out of ChangeReasonHeaderFilter via AllowWithoutChangeReason()");
        enforcedCount.Should().BeGreaterThanOrEqualTo(35, "all mutating Execution endpoints must be verified against missing and blank X-Change-Reason");
    }

    public static TheoryData<string, string, string, string, string, string?> NewlySignedEndpointCases => new()
    {
        { "execution", "/api/v1/execution/subjects/SUBJ-SIG-1/unblind", "/api/v1/execution/subjects/SUBJ-SIG-1/unblind", SubjectEndpoints.UnblindSubjectSemanticAction, "{}", null },
        { "execution", CgtEndpoints.BedsideVerifyAction, CgtEndpoints.BedsideVerifyAction, CgtEndpoints.BedsideVerifySemanticAction, """{"studyId":"STUDY-01","siteId":"SITE-101","subjectId":"SUBJ-01","lotNumber":"LOT-1","bagBarcode":"BAG-1","patientWristbandBarcode":"WB-1","primaryVerifierId":"crc-1","secondaryVerifierId":"rn-2"}""", null },
        { "execution", CgtEndpoints.InfuseAction, CgtEndpoints.InfuseAction, CgtEndpoints.InfuseSemanticAction, """{"studyId":"STUDY-01","siteId":"SITE-101","subjectId":"SUBJ-01","bagBarcode":"BAG-1","infusionVolumeMl":125.0}""", null },
        { "execution", CgtEndpoints.ReconcileReturnAction, CgtEndpoints.ReconcileReturnAction, CgtEndpoints.ReconcileReturnSemanticAction, """{"studyId":"STUDY-01","siteId":"SITE-101","subjectId":"SUBJ-01","vialId":"VIAL-1","disposition":"RETURNED"}""", null },
        { "execution", CgtEndpoints.CertifyDestructionAction, CgtEndpoints.CertifyDestructionAction, CgtEndpoints.CertifyDestructionSemanticAction, """{"studyId":"STUDY-01","siteId":"SITE-101","vialId":"VIAL-1","witnessUserId":"witness-2","destructionMethod":"INCINERATION"}""", null },
        { "execution", DeviceEndpoints.ReleaseQuarantineAction, DeviceEndpoints.ReleaseQuarantineAction, DeviceEndpoints.ReleaseQuarantineSemanticAction, """{"studyId":"STUDY-01","udiDi":"00884838000018","lotNumber":"LOT-DEV-1","releaseJustification":"Manufacturer clearance"}""", null },
        { "ctms", StartupGreenlightEndpoints.CertifySiteGreenlightAction("SITE-101") + "?study_id=STUDY-01", StartupGreenlightEndpoints.CertifySiteGreenlightAction("SITE-101"), StartupGreenlightEndpoints.CertifySiteGreenlightSemanticAction, "{}", null },
        { "ctms", StartupGreenlightEndpoints.BatchCertifyGreenlightAction, StartupGreenlightEndpoints.BatchCertifyGreenlightAction, StartupGreenlightEndpoints.BatchCertifyGreenlightSemanticAction, """{"study_id":"STUDY-01","country_code":"US","site_ids":["SITE-101","SITE-102"],"reason_for_change":"Country greenlight"}""", new BatchCertifyRequestDto("STUDY-01", "US", ["SITE-101", "SITE-102"], "Country greenlight").BatchBindingId() },
        { "ctms", LetterTemplateEndpoints.FinalizeLetterAction + "?templateName=confirmation_letter", LetterTemplateEndpoints.FinalizeLetterAction, LetterTemplateEndpoints.FinalizeLetterSemanticAction, """{"craId":"cra-1","createdAt":"2026-11-20","studyId":"STUDY-01","siteId":"SITE-101","visitType":"IMV","finalize":true,"reasonForChange":"Finalize monitoring letter"}""", null },
        { "ctms", DoaEndpoints.ExportSignedPdfAction("SITE-101"), DoaEndpoints.ExportSignedPdfAction("SITE-101"), DoaEndpoints.ExportSignedPdfSemanticAction, """{"study_id":"STUDY-01","reason_for_change":"Export signed regulatory DoA PDF"}""", null },
        { "payments", PaymentEndpoints.SignRemittanceAction("INV-101"), PaymentEndpoints.SignRemittanceAction("INV-101"), PaymentEndpoints.SignRemittanceSemanticAction, """{"signerName":"Dr. Finance","signingReason":"Remittance certified"}""", null },
        { "imaging", ImagingAdjudicationEndpoints.ChairAdjudicateAction("SESS-101"), ImagingAdjudicationEndpoints.ChairAdjudicateAction("SESS-101"), ImagingAdjudicationEndpoints.ChairAdjudicateSemanticAction, """{"finalConsensusResponse":"PR","finalConsensusSldMm":32.5,"bindingReader":"READER_1","adjudicationRationale":"Clear target lesion shrinkage","signerName":"Dr. Chair","signatureMeaning":"Consensus Adjudication"}""", null },
        { "interop", EsourceEndpoints.AcceptCandidateAction, EsourceEndpoints.AcceptCandidateAction, EsourceEndpoints.AcceptCandidateSemanticAction, """{"candidateId":"cand-1","commitReason":"Matches chart","changeReason":"Reconcile"}""", null },
        { "interop", EsourceEndpoints.BatchAcceptAction, EsourceEndpoints.BatchAcceptAction, EsourceEndpoints.BatchAcceptSemanticAction, """{"candidateIds":["cand-2","cand-1"],"commitReason":"Matches chart","changeReason":"Batch reconcile"}""", new EhrReconcileBatchAcceptRequest(["cand-2", "cand-1"], "Matches chart", "Batch reconcile").BatchBindingId() },
        { "interop", EsourceEndpoints.RejectCandidateAction, EsourceEndpoints.RejectCandidateAction, EsourceEndpoints.RejectCandidateSemanticAction, """{"candidateId":"cand-1","rejectionReason":"Duplicate entry","changeReason":"Reconcile"}""", null },
        { "interop", EsourceEndpoints.BatchRejectAction, EsourceEndpoints.BatchRejectAction, EsourceEndpoints.BatchRejectSemanticAction, """{"candidateIds":["cand-2","cand-1"],"rejectionReason":"Duplicate entry","changeReason":"Batch reconcile"}""", new EhrReconcileBatchRejectRequest(["cand-2", "cand-1"], "Duplicate entry", "Batch reconcile").BatchBindingId() },
    };

    [Theory(DisplayName = "NewlySignedRoutes_RejectMissingMismatchedAndBatchTamperedSigTokens_With401")]
    [MemberData(nameof(NewlySignedEndpointCases))]
    public async Task NewlySignedRoutesRejectMissingMismatchedAndBatchTamperedSigTokensWith401(
        string hostName,
        string requestUrl,
        string actionPath,
        string semanticAction,
        string jsonBody,
        string? expectedBatchId)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantResolver = new DeterministicTenantCodeResolver();

        await using var host = ServiceHosts.All[hostName](services =>
        {
            CadenceDatabaseExtensions.AddCadenceInMemoryTenancy(services, tenantResolver);
            foreach (var existing in services.Where(d => d.ServiceType == typeof(ISigTokenUseStore)).ToList())
            {
                services.Remove(existing);
            }

            services.AddSingleton<ISigTokenUseStore, ReplayCache>();
        });

        var signedEndpoint = host.Endpoints.First(e =>
            e.Metadata.GetMetadata<ElectronicSignatureRequired>() is not null
            && PathMatchesPattern(actionPath, e.RoutePattern));
        string role = ResolveAuthorizedRole(signedEndpoint);
        string reason = "Clinical signature verification test justification exceeding fifty characters for Part 11.";

        // 1. Missing X-Sig-Token -> 401 Unauthorized
        var missingSigIdentity = TestSecurityContext.Identity($"sig-missing-{hostName}", role) with
        {
            SiteIds = ["SITE-101", "SITE-102"],
            StudyIds = ["*"],
            ChangeReason = reason,
            SigToken = null,
        };
        using (var client = host.CreateSignedClient(missingSigIdentity))
        using (var req = BuildJsonRequest(HttpMethods.Post, requestUrl, jsonBody))
        using (var resp = await client.SendAsync(req, ct))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{hostName} {requestUrl} must reject missing X-Sig-Token with 401");
        }

        // 2. Wrong semantic_action -> 401 Unauthorized
        string wrongSemanticToken = TestSecurityContext.CreateSigToken(
            $"sig-wrong-sem-{hostName}",
            actionPath,
            "wrong.semantic.action",
            [role],
            batchId: expectedBatchId);
        var wrongSemanticIdentity = missingSigIdentity with
        {
            UserId = $"sig-wrong-sem-{hostName}",
            SigToken = wrongSemanticToken,
        };
        using (var client = host.CreateSignedClient(wrongSemanticIdentity))
        using (var req = BuildJsonRequest(HttpMethods.Post, requestUrl, jsonBody))
        using (var resp = await client.SendAsync(req, ct))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{hostName} {requestUrl} must reject mismatched semantic_action with 401");
        }

        // 3. Wrong action path -> 401 Unauthorized
        string wrongPathToken = TestSecurityContext.CreateSigToken(
            $"sig-wrong-path-{hostName}",
            "/api/v1/other/action",
            semanticAction,
            [role],
            batchId: expectedBatchId);
        var wrongPathIdentity = missingSigIdentity with
        {
            UserId = $"sig-wrong-path-{hostName}",
            SigToken = wrongPathToken,
        };
        using (var client = host.CreateSignedClient(wrongPathIdentity))
        using (var req = BuildJsonRequest(HttpMethods.Post, requestUrl, jsonBody))
        using (var resp = await client.SendAsync(req, ct))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{hostName} {requestUrl} must reject mismatched action path with 401");
        }

        // 4. For batch-bound routes, wrong batch_id -> 401 Unauthorized
        if (expectedBatchId is not null)
        {
            string wrongBatchToken = TestSecurityContext.CreateSigToken(
                $"sig-wrong-batch-{hostName}",
                actionPath,
                semanticAction,
                [role],
                batchId: "0000000000000000000000000000000000000000000000000000000000000000");
            var wrongBatchIdentity = missingSigIdentity with
            {
                UserId = $"sig-wrong-batch-{hostName}",
                SigToken = wrongBatchToken,
            };
            using var client = host.CreateSignedClient(wrongBatchIdentity);
            using var req = BuildJsonRequest(HttpMethods.Post, requestUrl, jsonBody);
            using var resp = await client.SendAsync(req, ct);
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{hostName} {requestUrl} must reject tampered batch_id with 401");
        }

        // 5. On the in-memory Execution host, verify single-use token replay fails closed with 401 Unauthorized on reuse
        if (string.Equals(hostName, "execution", StringComparison.Ordinal))
        {
            string singleUseToken = TestSecurityContext.CreateSigToken(
                $"sig-replay-{hostName}",
                actionPath,
                semanticAction,
                [role],
                batchId: expectedBatchId);
            var replayIdentity = missingSigIdentity with
            {
                UserId = $"sig-replay-{hostName}",
                SigToken = singleUseToken,
            };
            using var client = host.CreateSignedClient(replayIdentity);
            using (var firstReq = BuildJsonRequest(HttpMethods.Post, requestUrl, jsonBody))
            using (var firstResp = await client.SendAsync(firstReq, ct))
            {
                firstResp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, "first presentation of a valid X-Sig-Token must pass signature verification");
            }

            using var replayReq = BuildJsonRequest(HttpMethods.Post, requestUrl, jsonBody);
            using var replayResp = await client.SendAsync(replayReq, ct);
            replayResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{hostName} {requestUrl} must reject replayed X-Sig-Token with 401");
        }
    }

    private static GatewayIdentity BuildExecutionCaller(
        string userId,
        string role,
        RouteEndpoint endpoint,
        string url,
        string? changeReason)
    {
        string? sigToken = null;
        if (endpoint.Metadata.GetMetadata<ElectronicSignatureRequired>() is not null)
        {
            string semanticAction = ResolveExecutionSemanticAction(url);
            sigToken = TestSecurityContext.CreateSigToken(userId, url, semanticAction, [role]);
        }

        return TestSecurityContext.Identity(userId, role) with
        {
            SiteIds = ["SITE-101"],
            StudyIds = ["*"],
            ChangeReason = changeReason,
            SigToken = sigToken,
        };
    }

    private static string ResolveExecutionSemanticAction(string url) => url switch
    {
        CgtEndpoints.BedsideVerifyAction => CgtEndpoints.BedsideVerifySemanticAction,
        CgtEndpoints.InfuseAction => CgtEndpoints.InfuseSemanticAction,
        CgtEndpoints.ReconcileReturnAction => CgtEndpoints.ReconcileReturnSemanticAction,
        CgtEndpoints.CertifyDestructionAction => CgtEndpoints.CertifyDestructionSemanticAction,
        DeviceEndpoints.ReleaseQuarantineAction => DeviceEndpoints.ReleaseQuarantineSemanticAction,
        _ when url.Contains("/cec/dossiers/", StringComparison.Ordinal) => "execution.cec.chair_adjudicate",
        _ when url.Contains("/labs/toxicity/review/", StringComparison.Ordinal) => LabToxicityEndpoints.ReviewActionSemanticAction,
        _ when url.Contains("/randomization/schemes/", StringComparison.Ordinal) => RandomizationEndpoints.ApproveSchemeSemanticAction,
        _ when url.EndsWith("/randomize", StringComparison.Ordinal) => RandomizationEndpoints.RandomizeSubjectSemanticAction,
        _ when url.EndsWith("/unblind", StringComparison.Ordinal) => SubjectEndpoints.UnblindSubjectSemanticAction,
        _ => throw new InvalidOperationException($"Unknown signed Execution URL '{url}'."),
    };

    private static string ResolveAuthorizedRole(RouteEndpoint endpoint)
    {
        foreach (var authorize in endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>())
        {
            string? policy = authorize.Policy;
            if (string.IsNullOrEmpty(policy) || !policy.StartsWith(PermissionPolicyProvider.PolicyPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string permission = policy[PermissionPolicyProvider.PolicyPrefix.Length..];
            var match = PermissionMatrix.ROLE_PERMISSIONS.Keys
                .Order(StringComparer.Ordinal)
                .FirstOrDefault(candidate =>
                    !string.Equals(candidate, "auditor", StringComparison.Ordinal)
                    && PermissionMatrix.HasPermission(
                        new Principal { Roles = RoleNormalization.GetNormalizedRoles([candidate]), RawRoles = [candidate] },
                        permission));
            if (match is not null)
            {
                return match;
            }
        }

        return "crc";
    }

    private static bool PathMatchesPattern(string actionPath, RoutePattern pattern)
    {
        var actualSegments = actionPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (actualSegments.Length != pattern.PathSegments.Count)
        {
            return false;
        }

        for (int i = 0; i < actualSegments.Length; i++)
        {
            var seg = pattern.PathSegments[i];
            if (seg.Parts.Count == 1 && seg.Parts[0] is RoutePatternParameterPart)
            {
                continue;
            }

            string literal = string.Concat(seg.Parts.OfType<RoutePatternLiteralPart>().Select(p => p.Content));
            if (!string.Equals(actualSegments[i], literal, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string MaterializeRoute(RoutePattern pattern)
    {
        var segments = pattern.PathSegments.Select(segment =>
            string.Concat(segment.Parts.Select(part => part switch
            {
                RoutePatternLiteralPart literal => literal.Content,
                RoutePatternSeparatorPart separator => separator.Content,
                RoutePatternParameterPart param => SampleParameterValue(param),
                _ => "sample",
            })));
        return "/" + string.Join("/", segments);
    }

    private static string SampleParameterValue(RoutePatternParameterPart param)
    {
        if (param.ParameterPolicies.Any(p => string.Equals(p.Content, "guid", StringComparison.OrdinalIgnoreCase)))
        {
            return "01960000-0000-7000-8000-000000000101";
        }

        if (param.ParameterPolicies.Any(p =>
            string.Equals(p.Content, "int", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Content, "long", StringComparison.OrdinalIgnoreCase)))
        {
            return "1";
        }

        return param.Name.Contains("site", StringComparison.OrdinalIgnoreCase) ? "SITE-101" : "ID-101";
    }

    private static HttpRequestMessage BuildJsonRequest(string method, string url, string json) =>
        new(new HttpMethod(method), url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
}
