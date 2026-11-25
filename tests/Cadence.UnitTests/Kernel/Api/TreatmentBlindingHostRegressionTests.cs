using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Modules.Intelligence.Mcp;
using Cadence.Modules.Intelligence.Tools;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Kernel.Api;

/// <summary>
/// Full-host Kestrel regression suite for centralized treatment blinding (<c>K-AUTH-4</c>, <c>K-AI-6</c>, <c>K-AUTH-5</c>)
/// across study exports, participant casebooks, direct observation reads, Execution eCRF forms, Intelligence MCP tools,
/// validation findings, and <c>CollectionValidationService</c> rule-pack and form previews.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TreatmentBlindingHostRegressionTests : IAsyncLifetime
{
    private const string CanaryArm = "ARM-ACTIVE-CANARY-100MG";
    private const string AlternateCanaryArm = "ARM-PLACEBO-CANARY-0MG";
    private const string BlindedAssertionCanaryText = "Query mentions ARM-ACTIVE-CANARY-100MG kit";
    private const string BlindedAnswerCanaryText = "Confirmed ARM-ACTIVE-CANARY-100MG dispensed";
    private const string BlindedRuleId = "EC-BLIND-01";
    private const string NonBlindedRuleId = "EC-VS-01";
    private const string UnblindQualificationType = "unblinded_pharmacist_training";

    private static readonly DateTimeOffset T0 = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Actor Issuer = Actor.Person("sponsor-admin@sponsor.test");
    private static readonly Actor BlindedPerson = Actor.Person("crc-blinded@site.test");
    private static readonly Actor BlindedStudyDmPerson = Actor.Person("dm-study-blinded@sponsor.test");
    private static readonly Actor PharmacistPerson = Actor.Person("pharmacist@site.test");
    private static readonly Actor UnqualifiedPharmacistPerson = Actor.Person("pharmacist-expired@site.test");
    private static readonly Actor McpAgentActor = Actor.AutomationAgent(IntelligenceMcp.AgentName);

    private readonly SeededClinicalState _state = new(CanaryArm);
    private KestrelKernelHostFactory? _factory;

    public ValueTask InitializeAsync()
    {
        _factory = new KestrelKernelHostFactory(_state);
        _factory.EnsureStarted();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    /// <summary>
    /// Acceptance scenario: a blinded site coordinator holding a tenant-wide <c>observation.record</c> grant without
    /// <c>enrollment.unblind</c> exercises all eight read and preview surfaces on Kestrel and observes zero leaks of
    /// <see cref="CanaryArm"/> or <see cref="BlindedRuleId"/> while non-blinded clinical items and rules remain intact.
    /// </summary>
    [Fact(DisplayName = "BlindedCoordinator_WithTenantObservationGrant_SeesZeroTreatmentLeaksAcrossAllRoutes")]
    [KernelRule("K-AUTH-4")]
    [KernelRule("K-AI-6")]
    public async Task BlindedCoordinatorWithTenantObservationGrantSeesZeroTreatmentLeaksAcrossAllRoutes()
    {
        var ct = TestContext.Current.CancellationToken;
        var blindedIdentity = new GatewayIdentity(
            UserId: BlindedPerson.Id,
            Roles: ["Site Coordinator"],
            SiteIds: ["SITE-01"],
            TenantId: TestSecurityContext.TenantCode,
            StudyIds: [_state.StudyId.Value.ToString("D")],
            UnblindedAccess: false);

        using var client = _factory!.CreateKestrelSignedClient(blindedIdentity);

        var studyRef = Uri.EscapeDataString(_state.StudyNode.ToString());
        var enrollmentRef = Uri.EscapeDataString(_state.EnrollmentNode.ToString());
        var blindedObsRef = Uri.EscapeDataString(_state.BlindedObservationNode.ToString());
        var nonBlindedObsRef = Uri.EscapeDataString(_state.NonBlindedObservationNode.ToString());
        var blindedFindingRef = Uri.EscapeDataString(_state.BlindedFindingNode.ToString());
        var rulePackRef = Uri.EscapeDataString(NodeRef.For(_state.RulePackItemId).ToString());

        // 1. Study exports (CSV, ODM XML, Dataset-JSON)
        var (csvStatus, exportCsv) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/studies/{studyRef}/exports?format=csv", ct: ct);
        csvStatus.Should().Be(HttpStatusCode.OK, exportCsv);
        exportCsv.Should().NotContain(CanaryArm)
            .And.NotContain(_state.BlindedObservationNode.ToString())
            .And.Contain("masked")
            .And.Contain("185");

        var (odmStatus, exportOdm) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/studies/{studyRef}/exports?format=odm_xml", ct: ct);
        odmStatus.Should().Be(HttpStatusCode.OK, exportOdm);
        exportOdm.Should().NotContain(CanaryArm)
            .And.NotContain(_state.BlindedObservationNode.ToString())
            .And.Contain("Value=\"masked\"")
            .And.Contain("cadence:MaskReason=\"Blinded\"")
            .And.Contain("185");

        var (dsStatus, exportDatasetJson) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/studies/{studyRef}/exports?format=dataset_json", ct: ct);
        dsStatus.Should().Be(HttpStatusCode.OK, exportDatasetJson);
        exportDatasetJson.Should().NotContain(CanaryArm)
            .And.NotContain(_state.BlindedObservationNode.ToString())
            .And.Contain("\"masked\"")
            .And.Contain("185");

        // 2. Participant casebook (JSON, ODM XML, HTML) + treatment-entropy-free digest check
        var (cbStatus, cbBody) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/enrollments/{enrollmentRef}/casebook?format=json", ct: ct);
        cbStatus.Should().Be(HttpStatusCode.OK, cbBody);
        cbBody.Should().NotContain(CanaryArm)
            .And.NotContain(BlindedAssertionCanaryText)
            .And.NotContain(BlindedAnswerCanaryText)
            .And.NotContain(_state.BlindedObservationNode.ToString());

        var (cbOdmStatus, cbOdmBody) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/enrollments/{enrollmentRef}/casebook?format=odm_xml", ct: ct);
        cbOdmStatus.Should().Be(HttpStatusCode.OK, cbOdmBody);
        cbOdmBody.Should().NotContain(CanaryArm)
            .And.NotContain(BlindedAssertionCanaryText)
            .And.NotContain(BlindedAnswerCanaryText)
            .And.NotContain(_state.BlindedObservationNode.ToString());

        var (cbHtmlStatus, cbHtmlBody) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/enrollments/{enrollmentRef}/casebook?format=html", ct: ct);
        cbHtmlStatus.Should().Be(HttpStatusCode.OK, cbHtmlBody);
        cbHtmlBody.Should().NotContain(CanaryArm)
            .And.NotContain(BlindedAssertionCanaryText)
            .And.NotContain(BlindedAnswerCanaryText)
            .And.NotContain(_state.BlindedObservationNode.ToString());

        using var cbDoc = JsonDocument.Parse(cbBody);
        var cbRoot = cbDoc.RootElement;
        var blindedDigest = cbRoot.GetProperty("casebook_digest_sha256").GetString();
        blindedDigest.Should().NotBeNullOrWhiteSpace();
        var cbObservations = cbRoot.GetProperty("observations").EnumerateArray().ToList();
        var maskedObs = cbObservations.Single(o => o.GetProperty("masked").GetBoolean());
        maskedObs.GetProperty("mask").GetString().Should().Be("Blinded");
        maskedObs.GetProperty("ref").GetString().Should().Be("masked");
        maskedObs.GetProperty("collection_instance_ref").ValueKind.Should().Be(JsonValueKind.Null);
        cbObservations.Single(o => !o.GetProperty("masked").GetBoolean()).GetProperty("display_value").GetString().Should().Contain("185");
        var maskedVerification = cbRoot.GetProperty("verifications").EnumerateArray().Single(v => v.GetProperty("observation_ref").GetString() == "masked");
        maskedVerification.GetProperty("verified_by").ValueKind.Should().Be(JsonValueKind.Null);
        maskedVerification.GetProperty("reason_for_change").GetString().Should().Be("masked");
        var maskedQueries = cbRoot.GetProperty("queries").EnumerateArray().Where(q => q.GetProperty("target_ref").GetString() == "masked").ToList();
        maskedQueries.Should().HaveCount(2);
        maskedQueries.Should().OnlyContain(q => q.GetProperty("text").GetString() == "masked" && q.GetProperty("asserted_by").ValueKind == JsonValueKind.Null);
        cbRoot.GetProperty("audit_trail").EnumerateArray().Single(a => a.GetProperty("sequence").GetInt64() == 101)
            .GetProperty("reason_for_change").GetString().Should().Be("masked");

        // Verify CasebookDigestSha256 leaks zero treatment entropy when the underlying blinded treatment arm differs.
        var alternateState = new SeededClinicalState(AlternateCanaryArm, _state);
        await using (var alternateFactory = new KestrelKernelHostFactory(alternateState))
        {
            alternateFactory.EnsureStarted();
            using var alternateClient = alternateFactory.CreateKestrelSignedClient(blindedIdentity);
            var (altCbStatus, altCbBody) = await SendAsync(
                alternateClient,
                HttpMethod.Get,
                $"/api/v1/kernel/enrollments/{enrollmentRef}/casebook?format=json",
                ct: ct);
            altCbStatus.Should().Be(HttpStatusCode.OK, altCbBody);
            altCbBody.Should().NotContain(AlternateCanaryArm);
            using var altDoc = JsonDocument.Parse(altCbBody);
            altDoc.RootElement.GetProperty("casebook_digest_sha256").GetString().Should().Be(blindedDigest);
        }

        // 3. Direct observation GET -> 403 Forbidden on blinded observation, 200 OK on non-blinded observation
        var (obsStatus, obsBody) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/observations/{blindedObsRef}", ct: ct);
        obsStatus.Should().Be(HttpStatusCode.Forbidden, obsBody);
        obsBody.Should().NotContain(CanaryArm);

        var (nonBlindedObsStatus, nonBlindedObsBody) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/observations/{nonBlindedObsRef}", ct: ct);
        nonBlindedObsStatus.Should().Be(HttpStatusCode.OK, nonBlindedObsBody);
        nonBlindedObsBody.Should().Contain("185");

        // 4. Execution eCRF form GET -> masks blinded item, signed_values, assertions, and locks; keeps non-blinded item readable
        var (formStatus, formBody) = await SendAsync(client, HttpMethod.Get, $"/api/v1/execution/forms/{_state.FormId.Value:D}", ct: ct);
        formStatus.Should().Be(HttpStatusCode.OK, formBody);
        formBody.Should().NotContain(CanaryArm)
            .And.NotContain(BlindedAssertionCanaryText)
            .And.NotContain(BlindedAnswerCanaryText)
            .And.NotContain(_state.BlindedObservationId.Value.ToString("D"));

        using var formDoc = JsonDocument.Parse(formBody);
        var formItems = formDoc.RootElement.GetProperty("items").EnumerateArray().ToList();
        var trtItem = formItems.Single(i => i.GetProperty("item_id").GetString() == "trt_arm");
        trtItem.GetProperty("observation_id").ValueKind.Should().Be(JsonValueKind.Null);
        trtItem.GetProperty("recorded_by").ValueKind.Should().Be(JsonValueKind.Null);
        trtItem.GetProperty("value").GetProperty("type").GetString().Should().Be("missing");
        trtItem.GetProperty("value").GetProperty("reason").GetString().Should().Be("Masked");
        formItems.Single(i => i.GetProperty("item_id").GetString() == "sbp")
            .GetProperty("value").GetProperty("value").GetDecimal().Should().Be(185m);

        var signedValues = formDoc.RootElement.GetProperty("signatures")[0].GetProperty("signed_values").EnumerateArray().ToList();
        var maskedSigned = signedValues.Single(sv => sv.GetProperty("item_id").GetString() == "trt_arm");
        maskedSigned.GetProperty("observation_id").GetGuid().Should().Be(Guid.Empty);
        maskedSigned.GetProperty("value").GetProperty("reason").GetString().Should().Be("Masked");
        signedValues.Single(sv => sv.GetProperty("item_id").GetString() == "sbp")
            .GetProperty("value").GetProperty("value").GetDecimal().Should().Be(185m);

        // 5. Intelligence MCP tools (get_node & explain) -> NotFoundOrNotVisible even though agent:mcp holds enrollment.unblind (K-AI-6)
        var mcpGetBlindedPayload = BuildMcpToolCall(KernelToolNames.GetNode, new { node = _state.BlindedObservationNode.ToString() });
        var (mcpObsStatus, mcpObsBody) = await SendAsync(
            client,
            HttpMethod.Post,
            IntelligenceMcp.Route,
            mcpGetBlindedPayload,
            accept: "application/json, text/event-stream",
            extraHeaders: [("MCP-Protocol-Version", "2025-06-18")],
            ct: ct);
        mcpObsStatus.Should().Be(HttpStatusCode.OK, mcpObsBody);
        mcpObsBody.Should().NotContain(CanaryArm)
            .And.Contain(ToolErrors.NotFoundOrNotVisible);

        var mcpExplainBlindedPayload = BuildMcpToolCall(KernelToolNames.Explain, new { node = _state.BlindedFindingNode.ToString() });
        var (mcpExplainStatus, mcpExplainBody) = await SendAsync(
            client,
            HttpMethod.Post,
            IntelligenceMcp.Route,
            mcpExplainBlindedPayload,
            accept: "application/json, text/event-stream",
            extraHeaders: [("MCP-Protocol-Version", "2025-06-18")],
            ct: ct);
        mcpExplainStatus.Should().Be(HttpStatusCode.OK, mcpExplainBody);
        mcpExplainBody.Should().NotContain(CanaryArm)
            .And.Contain(ToolErrors.NotFoundOrNotVisible);

        var mcpExplainNonBlindedPayload = BuildMcpToolCall(KernelToolNames.Explain, new { node = NodeRef.For(_state.NonBlindedFindingId).ToString() });
        var (mcpExplainNonBlindedStatus, mcpExplainNonBlindedBody) = await SendAsync(
            client,
            HttpMethod.Post,
            IntelligenceMcp.Route,
            mcpExplainNonBlindedPayload,
            accept: "application/json, text/event-stream",
            extraHeaders: [("MCP-Protocol-Version", "2025-06-18")],
            ct: ct);
        mcpExplainNonBlindedStatus.Should().Be(HttpStatusCode.OK, mcpExplainNonBlindedBody);
        mcpExplainNonBlindedBody.Should().Contain(NonBlindedRuleId).And.NotContain(ToolErrors.NotFoundOrNotVisible);

        var mcpGetNonBlindedPayload = BuildMcpToolCall(KernelToolNames.GetNode, new { node = _state.NonBlindedObservationNode.ToString() });
        var (mcpNonBlindedStatus, mcpNonBlindedBody) = await SendAsync(
            client,
            HttpMethod.Post,
            IntelligenceMcp.Route,
            mcpGetNonBlindedPayload,
            accept: "application/json, text/event-stream",
            extraHeaders: [("MCP-Protocol-Version", "2025-06-18")],
            ct: ct);
        mcpNonBlindedStatus.Should().Be(HttpStatusCode.OK, mcpNonBlindedBody);
        mcpNonBlindedBody.Should().Contain("185");

        // 6. Study validation findings list and direct finding GET
        var (findingsStatus, findingsBody) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/studies/{studyRef}/validation/findings", ct: ct);
        findingsStatus.Should().Be(HttpStatusCode.OK, findingsBody);
        findingsBody.Should().NotContain(CanaryArm)
            .And.NotContain(BlindedRuleId)
            .And.Contain(NonBlindedRuleId);

        var (findingDetailStatus, findingDetailBody) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/validation/findings/{blindedFindingRef}", ct: ct);
        findingDetailStatus.Should().Be(HttpStatusCode.Forbidden, findingDetailBody);
        findingDetailBody.Should().NotContain(CanaryArm);

        // 7. Study rule-pack preview -> suppresses EC-BLIND-01 & CanaryArm while preserving EC-VS-01
        var (packPreviewStatus, packPreviewBody) = await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/kernel/studies/{studyRef}/rule-packs/{rulePackRef}/preview",
            "{}",
            ct: ct);
        packPreviewStatus.Should().Be(HttpStatusCode.OK, packPreviewBody);
        packPreviewBody.Should().NotContain(CanaryArm)
            .And.NotContain(BlindedRuleId)
            .And.Contain(NonBlindedRuleId);

        // 8. Execution form preview -> suppresses EC-BLIND-01 & CanaryArm while preserving EC-VS-01
        var (formPreviewStatus, formPreviewBody) = await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/execution/forms/{_state.FormId.Value:D}/preview",
            "{\"values\":[]}",
            ct: ct);
        formPreviewStatus.Should().Be(HttpStatusCode.OK, formPreviewBody);
        formPreviewBody.Should().NotContain(CanaryArm)
            .And.NotContain(BlindedRuleId)
            .And.Contain(NonBlindedRuleId);

        // 9. Deterministic ValidationFindingId.For probe and decision routes (K-FIND-1, K-AUTH-4)
        using var findingsDoc = JsonDocument.Parse(findingsBody);
        var visibleFinding = findingsDoc.RootElement.GetProperty("findings")[0];
        var discoveredPackKey = visibleFinding.GetProperty("pack_key").GetString()!;
        var discoveredFormId = visibleFinding.GetProperty("record_key").GetString()!;
        var discoveredTenant = _state.Tenant;
        var state = _state;
        var blindedClient = client;

        var derivedActiveFindingId = ValidationFindingId.For(
            discoveredTenant,
            new FindingIdentity(
                state.StudyId,
                ValidationStage.Collection,
                discoveredPackKey,
                "EC-BLIND-01",
                new FindingTarget(FindingTargetKind.DatasetRecord, "cs.day1", discoveredFormId, "trt_arm")));
        var derivedPlaceboFindingId = ValidationFindingId.For(
            discoveredTenant,
            new FindingIdentity(
                state.StudyId,
                ValidationStage.Collection,
                discoveredPackKey,
                "EC-BLIND-PLACEBO",
                new FindingTarget(FindingTargetKind.DatasetRecord, "cs.day1", discoveredFormId, "trt_arm")));

        derivedActiveFindingId.Should().Be(_state.BlindedFindingId);

        var derivedActiveNodeText = NodeRef.For(derivedActiveFindingId).ToString();
        var derivedPlaceboNodeText = NodeRef.For(derivedPlaceboFindingId).ToString();
        var derivedActiveRef = Uri.EscapeDataString(derivedActiveNodeText);
        var derivedPlaceboRef = Uri.EscapeDataString(derivedPlaceboNodeText);

        var acceptSigToken = TestSecurityContext.CreateSigToken(
            blindedIdentity.UserId,
            KernelValidationEndpoints.AcceptFindingAction(derivedActiveNodeText),
            KernelValidationEndpoints.AcceptFindingSemanticAction,
            blindedIdentity.Roles,
            issuedAt: T0.AddMinutes(5));
        var explainSigToken = TestSecurityContext.CreateSigToken(
            blindedIdentity.UserId,
            KernelValidationEndpoints.ExplainFindingAction(derivedActiveNodeText),
            KernelValidationEndpoints.ExplainFindingSemanticAction,
            blindedIdentity.Roles,
            issuedAt: T0.AddMinutes(5));

        var (_, canIActiveBody) = await SendAsync(blindedClient, HttpMethod.Get,
            $"/api/v1/kernel/authorization/can-i?capability=validation_finding.investigate&resource={derivedActiveRef}", ct: ct);
        var (_, canIPlaceboBody) = await SendAsync(blindedClient, HttpMethod.Get,
            $"/api/v1/kernel/authorization/can-i?capability=validation_finding.investigate&resource={derivedPlaceboRef}", ct: ct);
        var (_, cmdsActiveBody) = await SendAsync(blindedClient, HttpMethod.Get,
            $"/api/v1/kernel/nodes/{derivedActiveRef}/commands", ct: ct);
        var (_, cmdsPlaceboBody) = await SendAsync(blindedClient, HttpMethod.Get,
            $"/api/v1/kernel/nodes/{derivedPlaceboRef}/commands", ct: ct);

        _state.DispatchedCommands.Clear();
        var (invCode, invBody) = await SendCommandAsync(blindedClient,
            $"/api/v1/kernel/validation/findings/{derivedActiveRef}/investigate", "idem-inv-1", "Blinded DM", null, null, ct);
        var (clsCode, clsBody) = await SendCommandAsync(blindedClient,
            $"/api/v1/kernel/validation/findings/{derivedActiveRef}/classify", "idem-cls-1", "Blinded DM", null, "{\"classification\":\"DataIssue\"}", ct);
        var (accCode, accBody) = await SendCommandAsync(blindedClient,
            $"/api/v1/kernel/validation/findings/{derivedActiveRef}/accept", "idem-acc-1", "Blinded DM", acceptSigToken, "{\"rationale\":\"Accepted.\"}", ct);
        var (reopenCode, reopenBody) = await SendCommandAsync(blindedClient,
            $"/api/v1/kernel/validation/findings/{derivedActiveRef}/reopen", "idem-reopen-1", "Blinded DM", null, null, ct);
        var (expCode, expBody) = await SendCommandAsync(blindedClient,
            $"/api/v1/kernel/validation/findings/{derivedActiveRef}/explain", "idem-explain-1", "Blinded DM", explainSigToken, "{\"explanation\":\"Explained.\"}", ct);

        canIActiveBody.Should().Contain("\"permitted\":false");
        canIPlaceboBody.Should().Contain("\"permitted\":false");
        cmdsActiveBody.Should().NotContain("\"permitted\":true");

        // Uniform non-confirming GrantCondition denial across detected blinded and undetected findings (tenant-scoped caller).
        canIPlaceboBody.Replace(derivedPlaceboNodeText, derivedActiveNodeText, StringComparison.Ordinal)
            .Should().Be(canIActiveBody);
        cmdsPlaceboBody.Replace(derivedPlaceboNodeText, derivedActiveNodeText, StringComparison.Ordinal)
            .Should().Be(cmdsActiveBody);

        foreach (var (code, body) in new[] { (invCode, invBody), (clsCode, clsBody), (accCode, accBody), (reopenCode, reopenBody), (expCode, expBody) })
        {
            code.Should().Be(HttpStatusCode.Forbidden);
            body.Should().NotContain(CanaryArm);
        }

        // Hidden findings must be rejected before dispatching Attest or any finding decision command.
        _state.DispatchedCommands.Should().BeEmpty();

        // Also verify uniform non-confirming denials for a study-scoped blinded data manager.
        var studyDmIdentity = blindedIdentity with { UserId = BlindedStudyDmPerson.Id, Roles = ["Data Manager"] };
        using var studyDmClient = _factory.CreateKestrelSignedClient(studyDmIdentity);
        var (_, dmCanIActive) = await SendAsync(studyDmClient, HttpMethod.Get,
            $"/api/v1/kernel/authorization/can-i?capability=validation_finding.investigate&resource={derivedActiveRef}", ct: ct);
        var (_, dmCanIPlacebo) = await SendAsync(studyDmClient, HttpMethod.Get,
            $"/api/v1/kernel/authorization/can-i?capability=validation_finding.investigate&resource={derivedPlaceboRef}", ct: ct);
        dmCanIActive.Should().Contain("\"permitted\":false");
        dmCanIPlacebo.Replace(derivedPlaceboNodeText, derivedActiveNodeText, StringComparison.Ordinal)
            .Should().Be(dmCanIActive);
    }

    /// <summary>
    /// Acceptance scenario: a qualified unblinded pharmacist with active <c>enrollment.unblind</c> grant retains full read
    /// and preview access across all eight routes, whereas an unqualified pharmacist (<c>K-AUTH-5</c>) is masked.
    /// </summary>
    [Fact(DisplayName = "UnblindedPharmacist_WithActiveGrantAndQualification_RetainsFullAccess_WhileExpiredQualificationMasks")]
    [KernelRule("K-AUTH-4")]
    [KernelRule("K-AUTH-5")]
    [KernelRule("K-AI-6")]
    public async Task UnblindedPharmacistWithActiveGrantAndQualificationRetainsFullAccessWhileExpiredQualificationMasks()
    {
        var ct = TestContext.Current.CancellationToken;
        var pharmacistIdentity = new GatewayIdentity(
            UserId: PharmacistPerson.Id,
            Roles: ["Unblinded Pharmacist"],
            SiteIds: ["SITE-01"],
            TenantId: TestSecurityContext.TenantCode,
            StudyIds: [_state.StudyId.Value.ToString("D")],
            UnblindedAccess: true);

        using var client = _factory!.CreateKestrelSignedClient(pharmacistIdentity);

        var studyRef = Uri.EscapeDataString(_state.StudyNode.ToString());
        var enrollmentRef = Uri.EscapeDataString(_state.EnrollmentNode.ToString());
        var blindedObsRef = Uri.EscapeDataString(_state.BlindedObservationNode.ToString());
        var blindedFindingRef = Uri.EscapeDataString(_state.BlindedFindingNode.ToString());
        var rulePackRef = Uri.EscapeDataString(NodeRef.For(_state.RulePackItemId).ToString());

        var (csvStatus, exportCsv) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/studies/{studyRef}/exports?format=csv", ct: ct);
        csvStatus.Should().Be(HttpStatusCode.OK, exportCsv);
        exportCsv.Should().Contain(CanaryArm).And.Contain(_state.BlindedObservationNode.ToString());

        var (cbStatus, cbBody) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/enrollments/{enrollmentRef}/casebook?format=json", ct: ct);
        cbStatus.Should().Be(HttpStatusCode.OK, cbBody);
        cbBody.Should().Contain(CanaryArm).And.Contain(BlindedAssertionCanaryText).And.Contain(BlindedAnswerCanaryText);

        var (obsStatus, obsBody) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/observations/{blindedObsRef}", ct: ct);
        obsStatus.Should().Be(HttpStatusCode.OK, obsBody);
        obsBody.Should().Contain(CanaryArm);

        var (formStatus, formBody) = await SendAsync(client, HttpMethod.Get, $"/api/v1/execution/forms/{_state.FormId.Value:D}", ct: ct);
        formStatus.Should().Be(HttpStatusCode.OK, formBody);
        formBody.Should().Contain(CanaryArm).And.Contain(_state.BlindedObservationId.Value.ToString("D"));

        var mcpGetPayload = BuildMcpToolCall(KernelToolNames.GetNode, new { node = _state.BlindedObservationNode.ToString() });
        var (mcpObsStatus, mcpObsBody) = await SendAsync(
            client,
            HttpMethod.Post,
            IntelligenceMcp.Route,
            mcpGetPayload,
            accept: "application/json, text/event-stream",
            extraHeaders: [("MCP-Protocol-Version", "2025-06-18")],
            ct: ct);
        mcpObsStatus.Should().Be(HttpStatusCode.OK, mcpObsBody);
        mcpObsBody.Should().Contain(CanaryArm).And.NotContain(ToolErrors.NotFoundOrNotVisible);

        var mcpExplainPayload = BuildMcpToolCall(KernelToolNames.Explain, new { node = _state.BlindedFindingNode.ToString() });
        var (mcpExplainStatus, mcpExplainBody) = await SendAsync(
            client,
            HttpMethod.Post,
            IntelligenceMcp.Route,
            mcpExplainPayload,
            accept: "application/json, text/event-stream",
            extraHeaders: [("MCP-Protocol-Version", "2025-06-18")],
            ct: ct);
        mcpExplainStatus.Should().Be(HttpStatusCode.OK, mcpExplainBody);
        mcpExplainBody.Should().Contain(CanaryArm).And.NotContain(ToolErrors.NotFoundOrNotVisible);

        var (findingsStatus, findingsBody) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/studies/{studyRef}/validation/findings", ct: ct);
        findingsStatus.Should().Be(HttpStatusCode.OK, findingsBody);
        findingsBody.Should().Contain(CanaryArm).And.Contain(BlindedRuleId).And.Contain(NonBlindedRuleId);

        var (findingDetailStatus, findingDetailBody) = await SendAsync(client, HttpMethod.Get, $"/api/v1/kernel/validation/findings/{blindedFindingRef}", ct: ct);
        findingDetailStatus.Should().Be(HttpStatusCode.OK, findingDetailBody);
        findingDetailBody.Should().Contain(CanaryArm);

        var (packPreviewStatus, packPreviewBody) = await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/kernel/studies/{studyRef}/rule-packs/{rulePackRef}/preview",
            "{}",
            ct: ct);
        packPreviewStatus.Should().Be(HttpStatusCode.OK, packPreviewBody);
        packPreviewBody.Should().Contain(BlindedRuleId).And.Contain(CanaryArm);

        var (formPreviewStatus, formPreviewBody) = await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/execution/forms/{_state.FormId.Value:D}/preview",
            "{\"values\":[]}",
            ct: ct);
        formPreviewStatus.Should().Be(HttpStatusCode.OK, formPreviewBody);
        formPreviewBody.Should().Contain(BlindedRuleId).And.Contain(CanaryArm);

        // Unqualified / expired-qualification pharmacist (K-AUTH-5) must be masked even with an active enrollment.unblind grant.
        var expiredPharmacistIdentity = pharmacistIdentity with { UserId = UnqualifiedPharmacistPerson.Id };
        using var expiredClient = _factory.CreateKestrelSignedClient(expiredPharmacistIdentity);

        var (expiredObsStatus, expiredObsBody) = await SendAsync(expiredClient, HttpMethod.Get, $"/api/v1/kernel/observations/{blindedObsRef}", ct: ct);
        expiredObsStatus.Should().Be(HttpStatusCode.Forbidden, expiredObsBody);
        expiredObsBody.Should().NotContain(CanaryArm);

        var (expiredFormStatus, expiredFormBody) = await SendAsync(expiredClient, HttpMethod.Get, $"/api/v1/execution/forms/{_state.FormId.Value:D}", ct: ct);
        expiredFormStatus.Should().Be(HttpStatusCode.OK, expiredFormBody);
        expiredFormBody.Should().NotContain(CanaryArm);

        var (expiredPreviewStatus, expiredPreviewBody) = await SendAsync(
            expiredClient,
            HttpMethod.Post,
            $"/api/v1/execution/forms/{_state.FormId.Value:D}/preview",
            "{\"values\":[]}",
            ct: ct);
        expiredPreviewStatus.Should().Be(HttpStatusCode.OK, expiredPreviewBody);
        expiredPreviewBody.Should().NotContain(BlindedRuleId).And.NotContain(CanaryArm);

        // Expired-qualification pharmacist is also denied on probe and finding decision routes without mutating state.
        var blindedFindingNodeText = _state.BlindedFindingNode.ToString();
        var expiredAcceptSig = TestSecurityContext.CreateSigToken(
            expiredPharmacistIdentity.UserId,
            KernelValidationEndpoints.AcceptFindingAction(blindedFindingNodeText),
            KernelValidationEndpoints.AcceptFindingSemanticAction,
            expiredPharmacistIdentity.Roles,
            issuedAt: T0.AddMinutes(5));
        var expiredExplainSig = TestSecurityContext.CreateSigToken(
            expiredPharmacistIdentity.UserId,
            KernelValidationEndpoints.ExplainFindingAction(blindedFindingNodeText),
            KernelValidationEndpoints.ExplainFindingSemanticAction,
            expiredPharmacistIdentity.Roles,
            issuedAt: T0.AddMinutes(5));

        var (_, expiredCanIBody) = await SendAsync(expiredClient, HttpMethod.Get,
            $"/api/v1/kernel/authorization/can-i?capability=validation_finding.investigate&resource={blindedFindingRef}", ct: ct);
        expiredCanIBody.Should().Contain("\"permitted\":false");

        _state.DispatchedCommands.Clear();
        var (expInvCode, expInvBody) = await SendCommandAsync(expiredClient,
            $"/api/v1/kernel/validation/findings/{blindedFindingRef}/investigate", "idem-exp-inv", "Expired pharm", null, null, ct);
        var (expAccCode, expAccBody) = await SendCommandAsync(expiredClient,
            $"/api/v1/kernel/validation/findings/{blindedFindingRef}/accept", "idem-exp-acc", "Expired pharm", expiredAcceptSig, "{\"rationale\":\"Accepted.\"}", ct);
        var (expExpCode, expExpBody) = await SendCommandAsync(expiredClient,
            $"/api/v1/kernel/validation/findings/{blindedFindingRef}/explain", "idem-exp-explain", "Expired pharm", expiredExplainSig, "{\"explanation\":\"Explained.\"}", ct);

        foreach (var (code, body) in new[] { (expInvCode, expInvBody), (expAccCode, expAccBody), (expExpCode, expExpBody) })
        {
            code.Should().Be(HttpStatusCode.Forbidden);
            body.Should().NotContain(CanaryArm);
        }

        _state.DispatchedCommands.Should().BeEmpty();

        // Qualified unblinded pharmacist receives permitted:true on probes and completes signed decisions on the blinded finding.
        var (_, pharmCanIBody) = await SendAsync(client, HttpMethod.Get,
            $"/api/v1/kernel/authorization/can-i?capability=validation_finding.investigate&resource={blindedFindingRef}", ct: ct);
        pharmCanIBody.Should().Contain("\"permitted\":true");

        var (_, pharmCmdsBody) = await SendAsync(client, HttpMethod.Get,
            $"/api/v1/kernel/nodes/{blindedFindingRef}/commands", ct: ct);
        pharmCmdsBody.Should().Contain("\"permitted\":true").And.NotContain("\"permitted\":false");

        var pharmAcceptSig = TestSecurityContext.CreateSigToken(
            pharmacistIdentity.UserId,
            KernelValidationEndpoints.AcceptFindingAction(blindedFindingNodeText),
            KernelValidationEndpoints.AcceptFindingSemanticAction,
            pharmacistIdentity.Roles,
            issuedAt: T0.AddMinutes(5));
        var pharmExplainSig = TestSecurityContext.CreateSigToken(
            pharmacistIdentity.UserId,
            KernelValidationEndpoints.ExplainFindingAction(blindedFindingNodeText),
            KernelValidationEndpoints.ExplainFindingSemanticAction,
            pharmacistIdentity.Roles,
            issuedAt: T0.AddMinutes(5));

        var (pharmInvCode, pharmInvBody) = await SendCommandAsync(client,
            $"/api/v1/kernel/validation/findings/{blindedFindingRef}/investigate", "idem-pharm-inv", "Pharmacist review", null, null, ct);
        pharmInvCode.Should().Be(HttpStatusCode.OK, pharmInvBody);
        pharmInvBody.Should().Contain("\"status\":\"Investigating\"").And.Contain(CanaryArm);

        var (pharmClsCode, pharmClsBody) = await SendCommandAsync(client,
            $"/api/v1/kernel/validation/findings/{blindedFindingRef}/classify", "idem-pharm-cls", "Pharmacist classify", null, "{\"classification\":\"DataIssue\"}", ct);
        pharmClsCode.Should().Be(HttpStatusCode.OK, pharmClsBody);
        pharmClsBody.Should().Contain("\"classification\":\"DataIssue\"");

        var (pharmAccCode, pharmAccBody) = await SendCommandAsync(client,
            $"/api/v1/kernel/validation/findings/{blindedFindingRef}/accept", "idem-pharm-acc", "Pharmacist accept", pharmAcceptSig, "{\"rationale\":\"Kit verified.\"}", ct);
        pharmAccCode.Should().Be(HttpStatusCode.OK, pharmAccBody);
        pharmAccBody.Should().Contain("\"status\":\"Accepted\"");

        var (pharmReopenCode, pharmReopenBody) = await SendCommandAsync(client,
            $"/api/v1/kernel/validation/findings/{blindedFindingRef}/reopen", "idem-pharm-reopen", "Pharmacist reopen", null, null, ct);
        pharmReopenCode.Should().Be(HttpStatusCode.OK, pharmReopenBody);
        pharmReopenBody.Should().Contain("\"status\":\"Open\"");

        var (pharmExpCode, pharmExpBody) = await SendCommandAsync(client,
            $"/api/v1/kernel/validation/findings/{blindedFindingRef}/explain", "idem-pharm-exp", "Pharmacist explain", pharmExplainSig, "{\"explanation\":\"Dispense confirmed.\"}", ct);
        pharmExpCode.Should().Be(HttpStatusCode.OK, pharmExpBody);
        pharmExpBody.Should().Contain("\"status\":\"Explained\"");
    }

    private static Task<(HttpStatusCode Status, string Body)> SendCommandAsync(
        HttpClient client,
        string path,
        string idempotencyKey,
        string reason,
        string? sigToken,
        string? jsonBody,
        CancellationToken ct = default)
    {
        var headers = new List<(string Name, string Value)>
        {
            ("Idempotency-Key", idempotencyKey),
            (GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason)),
        };
        if (!string.IsNullOrWhiteSpace(sigToken))
        {
            headers.Add((GatewayHeaders.SigToken, sigToken));
        }

        return SendAsync(client, HttpMethod.Post, path, jsonBody, extraHeaders: headers, ct: ct);
    }

    /// <summary>
    /// Regression scenario: batched protocol design, concept resolution, and enrollment unblind evaluation eliminate
    /// per-concept and per-enrollment round-trips across form fallback reads, previews, study query worklists, and
    /// <c>K-AI-6</c> automation-agent authorization while preserving <c>K-AUTH-4</c> and <c>K-AUTH-5</c>.
    /// </summary>
    [Fact(DisplayName = "BatchedBlindingClassification_AndStudyQueryWorklist_EnforceKAuth4KAuth5AndKAi6WithoutPerItemOrPerEnrollmentLoops")]
    [KernelRule("K-AUTH-4")]
    [KernelRule("K-AUTH-5")]
    [KernelRule("K-AI-6")]
    public async Task BatchedBlindingClassificationAndStudyQueryWorklistEnforceKAuth4KAuth5AndKAi6WithoutPerItemOrPerEnrollmentLoops()
    {
        var ct = TestContext.Current.CancellationToken;
        using var isolatedState = new SeededClinicalState(CanaryArm);
        await using var host = new KestrelKernelHostFactory(isolatedState);
        host.EnsureStarted();

        var blindedQuery = FormQuery.Raise(
            FormQueryId.New(),
            isolatedState.Tenant,
            isolatedState.FormId,
            isolatedState.EnrollmentId,
            "trt_arm",
            isolatedState.BlindedObservationId,
            1,
            BlindedAssertionCanaryText,
            AssertionId.New(),
            "unblinded-dm@sponsor.test",
            T0.AddMinutes(1),
            "Kit audit").Query;
        var nonBlindedQuery = FormQuery.Raise(
            FormQueryId.New(),
            isolatedState.Tenant,
            isolatedState.FormId,
            isolatedState.EnrollmentId,
            "sbp",
            isolatedState.NonBlindedObservationId,
            1,
            "Repeat seated BP check",
            AssertionId.New(),
            "cra@sponsor.test",
            T0.AddMinutes(2),
            "Vitals SDV").Query;

        await using (var db = isolatedState.Create(isolatedState.Tenant))
        {
            using var _ = db.PermitCommandWrites();
            db.FormQueries.AddRange(blindedQuery, nonBlindedQuery);
            await db.SaveChangesAsync(ct);
        }

        var coordinatorIdentity = new GatewayIdentity(
            UserId: BlindedPerson.Id,
            Roles: ["Clinical Research Coordinator"],
            SiteIds: ["SITE-01"],
            TenantId: TestSecurityContext.TenantCode,
            StudyIds: [isolatedState.StudyId.Value.ToString("D")],
            UnblindedAccess: false);
        using var coordinatorClient = host.CreateKestrelSignedClient(coordinatorIdentity);

        var studyRef = Uri.EscapeDataString(isolatedState.StudyNode.ToString());
        var rulePackRef = Uri.EscapeDataString(NodeRef.For(isolatedState.RulePackItemId).ToString());

        isolatedState.ResetBatchTelemetry();
        var (formStatus, formBody) = await SendAsync(coordinatorClient, HttpMethod.Get, $"/api/v1/execution/forms/{isolatedState.FormId.Value:D}", ct: ct);
        formStatus.Should().Be(HttpStatusCode.OK, formBody);
        formBody.Should().NotContain(CanaryArm);
        isolatedState.SingleGetDesignCalls.Should().Be(0);
        isolatedState.BatchGetDesignsCalls.Should().Be(1);
        isolatedState.SingleResolveConceptCalls.Should().Be(0);
        isolatedState.BatchResolveConceptCalls.Should().Be(1);

        isolatedState.ResetBatchTelemetry();
        var (previewStatus, previewBody) = await SendAsync(
            coordinatorClient,
            HttpMethod.Post,
            $"/api/v1/execution/forms/{isolatedState.FormId.Value:D}/preview",
            "{\"values\":[]}",
            ct: ct);
        previewStatus.Should().Be(HttpStatusCode.OK, previewBody);
        previewBody.Should().NotContain(CanaryArm).And.NotContain(BlindedRuleId).And.Contain(NonBlindedRuleId);
        isolatedState.SingleGetDesignCalls.Should().Be(0);
        isolatedState.BatchGetDesignsCalls.Should().Be(1);
        isolatedState.SingleResolveConceptCalls.Should().Be(0);
        isolatedState.BatchResolveConceptCalls.Should().Be(1);

        isolatedState.ResetBatchTelemetry();
        var (packPreviewStatus, packPreviewBody) = await SendAsync(
            coordinatorClient,
            HttpMethod.Post,
            $"/api/v1/kernel/studies/{studyRef}/rule-packs/{rulePackRef}/preview",
            "{}",
            ct: ct);
        packPreviewStatus.Should().Be(HttpStatusCode.OK, packPreviewBody);
        packPreviewBody.Should().NotContain(CanaryArm).And.NotContain(BlindedRuleId).And.Contain(NonBlindedRuleId);
        isolatedState.SingleGetDesignCalls.Should().Be(0);
        isolatedState.BatchGetDesignsCalls.Should().Be(1);
        isolatedState.SingleResolveConceptCalls.Should().Be(0);
        isolatedState.BatchResolveConceptCalls.Should().Be(1);
        isolatedState.SinglePathOfCalls.Should().Be(0);

        // Precomputed blinding sets (matching KernelCollectionInstanceRecords in PostgreSQL):
        // GET /forms/{id} reuses BlindedItemIds/BlindedObservationIds with zero protocol/concept queries,
        // and GET /forms/queries?study_id=... batches enrollment unblind checks with zero single PathOfAsync calls.
        isolatedState.PopulateFormBlindingSets();
        isolatedState.ResetBatchTelemetry();
        var (precomputedFormStatus, precomputedFormBody) = await SendAsync(
            coordinatorClient,
            HttpMethod.Get,
            $"/api/v1/execution/forms/{isolatedState.FormId.Value:D}",
            ct: ct);
        precomputedFormStatus.Should().Be(HttpStatusCode.OK, precomputedFormBody);
        precomputedFormBody.Should().NotContain(CanaryArm);
        isolatedState.SingleGetDesignCalls.Should().Be(0);
        isolatedState.BatchGetDesignsCalls.Should().Be(0);
        isolatedState.SingleResolveConceptCalls.Should().Be(0);
        isolatedState.BatchResolveConceptCalls.Should().Be(0);

        // Study query worklist: blinded coordinator gets masked blinded query, readable non-blinded query, zero single PathOfAsync calls.
        isolatedState.ResetBatchTelemetry();
        var (coordQueriesStatus, coordQueriesBody) = await SendAsync(
            coordinatorClient,
            HttpMethod.Get,
            $"/api/v1/execution/forms/queries?study_id={isolatedState.StudyId.Value:D}",
            ct: ct);
        coordQueriesStatus.Should().Be(HttpStatusCode.OK, coordQueriesBody);
        coordQueriesBody.Should().NotContain(CanaryArm)
            .And.NotContain(BlindedAssertionCanaryText)
            .And.Contain("\"text\":\"masked\"")
            .And.Contain("Repeat seated BP check");
        isolatedState.SinglePathOfCalls.Should().Be(0);

        // Study query worklist: qualified pharmacist sees blinded query text via batched PathsOfAsync.
        var pharmacistIdentity = new GatewayIdentity(
            UserId: PharmacistPerson.Id,
            Roles: ["Unblinded Pharmacist"],
            SiteIds: ["SITE-01"],
            TenantId: TestSecurityContext.TenantCode,
            StudyIds: [isolatedState.StudyId.Value.ToString("D")],
            UnblindedAccess: true);
        using var pharmacistClient = host.CreateKestrelSignedClient(pharmacistIdentity);

        isolatedState.ResetBatchTelemetry();
        var (pharmQueriesStatus, pharmQueriesBody) = await SendAsync(
            pharmacistClient,
            HttpMethod.Get,
            $"/api/v1/execution/forms/queries?study_id={isolatedState.StudyId.Value:D}",
            ct: ct);
        pharmQueriesStatus.Should().Be(HttpStatusCode.OK, pharmQueriesBody);
        pharmQueriesBody.Should().Contain(BlindedAssertionCanaryText).And.Contain("Repeat seated BP check");
        isolatedState.SinglePathOfCalls.Should().Be(0);
        isolatedState.BatchPathsOfCalls.Should().BeGreaterThanOrEqualTo(1);

        // Study query worklist: expired-qualification pharmacist (K-AUTH-5) is masked via batched evaluation.
        using var expiredClient = host.CreateKestrelSignedClient(pharmacistIdentity with { UserId = UnqualifiedPharmacistPerson.Id });
        isolatedState.ResetBatchTelemetry();
        var (expiredQueriesStatus, expiredQueriesBody) = await SendAsync(
            expiredClient,
            HttpMethod.Get,
            $"/api/v1/execution/forms/queries?study_id={isolatedState.StudyId.Value:D}",
            ct: ct);
        expiredQueriesStatus.Should().Be(HttpStatusCode.OK, expiredQueriesBody);
        expiredQueriesBody.Should().NotContain(CanaryArm)
            .And.NotContain(BlindedAssertionCanaryText)
            .And.Contain("\"text\":\"masked\"")
            .And.Contain("Repeat seated BP check");
        isolatedState.SinglePathOfCalls.Should().Be(0);

        // K-AI-6 batched AuthorityEvaluator.EvaluateManyAsync: automation agent intersects agent and requester across multiple enrollments.
        var evaluator = new AuthorityEvaluator(isolatedState, new KernelAuthorityOptions(new HashSet<string>(StringComparer.Ordinal)));
        var otherSiteEnrollment = NodeRef.For(EnrollmentId.New());
        NodeRef[] targetEnrollments = [isolatedState.EnrollmentNode, otherSiteEnrollment];
        var unblindCapability = new Capability("enrollment.unblind", VerbClass.Read);
        var evalAt = T0.AddMinutes(5);

        isolatedState.ResetBatchTelemetry();
        var blindedRequesterDecisions = await evaluator.EvaluateManyAsync(
            McpAgentActor.ActingFor(BlindedPerson),
            isolatedState.Tenant,
            unblindCapability,
            targetEnrollments,
            evalAt,
            evalAt,
            ct);
        blindedRequesterDecisions[isolatedState.EnrollmentNode].Permitted.Should().BeFalse();
        blindedRequesterDecisions[isolatedState.EnrollmentNode].Conditions
            .Should().Contain(c => !c.Met && c.Code == AuthorityEvaluator.RequesterCondition);
        blindedRequesterDecisions[otherSiteEnrollment].Permitted.Should().BeFalse();
        blindedRequesterDecisions[otherSiteEnrollment].Conditions
            .Should().Contain(c => !c.Met && c.Code == AuthorityEvaluator.RequesterCondition);
        isolatedState.SinglePathOfCalls.Should().Be(0);

        isolatedState.ResetBatchTelemetry();
        var pharmacistRequesterDecisions = await evaluator.EvaluateManyAsync(
            McpAgentActor.ActingFor(PharmacistPerson),
            isolatedState.Tenant,
            unblindCapability,
            targetEnrollments,
            evalAt,
            evalAt,
            ct);
        pharmacistRequesterDecisions[isolatedState.EnrollmentNode].Permitted.Should().BeTrue();
        pharmacistRequesterDecisions[otherSiteEnrollment].Permitted.Should().BeFalse();
        pharmacistRequesterDecisions[otherSiteEnrollment].Conditions
            .Should().Contain(c => !c.Met && c.Code == AuthorityEvaluator.RequesterCondition);
        isolatedState.SinglePathOfCalls.Should().Be(0);
        isolatedState.BatchPathsOfCalls.Should().BeGreaterThanOrEqualTo(1);
    }

    private static string BuildMcpToolCall(string toolName, object arguments) =>
        JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name = toolName, arguments },
        });

    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? jsonBody = null,
        string? accept = null,
        IReadOnlyList<(string Name, string Value)>? extraHeaders = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, path);
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        if (!string.IsNullOrWhiteSpace(accept))
        {
            foreach (var mediaType in accept.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
            }
        }

        if (extraHeaders is not null)
        {
            foreach (var (name, value) in extraHeaders)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return (response.StatusCode, body);
    }

    /// <summary>
    /// Boots the real <see cref="Cadence.Kernel.Host.Program"/> entry point on an actual loopback Kestrel listener
    /// (<c>http://127.0.0.1:0</c>) with in-memory clinical read projections and the real kernel authorization,
    /// <see cref="GrantNodeVisibility"/>, <c>CollectionValidationService</c>, Execution module, and Intelligence MCP stack.
    /// </summary>
    private sealed class KestrelKernelHostFactory(SeededClinicalState state) : WebApplicationFactory<Cadence.Kernel.Host.Program>
    {
        private bool _started;

        public void EnsureStarted()
        {
            if (_started)
            {
                return;
            }

            UseKestrel(0);
            StartServer();
            _started = true;
        }

        public HttpClient CreateKestrelSignedClient(GatewayIdentity identity)
        {
            EnsureStarted();
            var handler = new TestSecurityContext.GatewaySigningHandler(identity)
            {
                InnerHandler = new SocketsHttpHandler(),
            };
            return new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = ClientOptions.BaseAddress,
            };
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(IntelligenceModuleEnvironment);
            builder.UseTestGatewaySecret().UseTestBiometricLivenessKey().UseTestPostgresConnectionString();
            builder.ConfigureServices(services =>
            {
                // Remove background DB-polling hosted services so the Kestrel host runs purely in-memory.
                var backgroundJobs = services
                    .Where(d => d.ServiceType == typeof(IHostedService)
                        && d.ImplementationType?.Namespace?.StartsWith("Cadence.Kernel.Persistence", StringComparison.Ordinal) == true)
                    .ToList();
                foreach (var job in backgroundJobs)
                {
                    services.Remove(job);
                }

                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new FakeClock(T0.AddMinutes(5)));

                services.RemoveAll<ISigTokenUseStore>();
                services.RemoveAll<IDurableSigTokenUseStore>();
                services.AddSingleton<ISigTokenUseStore>(state);
                services.AddSingleton<IDurableSigTokenUseStore>(state);

                services.RemoveAll<ITenantDirectory>();
                services.AddSingleton<ITenantDirectory>(state);

                services.RemoveAll<ITenantEntitlementReader>();
                services.AddSingleton<ITenantEntitlementReader>(state);

                services.RemoveAll<IAuthorityStore>();
                services.AddSingleton<IAuthorityStore>(state);

                services.RemoveAll<IStudyDirectory>();
                services.AddSingleton<IStudyDirectory>(state);

                services.RemoveAll<IKernelNavigator>();
                services.AddSingleton<IKernelNavigator>(state);

                services.RemoveAll<IProtocolVersionReader>();
                services.AddSingleton<IProtocolVersionReader>(state);

                services.RemoveAll<IConceptRegistry>();
                services.AddSingleton<IConceptRegistry>(state);

                services.RemoveAll<IObservationRecords>();
                services.AddSingleton<IObservationRecords>(state);

                services.RemoveAll<ICollectionInstanceRecords>();
                services.AddSingleton<ICollectionInstanceRecords>(state);

                services.RemoveAll<IKernelExportReader>();
                services.AddSingleton<IKernelExportReader>(state);

                services.RemoveAll<IStandardsRegistry>();
                services.AddSingleton<IStandardsRegistry>(state);

                services.RemoveAll<IValidationFindings>();
                services.AddSingleton<IValidationFindings>(state);

                services.RemoveAll<IKernelCommandSender>();
                services.AddSingleton<IKernelCommandSender>(state);

                services.RemoveAll<IModuleTenantContextFactory<ExecutionModuleDbContext>>();
                services.AddSingleton<IModuleTenantContextFactory<ExecutionModuleDbContext>>(state);
            });
        }

        private const string IntelligenceModuleEnvironment = "Testing";
    }

    private sealed class SeededClinicalState :
        IDisposable,
        ITenantDirectory,
        ITenantEntitlementReader,
        IDurableSigTokenUseStore,
        IAuthorityStore,
        IStudyDirectory,
        IKernelNavigator,
        IProtocolVersionReader,
        IConceptRegistry,
        IObservationRecords,
        ICollectionInstanceRecords,
        IKernelExportReader,
        IStandardsRegistry,
        IValidationFindings,
        IKernelCommandSender,
        IModuleTenantContextFactory<ExecutionModuleDbContext>
    {
        private readonly string _inMemoryDbName;
        private readonly ConcurrentDictionary<string, DateTimeOffset> _usedSigTokens = new(StringComparer.Ordinal);
        private readonly Grant[] _grants;
        private readonly Qualification[] _qualifications;
        private readonly QualificationRequirement[] _unblindRequirements;
        private readonly ScopePath _studyPath;
        private readonly ScopePath _sitePath;
        private readonly StudyDirectoryEntry _studyEntry;
        private readonly OrganizationDirectoryEntry _sponsorEntry;
        private readonly SiteDirectoryEntry _siteEntry;
        private readonly StudySiteDirectoryEntry _studySiteEntry;
        private readonly ProtocolDesign _design;
        private readonly ProtocolVersionSummary _protocolSummary;
        private readonly ConceptVersionView _blindedConcept;
        private readonly ConceptVersionView _sbpConcept;
        private readonly ObservationRecord _blindedObservation;
        private readonly ObservationRecord _nonBlindedObservation;
        private CollectionInstanceRecord _formRecord;
        private readonly StudyExportSnapshot _studySnapshot;
        private readonly EnrollmentCasebookSnapshot _casebookSnapshot;
        private readonly LibraryItemView _rulePackItem;
        private readonly StudyStandardsProfileView _standardsProfile;
        private ValidationFindingView _blindedFinding;
        private readonly ValidationFindingView _nonBlindedFinding;

        public List<ICommand> DispatchedCommands { get; } = [];

        public SeededClinicalState(string treatmentArmValue, SeededClinicalState? shareIdsFrom = null)
        {
            Tenant = shareIdsFrom?.Tenant ?? TenantId.New();
            SponsorId = shareIdsFrom?.SponsorId ?? OrganizationId.New();
            StudyId = shareIdsFrom?.StudyId ?? StudyId.New();
            SiteId = shareIdsFrom?.SiteId ?? StudySiteId.New();
            PhysicalSiteId = shareIdsFrom?.PhysicalSiteId ?? Cadence.Kernel.SiteId.New();
            ProtocolVersionId = shareIdsFrom?.ProtocolVersionId ?? ProtocolVersionId.New();
            EnrollmentId = shareIdsFrom?.EnrollmentId ?? EnrollmentId.New();
            ParticipantId = shareIdsFrom?.ParticipantId ?? ParticipantId.New();
            FormId = shareIdsFrom?.FormId ?? CollectionInstanceId.New();
            BlindedObservationId = shareIdsFrom?.BlindedObservationId ?? ObservationId.New();
            NonBlindedObservationId = shareIdsFrom?.NonBlindedObservationId ?? ObservationId.New();
            BlindedConceptRef = shareIdsFrom?.BlindedConceptRef ?? ConceptRef.New();
            SbpConceptRef = shareIdsFrom?.SbpConceptRef ?? ConceptRef.New();
            RulePackItemId = shareIdsFrom?.RulePackItemId ?? LibraryItemId.New();
            StandardsProfileId = shareIdsFrom?.StandardsProfileId ?? StudyStandardsProfileId.New();
            var blindedFindingTarget = new FindingTarget(FindingTargetKind.DatasetRecord, "cs.day1", FormId.Value.ToString("D"), "trt_arm");
            var nonBlindedFindingTarget = new FindingTarget(FindingTargetKind.DatasetRecord, "cs.day1", FormId.Value.ToString("D"), "sbp");
            BlindedFindingId = shareIdsFrom?.BlindedFindingId
                ?? ValidationFindingId.For(
                    Tenant,
                    new FindingIdentity(StudyId, ValidationStage.Collection, "pack.day1", BlindedRuleId, blindedFindingTarget));
            NonBlindedFindingId = shareIdsFrom?.NonBlindedFindingId
                ?? ValidationFindingId.For(
                    Tenant,
                    new FindingIdentity(StudyId, ValidationStage.Collection, "pack.day1", NonBlindedRuleId, nonBlindedFindingTarget));
            RunId = shareIdsFrom?.RunId ?? ValidationRunId.New();

            var blindedVerifyId = shareIdsFrom?._blindedObservation.Assertions[0].AssertionId ?? AssertionId.New();
            var blindedQueryId = shareIdsFrom?._blindedObservation.Assertions[1].AssertionId ?? AssertionId.New();
            var blindedAnswerId = shareIdsFrom?._blindedObservation.Assertions[2].AssertionId ?? AssertionId.New();
            var blindedLockId = shareIdsFrom?._blindedObservation.Locks[0].LockId ?? AssertionId.New();
            var blindedLockAttestationId = shareIdsFrom?._blindedObservation.Locks[0].AttestationId ?? AttestationId.New();
            var blindedObsAttestationId = shareIdsFrom?._studySnapshot.Attestations[0].AttestationId ?? AttestationId.New();
            var formAttestationId = shareIdsFrom?._formRecord.Attestations[0].AttestationId ?? AttestationId.New();
            var sbpVerifyId = shareIdsFrom?._nonBlindedObservation.Assertions[0].AssertionId ?? AssertionId.New();

            _inMemoryDbName = $"treatment-blinding-exec-{Guid.NewGuid():N}";
            _studyPath = new ScopePath(SponsorId, StudyId);
            _sitePath = new ScopePath(SponsorId, StudyId, "US", SiteId);
            _sponsorEntry = new OrganizationDirectoryEntry(SponsorId, 1, "SPONSOR-01", "Cadence Oncology", OrganizationKind.Sponsor);
            _studyEntry = new StudyDirectoryEntry(
                StudyId, 1, "STD-BLIND-01", "Phase 3 Randomized Study", "Phase 3", SponsorId, 1, "SPONSOR-01", "Cadence Oncology", 1);
            _siteEntry = new SiteDirectoryEntry(
                PhysicalSiteId, 1, SponsorId, 1, "SPONSOR-01", "Cadence Oncology", "SITE-01", "Metro Clinical Site", "US", true, 1);
            _studySiteEntry = new StudySiteDirectoryEntry(
                SiteId, 1, StudyId, PhysicalSiteId, 1, "SITE-01", "Metro Clinical Site", "US", SponsorId, 1, "SPONSOR-01", "Cadence Oncology", "Active", "STD-BLIND-01", 1, true);

            // Grants:
            // - Blinded coordinator holds a tenant-wide observation.record grant (exact task scenario).
            // - Pharmacist holds observation.record + enrollment.unblind + active qualification.
            // - Expired pharmacist holds observation.record + enrollment.unblind, but an expired qualification (K-AUTH-5).
            // - agent:mcp holds observation.record + enrollment.unblind + active qualification (so K-AI-6 requester check decides).
            string[] findingDecisionTokens =
            [
                "validation_finding.investigate",
                "validation_finding.classify",
                "validation_finding.accept",
                "validation_finding.explain",
                "validation_finding.reopen",
                "attestation.sign",
            ];

            _grants =
            [
                Grant.Issue(
                    GrantId.New(), Tenant, BlindedPerson, "observation.record", null,
                    new AuthorityScope(ScopeLevel.Tenant, null, null), T0, null, false, null, Issuer, T0).Value!,
                Grant.Issue(
                    GrantId.New(), Tenant, BlindedPerson, "study.export", null,
                    AuthorityScope.Tenant(), T0, null, false, null, Issuer, T0).Value!,
                Grant.Issue(
                    GrantId.New(), Tenant, BlindedPerson, "enrollment.export", null,
                    AuthorityScope.Tenant(), T0, null, false, null, Issuer, T0).Value!,
                .. findingDecisionTokens.Select(token => Grant.Issue(
                    GrantId.New(), Tenant, BlindedPerson, token, null,
                    AuthorityScope.Tenant(), T0, null, false, null, Issuer, T0).Value!),
                .. findingDecisionTokens.Select(token => Grant.Issue(
                    GrantId.New(), Tenant, BlindedStudyDmPerson, token, null,
                    AuthorityScope.Study(StudyId), T0, null, false, null, Issuer, T0).Value!),
                Grant.Issue(
                    GrantId.New(), Tenant, PharmacistPerson, "observation.record", null,
                    AuthorityScope.StudySite(SiteId), T0, null, false, null, Issuer, T0).Value!,
                Grant.Issue(
                    GrantId.New(), Tenant, PharmacistPerson, "study.export", null,
                    AuthorityScope.Study(StudyId), T0, null, false, null, Issuer, T0).Value!,
                Grant.Issue(
                    GrantId.New(), Tenant, PharmacistPerson, "enrollment.export", null,
                    AuthorityScope.StudySite(SiteId), T0, null, false, null, Issuer, T0).Value!,
                Grant.Issue(
                    GrantId.New(), Tenant, PharmacistPerson, "enrollment.unblind", null,
                    AuthorityScope.StudySite(SiteId), T0, null, false, null, Issuer, T0).Value!,
                .. findingDecisionTokens.Select(token => Grant.Issue(
                    GrantId.New(), Tenant, PharmacistPerson, token, null,
                    AuthorityScope.StudySite(SiteId), T0, null, false, null, Issuer, T0).Value!),
                Grant.Issue(
                    GrantId.New(), Tenant, UnqualifiedPharmacistPerson, "observation.record", null,
                    AuthorityScope.StudySite(SiteId), T0, null, false, null, Issuer, T0).Value!,
                Grant.Issue(
                    GrantId.New(), Tenant, UnqualifiedPharmacistPerson, "study.export", null,
                    AuthorityScope.Study(StudyId), T0, null, false, null, Issuer, T0).Value!,
                Grant.Issue(
                    GrantId.New(), Tenant, UnqualifiedPharmacistPerson, "enrollment.export", null,
                    AuthorityScope.StudySite(SiteId), T0, null, false, null, Issuer, T0).Value!,
                Grant.Issue(
                    GrantId.New(), Tenant, UnqualifiedPharmacistPerson, "enrollment.unblind", null,
                    AuthorityScope.StudySite(SiteId), T0, null, false, null, Issuer, T0).Value!,
                .. findingDecisionTokens.Select(token => Grant.Issue(
                    GrantId.New(), Tenant, UnqualifiedPharmacistPerson, token, null,
                    AuthorityScope.StudySite(SiteId), T0, null, false, null, Issuer, T0).Value!),
                Grant.Issue(
                    GrantId.New(), Tenant, McpAgentActor, "observation.record", null,
                    AuthorityScope.Tenant(), T0, null, false, null, Issuer, T0).Value!,
                Grant.Issue(
                    GrantId.New(), Tenant, McpAgentActor, "enrollment.unblind", null,
                    AuthorityScope.Tenant(), T0, null, false, null, Issuer, T0).Value!,
            ];

            var qualificationEvidence = ArtifactVersionId.New();
            _unblindRequirements =
            [
                QualificationRequirement.Define(
                    QualificationRequirementId.New(), Tenant, "enrollment.unblind", UnblindQualificationType, null, AuthorityScope.Tenant(), T0, null, Issuer, T0).Value!,
            ];

            _qualifications =
            [
                Qualification.Record(
                    QualificationId.New(), Tenant, PharmacistPerson, QualificationKind.Training, UnblindQualificationType,
                    null, qualificationEvidence, T0, T0.AddDays(365), Issuer, T0).Value!,
                Qualification.Record(
                    QualificationId.New(), Tenant, McpAgentActor, QualificationKind.Training, UnblindQualificationType,
                    null, qualificationEvidence, T0, T0.AddDays(365), Issuer, T0).Value!,
                Qualification.Record(
                    QualificationId.New(), Tenant, UnqualifiedPharmacistPerson, QualificationKind.Training, UnblindQualificationType,
                    null, qualificationEvidence, T0.AddDays(-30), T0.AddMinutes(-1), Issuer, T0.AddDays(-30)).Value!,
            ];

            _blindedConcept = new ConceptVersionView(
                BlindedConceptRef,
                ConceptOrigin.Sponsor,
                "C-RAND-ARM",
                ConceptKind.BiomedicalConcept,
                "Randomized Treatment Arm",
                "Blinded randomization treatment assignment",
                ConceptStatus.Active,
                BlindedConceptRef.Version,
                ConceptMappingState.Unmapped,
                T0);
            _sbpConcept = new ConceptVersionView(
                SbpConceptRef,
                ConceptOrigin.CdiscBiomedicalConcept,
                "VSTEST-SYSBP",
                ConceptKind.BiomedicalConcept,
                "Systolic Blood Pressure",
                "Sitting systolic blood pressure in mmHg",
                ConceptStatus.Active,
                SbpConceptRef.Version,
                ConceptMappingState.Unmapped,
                T0);

            var items = new CollectionItem[]
            {
                new("trt_arm", BlindedConceptRef, Required: true, ValueKind: ObservationValueKind.Text),
                new("sbp", SbpConceptRef, Required: true, ValueKind: ObservationValueKind.Quantity, Unit: "mmHg"),
            };

            _design = new ProtocolDesign
            {
                Title = "Phase 3 Randomized Study",
                Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
                Arms = [new Arm("arm.active", "Active 100mg", ArmType.Experimental, ["int.active"])],
                Interventions = [new Intervention("int.active", "Active Investigational Drug", BlindedConceptRef)],
                CollectionSpecs = [new CollectionSpec("cs.day1", "Day 1 Baseline Visit", items)],
                PlannedActivities = [new PlannedActivity("act.day1", "Day 1 Baseline", "ep.treatment", [ActivityLocation.Site], ["cs.day1"], new ActivityTiming(1))],
            };

            _protocolSummary = new ProtocolVersionSummary(
                ProtocolVersionId, StudyId, 1, null, ProtocolVersionStatus.Approved, "hash-pv-1", null, AttestationId.New(), null);

            var armValue = new TextValue(treatmentArmValue);
            var sbpValue = new QuantityValue(185m, "mmHg");
            var armRevision = new ObservationRevisionView(1, armValue, "Initial randomization allocation", "irt@system.test", T0, null);
            var sbpRevision = new ObservationRevisionView(1, sbpValue, "Initial vital signs entry", "crc@site.test", T0, null);

            var blindedObsNode = NodeRef.For(BlindedObservationId);
            var sbpObsNode = NodeRef.For(NonBlindedObservationId);

            AssertionView[] blindedAssertions =
            [
                new(blindedVerifyId, AssertionKind.Verified, blindedObsNode, 1, null, "unblinded-cra@sponsor.test", T0.AddMinutes(1), "Verified kit log", null, null),
                new(blindedQueryId, AssertionKind.Queried, blindedObsNode, null, BlindedAssertionCanaryText, "unblinded-dm@sponsor.test", T0.AddMinutes(2), "Kit check", null, null),
                new(blindedAnswerId, AssertionKind.QueryAnswered, NodeRef.For(blindedQueryId), null, BlindedAnswerCanaryText, "pharmacist@site.test", T0.AddMinutes(3), "Kit confirmed", null, null),
            ];

            AssertionView[] sbpAssertions =
            [
                new(sbpVerifyId, AssertionKind.Verified, sbpObsNode, 1, null, "cra@sponsor.test", T0.AddMinutes(1), "Verified vitals", null, null),
            ];

            var blindedLockView = new LockView(
                blindedLockId, LockKind.Locked, blindedObsNode, blindedLockAttestationId, T0.AddMinutes(4), "pharmacist@site.test", "Lock randomization");

            ObservationAuditView[] auditEntries =
            [
                new(101, T0, "irt@system.test", "RecordObservation", "Observation", 1, $"Recorded {treatmentArmValue}"),
                new(102, T0, "crc@site.test", "RecordObservation", "Observation", 1, "Recorded systolic BP 185"),
            ];

            _blindedObservation = new ObservationRecord(
                BlindedObservationId,
                EnrollmentId,
                ParticipantId,
                StudyId,
                BlindedConceptRef,
                armValue,
                T0,
                1,
                [blindedLockView],
                [armRevision],
                blindedAssertions,
                [auditEntries[0]],
                FormId,
                "trt_arm",
                ObservationValueKind.Text);

            _nonBlindedObservation = new ObservationRecord(
                NonBlindedObservationId,
                EnrollmentId,
                ParticipantId,
                StudyId,
                SbpConceptRef,
                sbpValue,
                T0,
                1,
                [],
                [sbpRevision],
                sbpAssertions,
                [auditEntries[1]],
                FormId,
                "sbp",
                ObservationValueKind.Quantity,
                "mmHg");

            _formRecord = new CollectionInstanceRecord(
                FormId,
                EnrollmentId,
                ParticipantId,
                StudyId,
                SiteId,
                ProtocolVersionId,
                "cs.day1",
                "act.day1",
                T0,
                "crc@site.test",
                T0,
                2,
                items,
                [
                    new CollectedValueView("sbp", NonBlindedObservationId, SbpConceptRef, sbpValue, T0, 1, "crc@site.test", T0),
                    new CollectedValueView("trt_arm", BlindedObservationId, BlindedConceptRef, armValue, T0, 1, "irt@system.test", T0),
                ],
                [blindedLockView],
                [.. sbpAssertions, .. blindedAssertions],
                [
                    new FormAttestationView(
                        formAttestationId,
                        AttestationMeaning.Responsibility,
                        "pi@site.test",
                        T0.AddMinutes(4),
                        "signed-form-hash",
                        "Investigator sign-off",
                        false,
                        [
                            new SignedValueView("sbp", NonBlindedObservationId, 1, sbpValue),
                            new SignedValueView("trt_arm", BlindedObservationId, 1, armValue),
                        ]),
                ],
                "signed-form-hash",
                "Day 1 Baseline Visit");

            ExportCollectionSnapshot[] exportCollections =
            [
                new(
                    FormId,
                    EnrollmentId,
                    SiteId,
                    ProtocolVersionId,
                    "cs.day1",
                    "Day 1 Baseline Visit",
                    "act.day1",
                    "Day 1 Baseline",
                    T0,
                    T0,
                    "crc@site.test",
                    "signed-form-hash",
                    new Dictionary<ObservationId, string>
                    {
                        [BlindedObservationId] = "trt_arm",
                        [NonBlindedObservationId] = "sbp",
                    }),
            ];

            ExportObservationSnapshot[] exportObservations =
            [
                new(
                    BlindedObservationId,
                    EnrollmentId,
                    ParticipantId,
                    SiteId,
                    FormId,
                    "cs.day1",
                    "act.day1",
                    "trt_arm",
                    BlindedConceptRef,
                    _blindedConcept.ExternalCode ?? "C-RAND-ARM",
                    _blindedConcept.PreferredLabel,
                    true,
                    T0,
                    armRevision,
                    [armRevision],
                    LockKind.Locked,
                    blindedAssertions),
                new(
                    NonBlindedObservationId,
                    EnrollmentId,
                    ParticipantId,
                    SiteId,
                    FormId,
                    "cs.day1",
                    "act.day1",
                    "sbp",
                    SbpConceptRef,
                    _sbpConcept.ExternalCode ?? "VSTEST-SYSBP",
                    _sbpConcept.PreferredLabel,
                    false,
                    T0,
                    sbpRevision,
                    [sbpRevision],
                    null,
                    sbpAssertions),
            ];

            StudyLockEntry[] studyLocks =
            [
                new(
                    blindedLockId,
                    StudyId,
                    EnrollmentId,
                    SiteId,
                    "SUBJ-001",
                    LockKind.Locked,
                    blindedObsNode,
                    true,
                    blindedLockAttestationId,
                    AttestationMeaning.Approved,
                    T0.AddMinutes(4),
                    "pharmacist@site.test",
                    "Lock randomization",
                    false,
                    null,
                    null,
                    null,
                    null,
                    null),
            ];

            ExportAttestationSnapshot[] exportAttestations =
            [
                new(
                    blindedObsAttestationId,
                    "pharmacist@site.test",
                    AttestationMeaning.Approved,
                    NodeKinds.Observation,
                    BlindedObservationId.Value,
                    "blinded-obs-hash",
                    T0.AddMinutes(4),
                    "oidc-stepup",
                    T0.AddMinutes(4),
                    "Attested allocation"),
            ];

            ExportProtocolSnapshot[] exportProtocols =
            [
                new(ProtocolVersionId, 1, ProtocolVersionStatus.Approved, _design, "{}", "hash-pv-1", null),
            ];

            _studySnapshot = new StudyExportSnapshot(
                StudyId,
                "STD-BLIND-01",
                "Phase 3 Randomized Study",
                "Phase 3",
                T0.AddMinutes(5),
                T0.AddMinutes(5),
                exportProtocols,
                [new ExportSiteSnapshot(SiteId, PhysicalSiteId, "SITE-01", "Metro Clinical Site", "US", "Active")],
                [new ExportEnrollmentSnapshot(EnrollmentId, ParticipantId, SiteId, "SITE-01", "SUBJ-001", EnrollmentStatus.Enrolled, 1)],
                exportCollections,
                exportObservations,
                studyLocks,
                exportAttestations,
                auditEntries);

            _casebookSnapshot = new EnrollmentCasebookSnapshot(
                StudyId,
                "STD-BLIND-01",
                "Phase 3 Randomized Study",
                SiteId,
                "SITE-01",
                "Metro Clinical Site",
                EnrollmentId,
                ParticipantId,
                "SUBJ-001",
                EnrollmentStatus.Enrolled,
                T0.AddMinutes(5),
                T0.AddMinutes(5),
                exportProtocols,
                exportCollections,
                exportObservations,
                [.. sbpAssertions, .. blindedAssertions],
                studyLocks,
                exportAttestations,
                auditEntries,
                new HashSet<long> { 101 });

            var pack = new RulePack(
                RuleAuthority.Study,
                "Day 1 Edit Checks",
                [
                    new Rule(
                        BlindedRuleId,
                        1,
                        RuleAuthority.Study,
                        new SourceCitation("Study Edit Check Spec", "4.1", null),
                        new RuleApplicability(ValidationStage.Collection, null, null, null, "cs.day1"),
                        ValidationSeverity.Warning,
                        new ComparisonExpression(
                            ComparisonOperator.Equal,
                            new VariableExpression("trt_arm"),
                            new LiteralExpression(RuleValue.FromText(treatmentArmValue))),
                        $"Active arm allocation {treatmentArmValue} requires pharmacist dispense check.",
                        "Verify kit dispense log.",
                        EffectivePeriod.Always),
                    new Rule(
                        NonBlindedRuleId,
                        1,
                        RuleAuthority.Study,
                        new SourceCitation("Study Edit Check Spec", "4.2", null),
                        new RuleApplicability(ValidationStage.Collection, null, null, null, "cs.day1"),
                        ValidationSeverity.Warning,
                        new ComparisonExpression(
                            ComparisonOperator.Greater,
                            new VariableExpression("sbp"),
                            new LiteralExpression(RuleValue.FromNumber(180m))),
                        "Systolic blood pressure exceeds 180 mmHg.",
                        "Repeat seated blood pressure measurement.",
                        EffectivePeriod.Always),
                ]);

            _rulePackItem = new LibraryItemView(
                RulePackItemId,
                LibraryItemKind.RulePack,
                "pack.day1",
                1,
                "Day 1 Edit Checks",
                RulePackJson.ToJson(pack),
                "hash-pack-day1",
                GovernanceStatus.Approved,
                [],
                AttestationId.New(),
                null);

            _standardsProfile = new StudyStandardsProfileView(
                StandardsProfileId,
                StudyId,
                1,
                [],
                [],
                [RulePackItemId]);

            _blindedFinding = new ValidationFindingView(
                BlindedFindingId,
                StudyId,
                ValidationStage.Collection,
                "pack.day1",
                RulePackItemId,
                BlindedRuleId,
                1,
                ValidationSeverity.Warning,
                new FindingTarget(FindingTargetKind.DatasetRecord, "cs.day1", FormId.Value.ToString("D"), "trt_arm"),
                $"Active arm allocation {treatmentArmValue} requires pharmacist dispense check.",
                null,
                [new ObservedVariable("trt_arm", treatmentArmValue)],
                null,
                null,
                null,
                FindingStatus.Open,
                RunId,
                RunId,
                null,
                null,
                null);

            _nonBlindedFinding = new ValidationFindingView(
                NonBlindedFindingId,
                StudyId,
                ValidationStage.Collection,
                "pack.day1",
                RulePackItemId,
                NonBlindedRuleId,
                1,
                ValidationSeverity.Warning,
                new FindingTarget(FindingTargetKind.DatasetRecord, "cs.day1", FormId.Value.ToString("D"), "sbp"),
                "Systolic blood pressure exceeds 180 mmHg.",
                null,
                [new ObservedVariable("sbp", "185")],
                null,
                null,
                null,
                FindingStatus.Open,
                RunId,
                RunId,
                null,
                null,
                null);
        }

        public TenantId Tenant { get; }

        public OrganizationId SponsorId { get; }

        public StudyId StudyId { get; }

        public StudySiteId SiteId { get; }

        public SiteId PhysicalSiteId { get; }

        public ProtocolVersionId ProtocolVersionId { get; }

        public EnrollmentId EnrollmentId { get; }

        public ParticipantId ParticipantId { get; }

        public CollectionInstanceId FormId { get; }

        public ObservationId BlindedObservationId { get; }

        public ObservationId NonBlindedObservationId { get; }

        public ConceptRef BlindedConceptRef { get; }

        public ConceptRef SbpConceptRef { get; }

        public LibraryItemId RulePackItemId { get; }

        public StudyStandardsProfileId StandardsProfileId { get; }

        public ValidationFindingId BlindedFindingId { get; }

        public ValidationFindingId NonBlindedFindingId { get; }

        public ValidationRunId RunId { get; }

        public NodeRef StudyNode => NodeRef.For(StudyId);

        public NodeRef EnrollmentNode => NodeRef.For(EnrollmentId);

        public NodeRef BlindedObservationNode => NodeRef.For(BlindedObservationId);

        public NodeRef NonBlindedObservationNode => NodeRef.For(NonBlindedObservationId);

        public NodeRef BlindedFindingNode => NodeRef.For(BlindedFindingId);

        public int SingleGetDesignCalls => _singleGetDesignCalls;

        public int BatchGetDesignsCalls => _batchGetDesignsCalls;

        public int SingleResolveConceptCalls => _singleResolveConceptCalls;

        public int BatchResolveConceptCalls => _batchResolveConceptCalls;

        public int SinglePathOfCalls => _singlePathOfCalls;

        public int BatchPathsOfCalls => _batchPathsOfCalls;

        private int _singleGetDesignCalls;
        private int _batchGetDesignsCalls;
        private int _singleResolveConceptCalls;
        private int _batchResolveConceptCalls;
        private int _singlePathOfCalls;
        private int _batchPathsOfCalls;

        public void ResetBatchTelemetry()
        {
            Interlocked.Exchange(ref _singleGetDesignCalls, 0);
            Interlocked.Exchange(ref _batchGetDesignsCalls, 0);
            Interlocked.Exchange(ref _singleResolveConceptCalls, 0);
            Interlocked.Exchange(ref _batchResolveConceptCalls, 0);
            Interlocked.Exchange(ref _singlePathOfCalls, 0);
            Interlocked.Exchange(ref _batchPathsOfCalls, 0);
        }

        public void PopulateFormBlindingSets()
        {
            _formRecord = _formRecord with
            {
                BlindedItemIds = FrozenSet.Create(StringComparer.Ordinal, "trt_arm"),
                BlindedObservationIds = FrozenSet.Create(BlindedObservationId),
            };
        }

        public void Dispose()
        {
            using var db = Create(Tenant);
            db.Database.EnsureDeleted();
        }

        public Task<TenantId?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(code, TestSecurityContext.TenantCode, StringComparison.Ordinal) ? (TenantId?)Tenant : null);

        public Task<bool> TryRecordFirstUseAsync(string tokenId, DateTimeOffset retainUntil, CancellationToken cancellationToken) =>
            Task.FromResult(_usedSigTokens.TryAdd(tokenId, retainUntil));

        public Task<bool> TryRecordFirstUseBatchAsync(
            IReadOnlyList<(string TokenId, DateTimeOffset RetainUntil)> uses,
            CancellationToken cancellationToken)
        {
            if (uses.Count == 0 || uses.Select(u => u.TokenId).Distinct(StringComparer.Ordinal).Count() != uses.Count)
            {
                return Task.FromResult(false);
            }

            if (uses.Any(u => _usedSigTokens.ContainsKey(u.TokenId)))
            {
                return Task.FromResult(false);
            }

            foreach (var (tokenId, retainUntil) in uses)
            {
                _usedSigTokens[tokenId] = retainUntil;
            }

            return Task.FromResult(true);
        }

        public Task<TenantEntitlementSnapshot> GetEntitlementsAsync(
            TenantId tenantId,
            DateTimeOffset asOf,
            CancellationToken cancellationToken,
            DateTimeOffset? knownAt = null) =>
            Task.FromResult(new TenantEntitlementSnapshot(
                tenantId,
                SubscriptionId.New(),
                CommercialTier.Complete,
                SubscriptionStatus.Active,
                T0.AddDays(-30),
                null,
                IsReadOnly: false,
                CommercialCatalog.FamiliesForTier(CommercialTier.Complete)));

        public Task<IReadOnlyList<StudyDirectoryEntry>> ListStudiesAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudyDirectoryEntry>>(tenant == Tenant ? [_studyEntry] : []);

        public Task<StudyDirectoryEntry?> FindStudyAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenant == Tenant && studyId == StudyId ? _studyEntry : null);

        public Task<StudyDirectoryEntry?> FindStudyByCodeAsync(TenantId tenant, string studyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenant == Tenant && string.Equals(studyCode, "STD-BLIND-01", StringComparison.OrdinalIgnoreCase) ? _studyEntry : null);

        public Task<IReadOnlyList<OrganizationDirectoryEntry>> ListOrganizationsAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrganizationDirectoryEntry>>(tenant == Tenant ? [_sponsorEntry] : []);

        public Task<OrganizationDirectoryEntry?> FindOrganizationAsync(TenantId tenant, OrganizationId organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenant == Tenant && organizationId == SponsorId ? _sponsorEntry : null);

        public Task<OrganizationDirectoryEntry?> FindOrganizationByCodeAsync(TenantId tenant, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenant == Tenant && string.Equals(code, "SPONSOR-01", StringComparison.OrdinalIgnoreCase) ? _sponsorEntry : null);

        public Task<IReadOnlyList<SiteDirectoryEntry>> ListSitesAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SiteDirectoryEntry>>(tenant == Tenant ? [_siteEntry] : []);

        public Task<SiteDirectoryEntry?> FindSiteAsync(TenantId tenant, SiteId siteId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenant == Tenant && siteId == PhysicalSiteId ? _siteEntry : null);

        public Task<SiteDirectoryEntry?> FindSiteByCodeAsync(
            TenantId tenant,
            string siteCode,
            OrganizationId? institutionId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                tenant == Tenant
                && string.Equals(siteCode, "SITE-01", StringComparison.OrdinalIgnoreCase)
                && (institutionId is null || institutionId == SponsorId)
                    ? _siteEntry
                    : null);

        public Task<IReadOnlyList<StudySiteDirectoryEntry>> ListStudySitesAsync(
            TenantId tenant,
            StudyId studyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudySiteDirectoryEntry>>(tenant == Tenant && studyId == StudyId ? [_studySiteEntry] : []);

        public Task<StudySiteDirectoryEntry?> FindStudySiteAsync(
            TenantId tenant,
            StudySiteId studySiteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(tenant == Tenant && studySiteId == SiteId ? _studySiteEntry : null);

        public Task<StudySiteDirectoryEntry?> FindStudySiteBySiteAsync(
            TenantId tenant,
            StudyId studyId,
            SiteId siteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(tenant == Tenant && studyId == StudyId && siteId == PhysicalSiteId ? _studySiteEntry : null);
        public Task<NodeSummary?> ResolveAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default)
        {
            if (tenant != Tenant)
            {
                return Task.FromResult<NodeSummary?>(null);
            }

            var unversioned = new NodeRef(node.Kind, node.Id);
            if (unversioned == StudyNode)
            {
                return Task.FromResult<NodeSummary?>(new NodeSummary(StudyNode.AtVersion(1), _studyEntry.Title, _studyEntry.StudyCode, "Active"));
            }

            if (unversioned == EnrollmentNode)
            {
                return Task.FromResult<NodeSummary?>(new NodeSummary(EnrollmentNode.AtVersion(1), "SUBJ-001", "SUBJ-001", "Enrolled"));
            }

            if (unversioned == NodeRef.For(FormId))
            {
                return Task.FromResult<NodeSummary?>(new NodeSummary(NodeRef.For(FormId, 2), "Day 1 Baseline Visit"));
            }

            if (unversioned == NodeRef.For(RulePackItemId))
            {
                return Task.FromResult<NodeSummary?>(new NodeSummary(NodeRef.For(RulePackItemId, 1), _rulePackItem.Title, _rulePackItem.Key, "Approved"));
            }

            if (unversioned == NodeRef.For(RunId))
            {
                return Task.FromResult<NodeSummary?>(new NodeSummary(NodeRef.For(RunId, 1), "Collection validation run"));
            }

            return Task.FromResult<NodeSummary?>(null);
        }

        public Task<IReadOnlyList<ZoomStep>?> GetZoomPathAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ZoomStep>?>(null);

        public Task<IReadOnlyList<ScopeChild>?> GetChildrenAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScopeChild>?>(null);

        public Task<IReadOnlyList<StudyEnrollmentNode>?> GetStudyEnrollmentsAsync(
            TenantId tenant,
            StudyId study,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudyEnrollmentNode>?>(
                tenant == Tenant && study == StudyId
                    ? [new StudyEnrollmentNode(EnrollmentId, "SUBJ-001", SiteId, "SITE-01")]
                    : null);

        public Task<IReadOnlyList<Grant>> GrantsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Grant>>(tenant == Tenant ? [.. _grants.Where(g => g.Grantee == actor)] : []);

        public Task<Grant?> FindGrantAsync(TenantId tenant, GrantId grant, CancellationToken cancellationToken) =>
            Task.FromResult(tenant == Tenant ? _grants.SingleOrDefault(g => g.Id == grant) : null);

        public Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Role>>([]);

        public Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _singlePathOfCalls);
            return Task.FromResult(ResolveScopePath(tenant, resource));
        }

        public Task<IReadOnlyDictionary<NodeRef, ScopePath>> PathsOfAsync(
            TenantId tenant,
            IReadOnlyCollection<NodeRef> resources,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _batchPathsOfCalls);
            var resolved = new Dictionary<NodeRef, ScopePath>();
            foreach (var resource in resources)
            {
                var key = resource.Unversioned();
                if (!resolved.ContainsKey(key))
                {
                    resolved[key] = ResolveScopePath(tenant, key);
                }
            }

            return Task.FromResult<IReadOnlyDictionary<NodeRef, ScopePath>>(resolved);
        }

        private ScopePath ResolveScopePath(TenantId tenant, NodeRef resource)
        {
            if (tenant != Tenant)
            {
                return ScopePath.TenantWide;
            }

            var unversioned = new NodeRef(resource.Kind, resource.Id);
            if (unversioned.Kind == NodeKinds.Grant)
            {
                var grant = _grants.SingleOrDefault(g => g.Id.Value == unversioned.Id);
                return grant?.Scope.Level switch
                {
                    ScopeLevel.Study => _studyPath,
                    ScopeLevel.StudySite => _sitePath,
                    _ => ScopePath.TenantWide,
                };
            }

            if (unversioned == StudyNode || unversioned == NodeRef.For(ProtocolVersionId))
            {
                return _studyPath;
            }

            if (unversioned == NodeRef.For(SiteId)
                || unversioned == EnrollmentNode
                || unversioned == NodeRef.For(FormId)
                || unversioned == BlindedObservationNode
                || unversioned == NonBlindedObservationNode
                || unversioned == BlindedFindingNode
                || unversioned == NodeRef.For(NonBlindedFindingId))
            {
                return _sitePath;
            }

            return ScopePath.TenantWide;
        }

        public Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScopePath>>(
                tenant == Tenant && new NodeRef(node.Kind, node.Id) == NodeRef.For(ParticipantId) ? [_sitePath] : []);

        public Task<IReadOnlyDictionary<NodeRef, EvidenceNodeClassification>> InspectEvidenceNodesAsync(
            TenantId tenant,
            IReadOnlyCollection<NodeRef> nodes,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<NodeRef, EvidenceNodeClassification>();
            if (tenant != Tenant)
            {
                return Task.FromResult<IReadOnlyDictionary<NodeRef, EvidenceNodeClassification>>(result);
            }

            foreach (var raw in nodes)
            {
                var key = new NodeRef(raw.Kind, raw.Id);
                if (key == BlindedObservationNode)
                {
                    var isBlinded = BlindedTreatmentClassifier.Instance.IsBlindedObservation(
                        BlindedConceptRef.ConceptId,
                        _blindedObservation.Value,
                        new BlindedConceptMetadata(_blindedConcept.ExternalCode, _blindedConcept.PreferredLabel, _blindedConcept.Definition),
                        _blindedObservation.CollectionInstanceId is not null ? "cs.day1" : null,
                        _blindedObservation.ItemId,
                        [_design]);
                    result[key] = new EvidenceNodeClassification(_sitePath, isBlinded);
                }
                else if (key == NonBlindedObservationNode)
                {
                    var isBlinded = BlindedTreatmentClassifier.Instance.IsBlindedObservation(
                        SbpConceptRef.ConceptId,
                        _nonBlindedObservation.Value,
                        new BlindedConceptMetadata(_sbpConcept.ExternalCode, _sbpConcept.PreferredLabel, _sbpConcept.Definition),
                        "cs.day1",
                        _nonBlindedObservation.ItemId,
                        [_design]);
                    result[key] = new EvidenceNodeClassification(_sitePath, isBlinded);
                }
                else if (key == BlindedFindingNode)
                {
                    var isBlinded = BlindedTreatmentClassifier.Instance.IsBlindedFinding(
                        _blindedFinding.Target,
                        _blindedFinding.Observed,
                        [_design],
                        conceptId => conceptId == BlindedConceptRef.ConceptId
                            ? new BlindedConceptMetadata(_blindedConcept.ExternalCode, _blindedConcept.PreferredLabel, _blindedConcept.Definition)
                            : null);
                    result[key] = new EvidenceNodeClassification(_sitePath, isBlinded);
                }
                else if (key == NodeRef.For(NonBlindedFindingId))
                {
                    var isBlinded = BlindedTreatmentClassifier.Instance.IsBlindedFinding(
                        _nonBlindedFinding.Target,
                        _nonBlindedFinding.Observed,
                        [_design],
                        conceptId => conceptId == SbpConceptRef.ConceptId
                            ? new BlindedConceptMetadata(_sbpConcept.ExternalCode, _sbpConcept.PreferredLabel, _sbpConcept.Definition)
                            : null);
                    result[key] = new EvidenceNodeClassification(_sitePath, isBlinded);
                }
            }

            return Task.FromResult<IReadOnlyDictionary<NodeRef, EvidenceNodeClassification>>(result);
        }

        public Task<IReadOnlyList<Qualification>> QualificationsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Qualification>>(tenant == Tenant ? [.. _qualifications.Where(q => q.Holder == actor)] : []);

        public Task<IReadOnlyList<QualificationRequirement>> QualificationRequirementsAsync(
            TenantId tenant, string capability, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QualificationRequirement>>(
                tenant == Tenant ? [.. _unblindRequirements.Where(r => string.Equals(r.Capability, capability, StringComparison.Ordinal))] : []);

        public Task<IReadOnlyList<ProtocolVersionSummary>> ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProtocolVersionSummary>>(tenant == Tenant && study == StudyId ? [_protocolSummary] : []);

        public Task<ProtocolVersionSummary?> GetAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult(tenant == Tenant && version == ProtocolVersionId ? _protocolSummary : null);

        public Task<SoaMatrix?> GetScheduleOfActivitiesAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult<SoaMatrix?>(null);

        public Task<ProtocolDesign?> GetDesignAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _singleGetDesignCalls);
            return Task.FromResult(tenant == Tenant && version == ProtocolVersionId ? _design : null);
        }

        public Task<IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign>> GetDesignsForStudyAsync(
            TenantId tenant,
            StudyId study,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _batchGetDesignsCalls);
            var resolved = new Dictionary<ProtocolVersionId, ProtocolDesign>();
            if (tenant == Tenant && study == StudyId)
            {
                resolved[ProtocolVersionId] = _design;
            }

            return Task.FromResult<IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign>>(resolved);
        }

        public Task<ConceptVersionView?> ResolveAsync(TenantId tenant, ConceptRef concept, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _singleResolveConceptCalls);
            return Task.FromResult(ResolveCurrentConcept(tenant, concept.ConceptId));
        }

        public Task<IReadOnlyDictionary<ConceptRef, ConceptVersionView>> ResolveManyAsync(
            TenantId tenant,
            IReadOnlyCollection<ConceptRef> concepts,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _batchResolveConceptCalls);
            var resolved = new Dictionary<ConceptRef, ConceptVersionView>();
            foreach (var concept in concepts.Distinct())
            {
                if (ResolveCurrentConcept(tenant, concept.ConceptId) is { } view && view.Concept == concept)
                {
                    resolved[concept] = view;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<ConceptRef, ConceptVersionView>>(resolved);
        }

        public Task<ConceptVersionView?> GetCurrentAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _singleResolveConceptCalls);
            return Task.FromResult(ResolveCurrentConcept(tenant, concept));
        }

        public Task<IReadOnlyDictionary<ConceptId, ConceptVersionView>> GetCurrentManyAsync(
            TenantId tenant,
            IReadOnlyCollection<ConceptId> concepts,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _batchResolveConceptCalls);
            var resolved = new Dictionary<ConceptId, ConceptVersionView>();
            foreach (var conceptId in concepts.Distinct())
            {
                if (ResolveCurrentConcept(tenant, conceptId) is { } view)
                {
                    resolved[conceptId] = view;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<ConceptId, ConceptVersionView>>(resolved);
        }

        private ConceptVersionView? ResolveCurrentConcept(TenantId tenant, ConceptId concept)
        {
            if (tenant != Tenant)
            {
                return null;
            }

            if (concept == BlindedConceptRef.ConceptId)
            {
                return _blindedConcept;
            }

            return concept == SbpConceptRef.ConceptId ? _sbpConcept : null;
        }

        public Task<ConceptVersionView?> FindBiomedicalConceptAsync(TenantId tenant, string cdiscConceptCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConceptVersionView?>(null);

        public Task<CodeSystemVersionView?> FindCodeSystemVersionAsync(TenantId tenant, string codeSystemUri, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult<CodeSystemVersionView?>(null);

        public Task<IReadOnlyList<CodeSystemEntryView>> LookupAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CodeSystemEntryView>>([]);

        public Task<IReadOnlyList<CodeSystemEntryView>> GetGroupAsync(TenantId tenant, CodeSystemVersionId version, string groupCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CodeSystemEntryView>>([]);

        public Task<IReadOnlyList<ConceptMappingView>> ResolveCodingAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConceptMappingView>>([]);

        public Task<IReadOnlyList<ConceptMappingView>> GetMappingsAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConceptMappingView>>([]);

        public Task<ObservationRecord?> FindAsync(TenantId tenant, ObservationId observation, CancellationToken cancellationToken)
        {
            if (tenant != Tenant)
            {
                return Task.FromResult<ObservationRecord?>(null);
            }

            if (observation == BlindedObservationId)
            {
                return Task.FromResult<ObservationRecord?>(_blindedObservation);
            }

            return Task.FromResult(observation == NonBlindedObservationId ? _nonBlindedObservation : null);
        }

        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken) =>
            Task.FromResult(tenant == Tenant && instance == FormId ? _formRecord : null);

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(
            TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceSummary>>([]);

        public Task<IReadOnlyList<CollectionInstanceRecord>> ListRecordsForEnrollmentAsync(
            TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>(tenant == Tenant && enrollment == EnrollmentId ? [_formRecord] : []);

        Task<IReadOnlyList<CollectionInstanceRecord>> ICollectionInstanceRecords.ListForStudyAsync(
            TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>(tenant == Tenant && study == StudyId ? [_formRecord] : []);

        public Task<StudyExportSnapshot?> ReadStudySnapshotAsync(
            TenantId tenant, StudyId study, DateTimeOffset validAt, DateTimeOffset knownAt, CancellationToken cancellationToken) =>
            Task.FromResult(tenant == Tenant && study == StudyId ? _studySnapshot : null);

        public Task<EnrollmentCasebookSnapshot?> ReadEnrollmentCasebookAsync(
            TenantId tenant, EnrollmentId enrollment, DateTimeOffset validAt, DateTimeOffset knownAt, CancellationToken cancellationToken) =>
            Task.FromResult(tenant == Tenant && enrollment == EnrollmentId ? _casebookSnapshot : null);

        public Task<IReadOnlyList<StandardVersionSummary>> ListStandardVersionsAsync(TenantId tenant, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StandardVersionSummary>>([]);

        public Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult<StandardVersionSummary?>(null);

        public Task<StandardVersionSummary?> FindStandardVersionAsync(
            TenantId tenant, string standardCode, string version, CancellationToken cancellationToken) =>
            Task.FromResult<StandardVersionSummary?>(null);

        public Task<StandardPackage?> GetStandardPackageAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult<StandardPackage?>(null);

        public Task<LibraryItemView?> FindLibraryItemAsync(TenantId tenant, LibraryItemId id, CancellationToken cancellationToken) =>
            Task.FromResult(tenant == Tenant && id == RulePackItemId ? _rulePackItem : null);

        public Task<IReadOnlyList<LibraryItemView>> ListLibraryItemVersionsAsync(
            TenantId tenant, LibraryItemKind kind, string key, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryItemView>>(
                tenant == Tenant && kind == LibraryItemKind.RulePack && string.Equals(key, _rulePackItem.Key, StringComparison.Ordinal)
                    ? [_rulePackItem]
                    : []);

        public Task<IReadOnlyList<LibraryItemView>> ListLibraryItemsAsync(
            TenantId tenant, LibraryItemKind? kind, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryItemView>>(
                tenant == Tenant && (kind is null || kind == LibraryItemKind.RulePack) ? [_rulePackItem] : []);

        public Task<StudyStandardsProfileView?> FindStudyStandardsAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult(tenant == Tenant && study == StudyId ? _standardsProfile : null);

        public Task<ValidationFindingView?> FindAsync(TenantId tenant, ValidationFindingId finding, CancellationToken cancellationToken)
        {
            if (tenant != Tenant)
            {
                return Task.FromResult<ValidationFindingView?>(null);
            }

            if (finding == BlindedFindingId)
            {
                return Task.FromResult<ValidationFindingView?>(_blindedFinding);
            }

            return Task.FromResult(finding == NonBlindedFindingId ? _nonBlindedFinding : null);
        }

        public Task<FindingPage> ListAsync(TenantId tenant, FindingQuery query, CancellationToken cancellationToken)
        {
            if (tenant != Tenant || query.Study != StudyId)
            {
                return Task.FromResult(new FindingPage([], 0));
            }

            ValidationFindingView[] all = [_blindedFinding, _nonBlindedFinding];
            return Task.FromResult(new FindingPage(all, all.Length));
        }

        public Task<IReadOnlyList<string>> ListDatasetsAsync(
            TenantId tenant, StudyId study, ValidationStage? stage = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(
                tenant == Tenant && study == StudyId && stage is null or ValidationStage.Collection
                    ? ["cs.day1"]
                    : []);

        public Task<ValidationRunSummary?> FindRunAsync(TenantId tenant, ValidationRunId run, CancellationToken cancellationToken) =>
            Task.FromResult(tenant == Tenant && run == RunId
                ? new ValidationRunSummary(
                    RunId,
                    StudyId,
                    ValidationStage.Collection,
                    DateOnly.FromDateTime(T0.UtcDateTime),
                    null,
                    null,
                    null,
                    [new ValidationRunPackView(RulePackItemId, _rulePackItem.Key, _rulePackItem.Version, _rulePackItem.ContentHash)],
                    ["cs.day1"],
                    [NodeRef.For(FormId)],
                    T0,
                    T0.AddSeconds(1),
                    T0.AddSeconds(1),
                    "system",
                    2)
                : null);

        public ExecutionModuleDbContext Create(TenantId tenant)
        {
            var options = new DbContextOptionsBuilder<ExecutionModuleDbContext>()
                .UseInMemoryDatabase($"{_inMemoryDbName}-{tenant.Value.ToString("N", CultureInfo.InvariantCulture)}")
                .ReplaceService<IModelCustomizer, InMemoryExecutionModelCustomizer>()
                .Options;
            return new ExecutionModuleDbContext(options);
        }

        private sealed class InMemoryExecutionModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
        {
            public override void Customize(ModelBuilder modelBuilder, DbContext context)
            {
                base.Customize(modelBuilder, context);
                modelBuilder.Entity<FormQuery>().Ignore(q => q.Periods);
                modelBuilder.Ignore<FormQueryPeriod>();
            }
        }

        Task<CommandResult<TResult>> IKernelCommandSender.SendAsync<TResult>(
            ICommand<TResult> command,
            CommandMetadata? metadata,
            CancellationToken cancellationToken)
        {
            DispatchedCommands.Add(command);
            return command switch
            {
                ExportStudySnapshot exportStudy => Task.FromResult(
                    CommandResult<TResult>.Success((TResult)(object)new ArtifactState(
                        exportStudy.ArtifactId,
                        ArtifactKind.Dataset,
                        exportStudy.StudyId,
                        exportStudy.Title,
                        exportStudy.ArtifactVersionId,
                        1,
                        exportStudy.Content.ContentHash))),
                ExportEnrollmentCasebook exportCasebook => Task.FromResult(
                    CommandResult<TResult>.Success((TResult)(object)new ArtifactState(
                        exportCasebook.ArtifactId,
                        ArtifactKind.Dataset,
                        exportCasebook.StudyId,
                        exportCasebook.Title,
                        exportCasebook.ArtifactVersionId,
                        1,
                        exportCasebook.Content.ContentHash))),
                Attest attest => Task.FromResult(
                    CommandResult<TResult>.Success((TResult)(object)attest.AttestationId)),
                InvestigateFinding inv when inv.FindingId == BlindedFindingId =>
                    UpdateBlindedFinding<TResult>(_blindedFinding with { Status = FindingStatus.Investigating }),
                ClassifyFinding cls when cls.FindingId == BlindedFindingId =>
                    UpdateBlindedFinding<TResult>(_blindedFinding with { Classification = cls.Classification }),
                AcceptFinding acc when acc.FindingId == BlindedFindingId =>
                    UpdateBlindedFinding<TResult>(_blindedFinding with
                    {
                        Status = FindingStatus.Accepted,
                        Decision = new FindingDecision(FindingStatus.Accepted, PharmacistPerson.Id, T0.AddMinutes(5), acc.Rationale, RunId, acc.AttestationId),
                    }),
                ReopenFinding reopen when reopen.FindingId == BlindedFindingId =>
                    UpdateBlindedFinding<TResult>(_blindedFinding with { Status = FindingStatus.Open, Decision = null }),
                ExplainFinding exp when exp.FindingId == BlindedFindingId =>
                    UpdateBlindedFinding<TResult>(_blindedFinding with
                    {
                        Status = FindingStatus.Explained,
                        Decision = new FindingDecision(FindingStatus.Explained, PharmacistPerson.Id, T0.AddMinutes(5), exp.Explanation, RunId, exp.AttestationId),
                    }),
                _ => throw new NotSupportedException($"Unexpected in-memory command {command.GetType().Name}."),
            };
        }

        private Task<CommandResult<TResult>> UpdateBlindedFinding<TResult>(ValidationFindingView updated)
        {
            _blindedFinding = updated;
            return Task.FromResult(CommandResult<TResult>.Success((TResult)(object)BlindedFindingId));
        }
    }
}
