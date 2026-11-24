using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Execution;
using Cadence.Kernel;
using Cadence.Kernel.Activity;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Activity;
using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence.Activity;
using Cadence.Kernel.Persistence.Background;
using Cadence.Kernel.Persistence.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// End-to-end PostgreSQL tests for <c>audit_entry:&lt;id&gt;</c> node navigation, <c>GET /api/v1/kernel/nodes/{nodeRef}/history</c>
/// across all 12 domain entities, <c>GET /api/v1/kernel/audit-entries/{auditRef}</c>, bitemporal <c>valid_at</c>/<c>known_at</c>
/// filters, <c>K-AUTH-4</c> blinded and site-scope masking without <c>enrollment.unblind</c>, and enriched
/// <c>kernel.activity_entries</c> (<c>reason_for_change</c>, <c>audit_entry_ref</c>, <c>attestation_ref</c>) with clean
/// outbox rebuild under <c>KA001</c> insert-only database triggers (ADR-2449, ADR-2471, ADR-2485, ADR-2607).
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelNodeHistoryPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string TreatmentItem = "item.treatment_arm";
    private const string BlindedArmText = "Investigational Arm Zeta";
    private const string BlindedCorrectionReason = "Corrected confidential Investigational Arm Zeta bottle label";
    private const string BlindedQueryPrompt = "Verify confidential Investigational Arm Zeta vial";

    private const string SiteACrc = "crc.site-a@node-history.test";
    private const string BlindedStat = "stat.blinded@node-history.test";
    private const string UnblindedPharm = "pharm.unblinded@node-history.test";
    private const string ProposalApprover = "approver.pi@node-history.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    [Fact(DisplayName = "NodeHistoryAndAuditEntryDetail_SpanAllTwelveDomainEntities_WithDiffsAssertionsAndAttestations")]
    public async Task NodeHistoryAndAuditEntryDetailSpanAllTwelveDomainEntitiesWithDiffsAssertionsAndAttestations()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await SeedWorldAsync(ct);
        await RelayAsync(ct);

        using var unblinded = Client(world.Code, UnblindedPharm);

        // 1. Record Systolic BP (245 mm[Hg] -> triggers VS_SBP_HIGH finding), then correct it (138 mm[Hg]), verify it, and freeze it.
        await SendJsonAsync<FormSavedResponse>(
            unblinded,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/values",
            "Initial vital signs entry",
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 245m, unit = "mm[Hg]" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayAsync(ct);

        var formDoc = await SendJsonAsync<JsonElement>(
            unblinded, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}", null, null, HttpStatusCode.OK, ct);
        var sbpObsId = new ObservationId(formDoc.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("item_id").GetString() == Systolic)
            .GetProperty("observation_id").GetGuid());

        // Explain the open VS_SBP_HIGH validation finding with a signed attestation before correcting the value.
        var findingsPage = await GetKernelAsync<KernelValidationFindingPageResponse>(
            unblinded, $"studies/{NodeRef.For(world.Study)}/validation/findings", ct);
        var findingDto = findingsPage.Findings.Single(f => f.RuleId == "VS_SBP_HIGH");
        var findingId = ValidationFindingId.FromGuid(NodeRef.Parse(findingDto.Ref).Id);
        var findingRunId = ValidationRunId.FromGuid(NodeRef.Parse(findingDto.LastDetectedRunRef).Id);

        const string findingExplanation = "Transient white-coat elevation confirmed prior to repeat measurement.";
        var decisionHash = FindingDecisionContent.Hash(findingId, findingRunId, FindingStatus.Explained, findingExplanation);
        var findingAttest = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(
                world.Tenant,
                AttestationMeaning.Approved,
                new AttestationTarget(NodeKinds.ValidationFinding, findingId.Value, decisionHash)),
            ct);
        await _scenario.Protocol.OkAsync(
            new ExplainFinding(
                world.Tenant,
                findingId,
                findingExplanation,
                findingAttest,
                KernelProtocolScenario.Key(),
                "Explained validation finding for reviewer guide"),
            ct);

        Tick();
        await SendJsonAsync<FormSavedResponse>(
            unblinded,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/values",
            "Corrected cuff transcription after source check",
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 138m, unit = "mm[Hg]" }, expected_revision = 1 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayAsync(ct);

        // Raise and answer a FormQuery on Systolic BP.
        var raisedQuery = await SendJsonAsync<FormQueryResponse>(
            unblinded,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/queries",
            "Verify cuff calibration",
            new { observation_id = sbpObsId.Value, revision = 2, text = "Please confirm resting cuff position." },
            HttpStatusCode.Created,
            ct);
        await RelayAsync(ct);

        await SendJsonAsync<FormQueryResponse>(
            unblinded,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/queries/{raisedQuery.QueryId:D}/answer",
            "Coordinator source confirmation",
            new { text = "Confirmed sitting 5 minutes prior to 138 mm[Hg] reading." },
            HttpStatusCode.OK,
            ct);
        await RelayAsync(ct);

        // Verify observation (SDV assertion) and apply a Frozen lock with inline step-up attestation.
        await _scenario.Protocol.OkAsync(
            new VerifyObservation(
                world.Tenant,
                AssertionId.New(),
                world.EnrollmentA,
                sbpObsId,
                2,
                KernelProtocolScenario.Key(),
                "Source data verified against paper chart"),
            ct);

        await _scenario.Protocol.OkAsync(
            new ApplyLock(
                world.Tenant,
                AssertionId.New(),
                world.Study,
                NodeRef.For(sbpObsId),
                LockKind.Frozen,
                AttestationId.New(),
                KernelProtocolScenario.Key(),
                "Frozen observation for interim analysis",
                AttestationMeaning.Approved,
                Tick(),
                "passkey"),
            ct);

        // Propose and approve a study proposal (approver must differ from proposer).
        var proposalId = ProposalId.New();
        var proposed = await _scenario.Protocol.OkAsync(
            new Propose(
                world.Tenant,
                proposalId,
                world.Study,
                "Extend baseline visit window by one day",
                world.PlanArtifactVersion,
                [NodeRef.For(world.ProtocolVersion, 3)],
                null,
                ["protocol.approve"],
                Tick().AddDays(14),
                null,
                KernelProtocolScenario.Key(),
                "Proposed window extension"),
            ct);

        var previousActor = fixture.CurrentActor.Actor;
        try
        {
            fixture.CurrentActor.Actor = Actor.Person(ProposalApprover);
            var proposalAttest = await _scenario.Protocol.OkAsync(
                _scenario.Protocol.AttestCommand(
                    world.Tenant,
                    AttestationMeaning.Approved,
                    new AttestationTarget(NodeKinds.Proposal, proposalId.Value, proposed.ContentHash)),
                ct);
            await _scenario.Protocol.OkAsync(
                new ApproveProposal(
                    world.Tenant,
                    proposalId,
                    proposalAttest,
                    KernelProtocolScenario.Key(),
                    "Approved proposal after safety review"),
                ct);
        }
        finally
        {
            fixture.CurrentActor.Actor = previousActor;
        }

        // Verify all 12 domain entities return rich, unmasked timelines via GET /api/v1/kernel/nodes/{nodeRef}/history.
        var visitElementRef = NodeRef.For(ProtocolElementId.For(world.Study, VisitId), 1);
        var objectiveElementRef = NodeRef.For(ProtocolElementId.For(world.Study, "obj.primary"), 1);

        var allTwelveDomainRefs = new (string DomainName, NodeRef Ref)[]
        {
            ("study", NodeRef.For(world.Study)),
            ("protocol_version", NodeRef.For(world.ProtocolVersion)),
            ("protocol_element (visit)", visitElementRef),
            ("protocol_element (objective)", objectiveElementRef),
            ("site", NodeRef.For(world.RawSiteA)),
            ("study_site", NodeRef.For(world.SiteA)),
            ("enrollment", NodeRef.For(world.EnrollmentA)),
            ("collection_instance", NodeRef.For(world.FormA)),
            ("observation", NodeRef.For(sbpObsId)),
            ("form_query", NodeRef.For(new FormQueryId(raisedQuery.QueryId))),
            ("validation_finding", NodeRef.For(findingId)),
            ("artifact", NodeRef.For(world.EthicsArtifact)),
            ("proposal", NodeRef.For(proposalId)),
            ("qualification", NodeRef.For(world.Qualification)),
        };

        foreach (var (domainName, nodeRef) in allTwelveDomainRefs)
        {
            var history = await GetKernelAsync<KernelNodeHistoryResponse>(unblinded, $"nodes/{nodeRef}/history", ct);
            history.Mask.Should().BeNull($"caller has full authority over {domainName}");
            history.Node.Should().StartWith($"{nodeRef.Kind}:");
            history.Entries.Should().NotBeEmpty($"{domainName} ({nodeRef}) must return committed timeline entries");
            history.Entries.Should().OnlyContain(e => e.Mask == null, $"all entries for {domainName} should be visible");
        }

        // Inspect specific rich semantics on Observation history (diffs, assertions, attestations) & ProtocolElement lineage history.
        var obsHistory = await GetKernelAsync<KernelNodeHistoryResponse>(unblinded, $"nodes/{NodeRef.For(sbpObsId)}/history", ct);
        obsHistory.Entries.Should().Contain(e =>
            e.ReasonForChange == "Corrected cuff transcription after source check" &&
            e.FieldChanges.Any(c => c.Before != null && c.Before.Contains("245") && c.After != null && c.After.Contains("138")));
        obsHistory.Entries.Should().Contain(e => e.ReasonForChange == "Source data verified against paper chart");
        obsHistory.Entries.Should().Contain(e => e.ReasonForChange == "Frozen observation for interim analysis");
        obsHistory.Entries.Should().Contain(e => e.Attestation != null && e.Attestation.Meaning == "Approved");

        var visitHistory = await GetKernelAsync<KernelNodeHistoryResponse>(unblinded, $"nodes/{visitElementRef}/history", ct);
        visitHistory.Node.Should().StartWith($"{NodeKinds.ProtocolElement}:");
        visitHistory.Entries.Should().Contain(e => e.AuditEntryRef != null && e.AuditEntryRef.StartsWith("audit_entry:", StringComparison.Ordinal));

        // Inspect GET /api/v1/kernel/audit-entries/{auditRef} and GET /api/v1/kernel/nodes/{auditRef} (+ /path).
        var lockEntry = obsHistory.Entries.First(e => e.Attestation != null && e.AuditEntryRef != null);
        var auditRef = lockEntry.AuditEntryRef!;

        var detail = await GetKernelAsync<KernelAuditEntryDetailResponse>(unblinded, $"audit-entries/{auditRef}", ct);
        detail.Mask.Should().BeNull();
        detail.AuditEntryRef.Should().Be(auditRef);
        detail.Sequence.Should().BeGreaterThan(0);
        detail.Hash.Should().NotBeNullOrWhiteSpace();
        detail.ActivityId.Should().NotBeNull();
        detail.Attestation.Should().NotBeNull();
        detail.Attestation!.AttestationRef.Should().StartWith("attestation:");
        detail.TargetKind.Should().NotBeNullOrWhiteSpace();

        var auditNodeSummary = await GetKernelAsync<KernelNodeResponse>(unblinded, $"nodes/{auditRef}", ct);
        auditNodeSummary.Mask.Should().BeNull();
        auditNodeSummary.Kind.Should().Be(NodeKinds.AuditEntry);
        auditNodeSummary.Ref.Should().Be(auditRef);
        auditNodeSummary.Code.Should().StartWith("#");
        auditNodeSummary.Status.Should().Be("Committed");

        var auditPath = await GetKernelAsync<IReadOnlyList<KernelZoomStepResponse>>(unblinded, $"nodes/{auditRef}/path", ct);
        auditPath.Should().HaveCountGreaterThanOrEqualTo(2);
        auditPath[^1].Ref.Should().Be(auditRef);
        var studyPrefix = NodeRef.For(world.Study).ToString();
        auditPath.Should().Contain(n => n.Ref != null && n.Ref.StartsWith(studyPrefix, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "NodeHistory_RespectsValidAtAndKnownAtBitemporalFilters")]
    public async Task NodeHistoryRespectsValidAtAndKnownAtBitemporalFilters()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await SeedWorldAsync(ct);

        var validAtInitial = Tick().AddMinutes(-30);
        var obsId = ObservationId.New();
        await _scenario.Protocol.OkAsync(
            new RecordObservation(
                world.Tenant,
                obsId,
                world.EnrollmentA,
                world.SystolicConcept,
                new QuantityValue(124m, "mm[Hg]"),
                validAtInitial,
                KernelProtocolScenario.Key(),
                "Initial chart entry"),
            ct);
        var knownAfterInitial = fixture.Clock.UtcNow;

        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        await _scenario.Protocol.OkAsync(
            new CorrectObservation(
                world.Tenant,
                obsId,
                world.EnrollmentA,
                new QuantityValue(118m, "mm[Hg]"),
                1,
                KernelProtocolScenario.Key(),
                "Corrected chart transcription"),
            ct);
        var knownAfterCorrection = fixture.Clock.UtcNow;

        using var unblinded = Client(world.Code, UnblindedPharm);
        var obsRef = NodeRef.For(obsId);

        var beforeCorrectionQuery = $"nodes/{obsRef}/history?known_at={Uri.EscapeDataString(knownAfterInitial.ToString("O"))}";
        var historyAtInitial = await GetKernelAsync<KernelNodeHistoryResponse>(unblinded, beforeCorrectionQuery, ct);
        historyAtInitial.Entries.Select(e => e.ReasonForChange).Should().Contain("Initial chart entry")
            .And.NotContain("Corrected chart transcription", "the correction was not yet known at knownAfterInitial");

        var afterCorrectionQuery = $"nodes/{obsRef}/history?known_at={Uri.EscapeDataString(knownAfterCorrection.ToString("O"))}";
        var historyAtCorrection = await GetKernelAsync<KernelNodeHistoryResponse>(unblinded, afterCorrectionQuery, ct);
        historyAtCorrection.Entries.Select(e => e.ReasonForChange).Should().Contain(["Initial chart entry", "Corrected chart transcription"]);

        var beforeValidAtQuery = $"nodes/{obsRef}/history?valid_at={Uri.EscapeDataString(validAtInitial.AddMinutes(-5).ToString("O"))}";
        var historyBeforeValidAt = await GetKernelAsync<KernelNodeHistoryResponse>(unblinded, beforeValidAtQuery, ct);
        historyBeforeValidAt.Entries.Should().BeEmpty("the observation had not yet taken clinical effect before validAtInitial");
    }

    [Fact(DisplayName = "BlindedAndOutOfScopeHistoryAuditAndActivity_ReturnMaskedPlaceholdersWithoutEnrollmentUnblind")]
    [KernelRule("K-AUTH-4")]
    public async Task BlindedAndOutOfScopeHistoryAuditAndActivityReturnMaskedPlaceholdersWithoutEnrollmentUnblind()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await SeedWorldAsync(ct);
        await RelayAsync(ct);

        using var crcSiteA = Client(world.Code, SiteACrc);
        using var blindedStat = Client(world.Code, BlindedStat);
        using var unblindedPharm = Client(world.Code, UnblindedPharm);

        // Save blinded Treatment Arm observation on Form A and then correct it + raise a query on it.
        await SendJsonAsync<FormSavedResponse>(
            crcSiteA,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/values",
            "Initial kit allocation entry",
            new
            {
                values = new object[]
                {
                    new { item_id = TreatmentItem, value = new { type = "text", value = BlindedArmText }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayAsync(ct);

        var pharmForm = await SendJsonAsync<JsonElement>(
            unblindedPharm, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}", null, null, HttpStatusCode.OK, ct);
        var blindedObsId = new ObservationId(pharmForm.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("item_id").GetString() == TreatmentItem)
            .GetProperty("observation_id").GetGuid());
        var blindedObsRef = NodeRef.For(blindedObsId);

        Tick();
        await _scenario.Protocol.OkAsync(
            new CorrectObservation(
                world.Tenant,
                blindedObsId,
                world.EnrollmentA,
                new TextValue($"{BlindedArmText} - Verified Kit 04"),
                1,
                KernelProtocolScenario.Key(),
                BlindedCorrectionReason),
            ct);

        await SendJsonAsync<FormQueryResponse>(
            unblindedPharm,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/queries",
            BlindedCorrectionReason,
            new { observation_id = blindedObsId.Value, revision = 2, text = BlindedQueryPrompt },
            HttpStatusCode.Created,
            ct);
        await RelayAsync(ct);

        // Project activity stream so blinded and unblinded events populate kernel.activity_entries.
        await Projector().ProjectAsync(world.Tenant, ct);

        // Unblinded pharmacist sees full history, audit entry detail, and enriched activity entries.
        var unblindedHistory = await GetKernelAsync<KernelNodeHistoryResponse>(unblindedPharm, $"nodes/{blindedObsRef}/history", ct);
        unblindedHistory.Mask.Should().BeNull();
        unblindedHistory.Entries.Should().NotBeEmpty();
        var blindedAuditRef = unblindedHistory.Entries.First(e => e.ReasonForChange == BlindedCorrectionReason && e.AuditEntryRef != null).AuditEntryRef!;
        blindedAuditRef.Should().StartWith("audit_entry:");

        var unblindedAudit = await GetKernelAsync<KernelAuditEntryDetailResponse>(unblindedPharm, $"audit-entries/{blindedAuditRef}", ct);
        unblindedAudit.Mask.Should().BeNull();
        unblindedAudit.ReasonForChange.Should().Be(BlindedCorrectionReason);

        // Study-scoped statistician WITHOUT enrollment.unblind receives masked placeholders everywhere (K-AUTH-4).
        var blindedHistoryRaw = await GetRawAsync(blindedStat, $"/api/v1/kernel/nodes/{blindedObsRef}/history", HttpStatusCode.OK, ct);
        AssertNoBlindedClinicalLeak(blindedHistoryRaw);
        var blindedHistory = JsonSerializer.Deserialize<KernelNodeHistoryResponse>(blindedHistoryRaw, Json)!;
        blindedHistory.Mask.Should().Be(nameof(MaskReason.Blinded));
        blindedHistory.Entries.Should().OnlyContain(e =>
            e.Mask == nameof(MaskReason.Blinded) &&
            e.AuditEntryRef == null &&
            e.TargetRef == null &&
            e.ReasonForChange == null &&
            e.Summary == null &&
            e.FieldChanges.Count == 0);

        var blindedAuditRaw = await GetRawAsync(blindedStat, $"/api/v1/kernel/audit-entries/{blindedAuditRef}", HttpStatusCode.OK, ct);
        AssertNoBlindedLeak(blindedAuditRaw, blindedObsId.Value.ToString("D"));
        var blindedAudit = JsonSerializer.Deserialize<KernelAuditEntryDetailResponse>(blindedAuditRaw, Json)!;
        blindedAudit.Mask.Should().Be(nameof(MaskReason.Blinded));
        blindedAudit.AuditEntryRef.Should().BeNull();
        blindedAudit.TargetRef.Should().BeNull();
        blindedAudit.Hash.Should().BeNull();
        blindedAudit.Sequence.Should().BeNull();
        blindedAudit.ReasonForChange.Should().BeNull();

        var blindedNodeSummary = await GetKernelAsync<KernelNodeResponse>(blindedStat, $"nodes/{blindedAuditRef}", ct);
        blindedNodeSummary.Mask.Should().Be(nameof(MaskReason.Blinded));
        blindedNodeSummary.Ref.Should().BeNull();

        var blindedActivityRaw = await GetRawAsync(blindedStat, $"/api/v1/kernel/activity?scope={NodeRef.For(world.Study)}", HttpStatusCode.OK, ct);
        AssertNoBlindedLeak(blindedActivityRaw, blindedObsId.Value.ToString("D"));
        var blindedActivity = JsonSerializer.Deserialize<KernelActivityPageResponse>(blindedActivityRaw, Json)!;
        blindedActivity.Entries.Where(e => e.Mask == nameof(MaskReason.Blinded)).Should().NotBeEmpty()
            .And.OnlyContain(e => e.Subject == null && e.ReasonForChange == null && e.AuditEntryRef == null && e.AttestationRef == null);

        // Site-scoped CRC at Site A requesting Site B enrollment history gets NotPermitted masked placeholders.
        var siteBHistoryRaw = await GetRawAsync(crcSiteA, $"/api/v1/kernel/nodes/{NodeRef.For(world.EnrollmentB)}/history", HttpStatusCode.OK, ct);
        siteBHistoryRaw.Should().NotContain("SUBJ-B");
        var siteBHistory = JsonSerializer.Deserialize<KernelNodeHistoryResponse>(siteBHistoryRaw, Json)!;
        siteBHistory.Mask.Should().Be(nameof(MaskReason.NotPermitted));
        siteBHistory.Entries.Should().OnlyContain(e => e.Mask == nameof(MaskReason.NotPermitted) && e.TargetRef == null && e.AuditEntryRef == null);
    }

    [Fact(DisplayName = "ActivityStream_EnrichesReasonAuditAndAttestationRefs_AndRebuildsCleanlyFromOutboxUnderInsertOnlyGuards")]
    [KernelRule("K-EVT-3")]
    public async Task ActivityStreamEnrichesReasonAuditAndAttestationRefsAndRebuildsCleanlyFromOutboxUnderInsertOnlyGuards()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await SeedWorldAsync(ct);

        await Projector().ProjectAsync(world.Tenant, ct);

        using var unblinded = Client(world.Code, UnblindedPharm);
        var initialPage = await GetKernelAsync<KernelActivityPageResponse>(
            unblinded, $"activity?scope={NodeRef.For(world.Study)}&limit=100", ct);

        initialPage.Entries.Should().Contain(e =>
            e.Type == "ProtocolVersionApproved" &&
            e.ReasonForChange == "Approved" &&
            e.AuditEntryRef != null &&
            e.AuditEntryRef.StartsWith("audit_entry:", StringComparison.Ordinal));

        initialPage.Entries.Should().Contain(e =>
            e.Type == "AttestationRecorded" &&
            e.AuditEntryRef != null &&
            e.AttestationRef != null &&
            e.AttestationRef.StartsWith("attestation:", StringComparison.Ordinal));

        // Verify KA001 insert-only guards still reject UPDATE and DELETE on kernel.audit_entries,
        // kernel.provenance_activities, and kernel.activity_entries even for the table owner (ADR-2449).
        await using (var ownerConn = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await ownerConn.OpenAsync(ct);

            Func<Task> updateAudit = async () =>
            {
                await using var cmd = new NpgsqlCommand(
                    $"UPDATE kernel.audit_entries SET reason_for_change = 'tampered' WHERE tenant_id = '{world.Tenant.Value:D}'",
                    ownerConn);
                await cmd.ExecuteNonQueryAsync(ct);
            };
            (await updateAudit.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("KA001");

            Func<Task> deleteProvenance = async () =>
            {
                await using var cmd = new NpgsqlCommand(
                    $"DELETE FROM kernel.provenance_activities WHERE tenant_id = '{world.Tenant.Value:D}'",
                    ownerConn);
                await cmd.ExecuteNonQueryAsync(ct);
            };
            (await deleteProvenance.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("KA001");

            Func<Task> updateActivityProjection = async () =>
            {
                await using var cmd = new NpgsqlCommand(
                    $"UPDATE kernel.activity_entries SET reason_for_change = 'tampered' WHERE tenant_id = '{world.Tenant.Value:D}'",
                    ownerConn);
                await cmd.ExecuteNonQueryAsync(ct);
            };
            (await updateActivityProjection.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("KA001");
        }

        // Simulate the disposable projection rebuild (ADR-2485 §4.2): clear kernel.activity_entries as owner (re-enabling
        // KA001 guards afterwards) without touching outbox_messages, audit_entries, or provenance_activities, then re-project.
        await using (var adminConn = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await adminConn.OpenAsync(ct);
            await using var clearCmd = new NpgsqlCommand(
                $"""
                {ModuleInsertOnlySql.DropGuards("kernel", "activity_entries")}
                DELETE FROM kernel.activity_entries WHERE tenant_id = @tenantId;
                {ModuleInsertOnlySql.CreateGuards("kernel", "activity_entries")}
                """,
                adminConn);
            clearCmd.Parameters.AddWithValue("tenantId", world.Tenant.Value);
            await clearCmd.ExecuteNonQueryAsync(ct);
        }

        var rebuiltCount = await Projector().ProjectAsync(world.Tenant, ct);
        rebuiltCount.Should().BeGreaterThan(0);

        var rebuiltPage = await GetKernelAsync<KernelActivityPageResponse>(
            unblinded, $"activity?scope={NodeRef.For(world.Study)}&limit=100", ct);

        rebuiltPage.Entries.Should().BeEquivalentTo(initialPage.Entries);
    }

    private static void AssertNoBlindedClinicalLeak(string payload)
    {
        foreach (var secret in new[] { BlindedArmText, BlindedCorrectionReason, BlindedQueryPrompt })
        {
            payload.Should().NotContain(secret);
        }
    }

    private static void AssertNoBlindedLeak(string payload, string blindedObsUuid)
    {
        AssertNoBlindedClinicalLeak(payload);
        payload.Should().NotContain(blindedObsUuid);
    }

    private KernelActivityProjector Projector() =>
        new(fixture.TenantContexts, new KernelNavigator(new KernelScopeGraph(fixture.TenantContexts)), new KernelBackgroundOptions());

    private async Task<SeededWorld> SeedWorldAsync(CancellationToken ct)
    {
        fixture.Clock.Set(SystemClock.Instance.UtcNow.AddHours(-2));
        var code = $"T-NH-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Node history test tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        OrganizationId sponsor;
        await using (var db = fixture.CreateContext(tenant))
        {
            sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
        }

        var systolicConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Systolic BP", "Systolic BP.", KernelProtocolScenario.Key(), "Defined"), ct);
        var diastolicConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Diastolic BP", "Diastolic BP.", KernelProtocolScenario.Key(), "Defined"), ct);
        var treatmentConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(
                tenant,
                ConceptId.New(),
                ConceptOrigin.Sponsor,
                null,
                ConceptKind.BiomedicalConcept,
                "Treatment Arm",
                "Randomized treatment arm allocation.",
                KernelProtocolScenario.Key(),
                "Defined"),
            ct);

        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            Objectives = [new Objective("obj.primary", ObjectiveLevel.Primary, "Evaluate systolic blood pressure reduction.")],
            Endpoints = [new Cadence.Kernel.Protocol.Design.Endpoint("ep.primary", "obj.primary", ObjectiveLevel.Primary, "Change in SBP at week 12.")],
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity(
                    VisitId,
                    "Baseline visit",
                    "ep.treatment",
                    [ActivityLocation.Site],
                    [SpecId],
                    new ActivityTiming(1, 0, 2)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    SpecId,
                    "Vital signs",
                    [
                        new CollectionItem(Systolic, systolicConcept, "mm[Hg]", Required: true),
                        new CollectionItem(Diastolic, diastolicConcept, "mm[Hg]", Required: false),
                        new CollectionItem(TreatmentItem, treatmentConcept, ValueKind: ObservationValueKind.Text, Required: false),
                    ]),
            ],
        };

        var versionId = ProtocolVersionId.New();
        var draft = await _scenario.Protocol.CreateDraftAsync(tenant, study, versionId, ct, ProtocolDesignDocument.Write(design));
        draft.IsSuccess.Should().BeTrue(draft.Error?.Message);
        var inReview = await _scenario.Protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, versionId, KernelProtocolScenario.Key(), "Ready"), ct);
        var signature = await _scenario.Protocol.OkAsync(_scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _scenario.Protocol.OkAsync(new ApproveProtocolVersion(tenant, versionId, signature, KernelProtocolScenario.Key(), "Approved"), ct);

        var effectivity = EffectivityId.New();
        await _scenario.Protocol.OkAsync(
            new DeclareEffectivity(tenant, effectivity, versionId, EffectivityScope.Study(), Tick(), KernelProtocolScenario.Key(), "Study-wide"), ct);
        var ethicsBytes = Encoding.UTF8.GetBytes($"ethics-{Guid.NewGuid():N}");
        var ethicsArtifact = ArtifactId.New();
        var ethicsVersion = ArtifactVersionId.New();
        var ethicsContent = new ArtifactContent(ContentHash.OfBytes(ethicsBytes), ethicsBytes.Length, "application/pdf", "blob://evidence/ethics.pdf");
        await _scenario.Protocol.OkAsync(
            new RegisterArtifact(tenant, ethicsArtifact, ethicsVersion, ArtifactKind.EthicsApproval, study, "Ethics approval", ethicsContent, KernelProtocolScenario.Key(), "Filed"), ct);
        await _scenario.Protocol.OkAsync(
            new ChangeEffectivityStatus(tenant, effectivity, EffectivityStatus.Effective, Tick(), [ethicsVersion], KernelProtocolScenario.Key(), "Effective"), ct);

        // Register sites and study sites through the command pipeline so they have full audit & provenance rows.
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var rawSiteA = await _scenario.Protocol.OkAsync(
            new RegisterSite(tenant, SiteId.New(), sponsor, $"SITE-A-{suffix}", "Site Alpha", "US", KernelProtocolScenario.Key(), "Registered Site Alpha"), ct);
        var rawSiteB = await _scenario.Protocol.OkAsync(
            new RegisterSite(tenant, SiteId.New(), sponsor, $"SITE-B-{suffix}", "Site Beta", "US", KernelProtocolScenario.Key(), "Registered Site Beta"), ct);
        var siteA = await _scenario.Protocol.OkAsync(
            new LinkSiteToStudy(tenant, StudySiteId.New(), study, rawSiteA, KernelProtocolScenario.Key(), "Linked Site Alpha"), ct);
        var siteB = await _scenario.Protocol.OkAsync(
            new LinkSiteToStudy(tenant, StudySiteId.New(), study, rawSiteB, KernelProtocolScenario.Key(), "Linked Site Beta"), ct);
        await _scenario.Protocol.OkAsync(new ActivateStudySite(tenant, siteA, KernelProtocolScenario.Key(), "Activated Site Alpha"), ct);
        await _scenario.Protocol.OkAsync(new ActivateStudySite(tenant, siteB, KernelProtocolScenario.Key(), "Activated Site Beta"), ct);

        // Register a Proposal plan artifact and a TrainingRecord artifact + Qualification.
        var planBytes = Encoding.UTF8.GetBytes($"plan-{Guid.NewGuid():N}");
        var planVersion = ArtifactVersionId.New();
        await _scenario.Protocol.OkAsync(
            new RegisterArtifact(
                tenant,
                ArtifactId.New(),
                planVersion,
                ArtifactKind.Other,
                study,
                "Amendment simulation plan",
                new ArtifactContent(ContentHash.OfBytes(planBytes), planBytes.Length, "application/pdf", "blob://plans/window.pdf"),
                KernelProtocolScenario.Key(),
                "Filed proposal plan"),
            ct);

        var trainingBytes = Encoding.UTF8.GetBytes($"gcp-{Guid.NewGuid():N}");
        var trainingVersion = ArtifactVersionId.New();
        await _scenario.Protocol.OkAsync(
            new RegisterArtifact(
                tenant,
                ArtifactId.New(),
                trainingVersion,
                ArtifactKind.TrainingRecord,
                null,
                "GCP training certificate",
                new ArtifactContent(ContentHash.OfBytes(trainingBytes), trainingBytes.Length, "application/pdf", "blob://training/gcp.pdf"),
                KernelProtocolScenario.Key(),
                "Filed GCP certificate"),
            ct);

        var qualificationId = QualificationId.New();
        await _scenario.Protocol.OkAsync(
            new RecordQualification(
                tenant,
                qualificationId,
                Actor.Person(UnblindedPharm),
                QualificationKind.Training,
                "gcp",
                null,
                trainingVersion,
                Tick().AddDays(-30),
                Tick().AddDays(335),
                KernelProtocolScenario.Key(),
                "Recorded GCP qualification"),
            ct);

        var participantA = await _scenario.Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented A"), ct);
        SubjectCode.TryFrom("SUBJ-A", out var subjA);
        var screenedA = await _scenario.Protocol.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participantA, siteA, subjA, Tick(), KernelProtocolScenario.Key(), "Screened A"), ct);

        var participantB = await _scenario.Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented B"), ct);
        SubjectCode.TryFrom("SUBJ-B", out var subjB);
        var screenedB = await _scenario.Protocol.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participantB, siteB, subjB, Tick(), KernelProtocolScenario.Key(), "Screened B"), ct);

        // Pin a RulePack with VS_SBP_HIGH so validation_finding is created on out-of-range SBP.
        var sbpVar = new VariableExpression(Systolic);
        var pack = new RulePack(
            RuleAuthority.Study,
            "Node History Edit Checks",
            [
                new Rule(
                    "VS_SBP_HIGH",
                    1,
                    RuleAuthority.Study,
                    new SourceCitation("Validation Plan", "Section 1", null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, SpecId),
                    ValidationSeverity.Warning,
                    new LogicalExpression(
                        LogicalOperator.And,
                        [
                            new FunctionExpression(RuleFunction.IsNotEmpty, [sbpVar]),
                            new ComparisonExpression(ComparisonOperator.Greater, sbpVar, new LiteralExpression(RuleValue.FromNumber(220m))),
                        ]),
                    "Systolic blood pressure exceeds 220 mm[Hg].",
                    "Confirm cuff measurement.",
                    EffectivePeriod.Always),
            ]);
        var rulePackId = LibraryItemId.New();
        var draftedPack = await _scenario.Protocol.OkAsync(
            new DraftLibraryItem(tenant, rulePackId, LibraryItemKind.RulePack, "node-history-checks", pack.Title, RulePackJson.ToJson(pack), [], KernelProtocolScenario.Key(), "Drafted rule pack"),
            ct);
        var packAttest = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.LibraryItem, rulePackId.Value, draftedPack.ContentHash)),
            ct);
        await _scenario.Protocol.OkAsync(new ApproveLibraryItem(tenant, rulePackId, packAttest, KernelProtocolScenario.Key(), "Approved rule pack"), ct);

        var standardId = StandardVersionId.New();
        var standardPkg = new StandardPackage(
            "CDASHIG",
            "2.3",
            "CDISC CDASHIG v2.3",
            "CDISC",
            "/mdr/cdashig/2-3",
            new DateOnly(2023, 9, 29),
            [
                new DatasetDefinitionData(
                    "VS",
                    "Vital Signs",
                    "Findings",
                    "One record per measurement",
                    1,
                    [
                        new VariableDefinitionData("VSORRES", "Result in Original Units", 1, StandardDataType.Num, "Result Qualifier", VariableCore.Expected, [], [], null, null),
                    ]),
            ],
            [],
            []);
        await _scenario.Protocol.OkAsync(new RegisterStandardVersion(tenant, standardId, standardPkg, null, KernelProtocolScenario.Key(), "Registered CDASHIG"), ct);
        var stdSummary = (await fixture.Standards.FindStandardVersionAsync(tenant, standardId, ct))!;
        var stdAttest = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.StandardVersion, standardId.Value, stdSummary.ContentHash)),
            ct);
        await _scenario.Protocol.OkAsync(new ApproveStandardVersion(tenant, standardId, stdAttest, KernelProtocolScenario.Key(), "Approved CDASHIG"), ct);
        await _scenario.Protocol.OkAsync(
            new DeclareStudyStandards(tenant, study, StudyStandardsProfileId.New(), [standardId], [], [rulePackId], KernelProtocolScenario.Key(), "Pinned rule pack"),
            ct);

        var openedA = await _scenario.Protocol.OkAsync(
            Open(new KernelObservationScenario.Subject(tenant, study, siteA, screenedA.EnrollmentId), CollectionInstanceId.New(), Tick()), ct);

        foreach (var (actor, capabilities, scope) in new (Actor, string[], AuthorityScope)[]
        {
            (Actor.Person(SiteACrc), ["collection.open", "collection.record", "execution.query.answer"], AuthorityScope.StudySite(siteA)),
            (Actor.Person(BlindedStat), ["observation.verify"], AuthorityScope.Study(study)),
            (Actor.Person(UnblindedPharm), ["observation.verify", "observation.record", "collection.open", "collection.record", "collection.sign", "lock.apply", "attestation.sign", "execution.query.raise", "execution.query.answer", "execution.query.manage", "enrollment.unblind", "qualification.record"], AuthorityScope.Tenant()),
            (Actor.Person(ProposalApprover), ["proposal.approve", "attestation.sign"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount("execution-forms"), ["execution.form.open"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount("execution-queries"), ["observation.query", "observation.query.answer", "assertion.retract"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount(EvaluateEditChecksOnCollectionChanged.Name), ["validation_run.record", "collection.record_unasked", "execution.query.raise", "execution.query.manage"], AuthorityScope.Tenant()),
        })
        {
            foreach (var cap in capabilities)
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), actor, cap, null, scope, Tick(), null, false, KernelProtocolScenario.Key(), "Test grant"),
                    ct);
            }
        }

        return new SeededWorld(
            code,
            tenant,
            study,
            versionId,
            ethicsArtifact,
            planVersion,
            qualificationId,
            systolicConcept,
            rawSiteA,
            siteA,
            siteB,
            screenedA.EnrollmentId,
            screenedB.EnrollmentId,
            openedA.CollectionInstanceId);
    }

    private async Task RelayAsync(CancellationToken ct)
    {
        fixture.Clock.Set(SystemClock.Instance.UtcNow);
        for (var pass = 0; pass < 4; pass++)
        {
            var relayed = await fixture.OutboxRelay.RelayBatchAsync(500, ct);
            relayed.Failed.Should().Be(0);
            if (relayed.Dispatched == 0 && !relayed.MoreDue)
            {
                break;
            }
        }
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddDeclaredKernelErrorsCheck();
            }));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static async Task<T> GetKernelAsync<T>(HttpClient client, string relativeUnderKernel, CancellationToken ct)
    {
        var raw = await GetRawAsync(client, $"/api/v1/kernel/{relativeUnderKernel}", HttpStatusCode.OK, ct);
        return JsonSerializer.Deserialize<T>(raw, Json)!;
    }

    private static async Task<string> GetRawAsync(HttpClient client, string path, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, body);
        return body;
    }

    private static async Task<T> SendJsonAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        string? reason,
        object? payload,
        HttpStatusCode expected,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (method != HttpMethod.Get)
        {
            request.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
        }

        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, body);
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    private DateTimeOffset Tick()
    {
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        return fixture.Clock.UtcNow;
    }

    private sealed record SeededWorld(
        string Code,
        TenantId Tenant,
        StudyId Study,
        ProtocolVersionId ProtocolVersion,
        ArtifactId EthicsArtifact,
        ArtifactVersionId PlanArtifactVersion,
        QualificationId Qualification,
        ConceptRef SystolicConcept,
        SiteId RawSiteA,
        StudySiteId SiteA,
        StudySiteId SiteB,
        EnrollmentId EnrollmentA,
        EnrollmentId EnrollmentB,
        CollectionInstanceId FormA);
}
