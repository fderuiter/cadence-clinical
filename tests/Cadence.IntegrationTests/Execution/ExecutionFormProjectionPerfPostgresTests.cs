using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence.Background;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// PostgreSQL 18 integration and performance regression tests verifying narrow projections on
/// <see cref="ICollectionInstanceRecords"/>, summary query assertion ID merging across 30 forms, pre-filtered bitemporal
/// query paging and bounded memory across 300 study forms, deduplicated mutation round-trips, RLS, blinding, and lineage.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionFormProjectionPerfPostgresTests(
    ExecutionFormModulePostgresFixture fixture,
    ITestOutputHelper output)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string Crc = "crc@form-perf.test";
    private const string Monitor = "cra@form-perf.test";
    private const string UnblindedPharmacist = "pharmacist@form-perf.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private DelegatingRecordsSpy? _recordsSpy;
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    [Fact(DisplayName = "NarrowProjections_MatchFullRecords_PreserveBlindingAndLocks_AndCutSqlInPostgres")]
    public async Task NarrowProjectionsMatchFullRecordsPreserveBlindingAndLocksAndCutSqlInPostgres()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.Protocol.NewStudyAsync(ct);

        var sbpConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Systolic BP", "Systolic BP.", KernelProtocolScenario.Key(), "Concept"),
            ct);
        var armConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Randomization Arm", "Blinded treatment arm.", KernelProtocolScenario.Key(), "Concept"),
            ct);

        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity(VisitId, "Week 12 visit", "ep.treatment", [ActivityLocation.Site], [SpecId, "cs.dispense"], new ActivityTiming(84, 3, 3)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(SpecId, "Vital signs", [new CollectionItem(Systolic, sbpConcept, "mm[Hg]", Required: true)]),
                new CollectionSpec("cs.dispense", "Randomization Arm Dispense", [new CollectionItem("item.arm", armConcept, ValueKind: ObservationValueKind.Text, Required: true)]),
            ],
        };

        var version = ProtocolVersionId.New();
        (await _scenario.Protocol.CreateDraftAsync(tenant, study, version, ct, ProtocolDesignDocument.Write(design))).IsSuccess.Should().BeTrue();
        var inReview = await _scenario.Protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, version, KernelProtocolScenario.Key(), "Ready"), ct);
        var sig = await _scenario.Protocol.OkAsync(_scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _scenario.Protocol.OkAsync(new ApproveProtocolVersion(tenant, version, sig, KernelProtocolScenario.Key(), "Approved"), ct);
        var eff = EffectivityId.New();
        await _scenario.Protocol.OkAsync(new DeclareEffectivity(tenant, eff, version, EffectivityScope.Study(), _scenario.Tick(), KernelProtocolScenario.Key(), "Study-wide"), ct);
        var ethicsBytes = Encoding.UTF8.GetBytes($"ethics-{Guid.CreateVersion7():N}");
        var ethics = ArtifactVersionId.New();
        await _scenario.Protocol.OkAsync(
            new RegisterArtifact(
                tenant,
                ArtifactId.New(),
                ethics,
                ArtifactKind.EthicsApproval,
                study,
                "Ethics approval letter",
                new ArtifactContent(ContentHash.OfBytes(ethicsBytes), ethicsBytes.Length, "application/pdf", "blob://evidence/ethics"),
                KernelProtocolScenario.Key(),
                "Filed approval"),
            ct);
        await _scenario.Protocol.OkAsync(new ChangeEffectivityStatus(tenant, eff, EffectivityStatus.Effective, _scenario.Tick(), [ethics], KernelProtocolScenario.Key(), "Effective"), ct);

        var site = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var subject = new KernelObservationScenario.Subject(tenant, study, site, await _scenario.Observations.ScreenAsync(tenant, site, ct));
        var vitalsId = (await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick(), SpecId), ct)).CollectionInstanceId;
        var dispenseId = (await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick(), "cs.dispense"), ct)).CollectionInstanceId;

        var sbpObs = (await _scenario.Protocol.OkAsync(
            Save(new Form(subject, version, vitalsId), Value(Systolic, Mmhg(126))), ct)).Values.Single().ObservationId;
        var armObs = (await _scenario.Protocol.OkAsync(
            Save(new Form(subject, version, dispenseId), new CollectedValue("item.arm", new TextValue("Treatment Arm A"), 0)), ct)).Values.Single().ObservationId;

        // Place and keep a Frozen lock on enrollment to verify ScopedAssertions.ReadLocksAsync.
        var frozen = await _scenario.Protocol.OkAsync(
            await _scenario.Observations.LockAsync(tenant, study, NodeRef.For(subject.Enrollment), ct, LockKind.Frozen), ct);

        var records = fixture.CollectionInstanceRecords;

        using var sql = EfSqlCounter.Start();

        sql.Reset();
        var header = await records.FindHeaderAsync(tenant, vitalsId, ct);
        var headerQueries = sql.Count;
        headerQueries.Should().Be(1, "FindHeaderAsync executes a single scalar SELECT");

        sql.Reset();
        var scopeState = await records.FindScopeStateAsync(tenant, vitalsId, ct);
        var scopeQueries = sql.Count;
        scopeQueries.Should().BeLessThanOrEqualTo(3, "FindScopeStateAsync reads header, member values, and covering lock assertions only");

        sql.Reset();
        var full = (await records.FindAsync(tenant, vitalsId, ct))!;
        var fullQueries = sql.Count;
        output.WriteLine($"SQL counts — FindHeaderAsync: {headerQueries}, FindScopeStateAsync: {scopeQueries}, FindAsync: {fullQueries}");
        headerQueries.Should().BeLessThan(fullQueries);
        scopeQueries.Should().BeLessThan(fullQueries);

        header.Should().BeEquivalentTo(CollectionInstanceHeader.From(full));
        scopeState!.Locks.Should().ContainSingle(l => l.LockId == frozen.LockId && l.Kind == LockKind.Frozen);
        scopeState.Values.Should().ContainSingle(v => v.ObservationId == sbpObs && v.Revision == 1);

        var blindedTargets = await records.FindQueryTargetsAsync(
            tenant,
            [vitalsId, dispenseId],
            new HashSet<EnrollmentId>(),
            ct);
        blindedTargets[vitalsId].IsQueryBlinded(Systolic, sbpObs).Should().BeFalse();
        blindedTargets[vitalsId].ObservationRevisions.Should().ContainKey(sbpObs);
        blindedTargets[dispenseId].IsQueryBlinded("item.arm", armObs).Should().BeTrue();
        blindedTargets[dispenseId].ObservationRevisions.Should().NotContainKey(armObs);

        var unblindedTargets = await records.FindQueryTargetsAsync(
            tenant,
            [vitalsId, dispenseId],
            new HashSet<EnrollmentId> { subject.Enrollment },
            ct);
        unblindedTargets[dispenseId].IsQueryBlinded("item.arm", armObs).Should().BeFalse();
        unblindedTargets[dispenseId].ObservationRevisions.Should().ContainKey(armObs);

        var ruleInputs = await records.ListRuleInputsForEnrollmentAsync(tenant, subject.Enrollment, ct);
        ruleInputs.Should().HaveCount(2);
        ruleInputs.Single(r => r.CollectionInstanceId == vitalsId).Values.Should().ContainSingle(v => v.ObservationId == sbpObs);

        await _scenario.Protocol.OkAsync(
            await _scenario.Observations.ReleaseAsync(tenant, study, frozen.LockId, ct), ct);
        (await records.FindScopeStateAsync(tenant, vitalsId, ct))!.Locks.Should().BeEmpty();
    }

    [Fact(DisplayName = "ListForEnrollment_Across30Forms_MergesQueriesAndCutsSqlBelow28")]
    public async Task ListForEnrollmentAcross30FormsMergesQueriesAndCutsSqlBelow28()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, tenant, study, site, enrollment, version) = await SeedStudyAndActorsAsync(ct);
        var subject = new KernelObservationScenario.Subject(tenant, study, site, enrollment);

        var formIds = new List<CollectionInstanceId>(30);
        var firstObs = ObservationId.Empty;
        var secondObs = ObservationId.Empty;
        for (var i = 0; i < 30; i++)
        {
            var opened = await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct);
            formIds.Add(opened.CollectionInstanceId);
            var saved = await _scenario.Protocol.OkAsync(
                Save(
                    new Form(subject, version, opened.CollectionInstanceId),
                    Value(Systolic, Mmhg(115 + (i % 20))),
                    Value(Diastolic, Mmhg(75 + (i % 10)))),
                ct);
            if (i == 0)
            {
                firstObs = saved.Values.First(v => v.ItemId == Systolic).ObservationId;
            }
            else if (i == 1)
            {
                secondObs = saved.Values.First(v => v.ItemId == Systolic).ObservationId;
            }
        }

        // Add a standalone kernel query on form 0 (not owned by Execution) + an Execution query on form 0 + an answered Execution query on form 1.
        await _scenario.Protocol.OkAsync(
            new RaiseQuery(tenant, AssertionId.New(), enrollment, firstObs, "Standalone kernel query", KernelProtocolScenario.Key(), "Kernel check", Revision: 1),
            ct);
        await RelayOutboxAsync(tenant, ct);

        using var crc = Client(code, Crc);
        using var monitor = Client(code, Monitor);

        await SendAsync<FormQueryResponse>(
            monitor, HttpMethod.Post, $"/{formIds[0].Value:D}/queries", "Monitor SDV",
            new { observation_id = firstObs.Value, revision = 1, text = "Confirm systolic value" },
            HttpStatusCode.Created, ct);

        var form1Query = await SendAsync<FormQueryResponse>(
            monitor, HttpMethod.Post, $"/{formIds[1].Value:D}/queries", "Monitor SDV",
            new { observation_id = secondObs.Value, revision = 1, text = "Confirm second form systolic" },
            HttpStatusCode.Created, ct);
        await SendAsync<FormQueryResponse>(
            crc, HttpMethod.Post, $"/{formIds[1].Value:D}/queries/{form1Query.QueryId:D}/answer", "Site answer",
            new { text = "Verified on chart" },
            HttpStatusCode.OK, ct);
        await RelayOutboxAsync(tenant, ct);

        // Warm up host pipeline once, then measure exact SQL count for 30-form ListForEnrollmentAsync.
        _ = await SendAsync<FormSummaryResponse[]>(monitor, HttpMethod.Get, $"?enrollment_id={enrollment.Value:D}", null, null, HttpStatusCode.OK, ct);

        using var sql = EfSqlCounter.Start();
        var summaries = await SendAsync<FormSummaryResponse[]>(
            monitor, HttpMethod.Get, $"?enrollment_id={enrollment.Value:D}", null, null, HttpStatusCode.OK, ct);
        var listSqlCount = sql.Count;
        output.WriteLine($"30-form ListForEnrollmentAsync SQL count: {listSqlCount} (SELECTs: {sql.SelectCount})");

        summaries.Should().HaveCount(30);
        var summary0 = summaries.Single(s => s.FormId == formIds[0].Value);
        summary0.OpenQueryCount.Should().Be(2, "1 standalone kernel query + 1 execution open query");
        summary0.AnsweredQueryCount.Should().Be(0);
        summary0.Complete.Should().BeFalse();

        var summary1 = summaries.Single(s => s.FormId == formIds[1].Value);
        summary1.OpenQueryCount.Should().Be(0);
        summary1.AnsweredQueryCount.Should().Be(1, "answered execution query deduplicates against underlying kernel Queried + QueryAnswered assertions");
        summary1.Complete.Should().BeFalse();

        summaries.Count(s => s.Complete).Should().Be(28);
        listSqlCount.Should().BeLessThanOrEqualTo(18, "ListForEnrollmentAsync across 30 forms must stay well below the 28-query baseline");
        _recordsSpy!.ListRecordsForEnrollmentCalls.Should().Be(0, "ListRecordsForEnrollmentAsync was eliminated from FormEndpoints.ListForEnrollmentAsync");
        _recordsSpy.FindCalls.Should().Be(0);
    }

    [Fact(DisplayName = "Mutations_SaveRaiseAnswerClose_CutSqlRoundTripsAndPreserveLineageAndContentHash")]
    public async Task MutationsSaveRaiseAnswerCloseCutSqlRoundTripsAndPreserveLineageAndContentHash()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, tenant, study, site, enrollment, _) = await SeedStudyAndActorsAsync(ct);
        var subject = new KernelObservationScenario.Subject(tenant, study, site, enrollment);
        var formId = (await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct)).CollectionInstanceId;
        await RelayOutboxAsync(tenant, ct);

        using var crc = Client(code, Crc);
        using var monitor = Client(code, Monitor);

        // Warm up host pipeline.
        _ = await SendAsync<FormResponse>(crc, HttpMethod.Get, $"/{formId.Value:D}", null, null, HttpStatusCode.OK, ct);

        using var sql = EfSqlCounter.Start();

        // 1. SaveAsync (replaces pre-command 12-query FindAsync with 1-query FindHeaderAsync)
        sql.Reset();
        _recordsSpy!.Reset();
        var saved = await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"/{formId.Value:D}/values",
            SaveReason,
            new
            {
                values = new[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 122m, unit = "mm[Hg]" }, expected_revision = 0 },
                    new { item_id = Diastolic, value = new { type = "quantity", value = 78m, unit = "mm[Hg]" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        var saveSqlCount = sql.Count;
        var saveSelectCount = sql.SelectCount;
        _recordsSpy.FindHeaderCalls.Should().Be(1, "SaveAsync checks form enrollment via 1-query FindHeaderAsync before RecordCollectedValues");
        _recordsSpy.FindCalls.Should().Be(1, "SaveAsync hydrates full CollectionInstanceRecord only once after saving");
        saved.Form!.ContentHash.Should().NotBeNullOrWhiteSpace();
        var sbpObs = saved.Values.Single(v => v.ItemId == Systolic).ObservationId;

        // 2. RaiseAsync (eliminates all full FindAsync hydrations — was 3x FindAsync / 60 SQL commands)
        sql.Reset();
        _recordsSpy.Reset();
        var raised = await SendAsync<FormQueryResponse>(
            monitor,
            HttpMethod.Post,
            $"/{formId.Value:D}/queries",
            "Source data verification",
            new { observation_id = sbpObs, revision = 1, text = "Verify systolic BP" },
            HttpStatusCode.Created,
            ct);
        var raiseSqlCount = sql.Count;
        var raiseSelectCount = sql.SelectCount;
        _recordsSpy.FindCalls.Should().Be(0, "RaiseAsync uses FindHeaderAsync + FindScopeStateAsync + FindQueryTargetsAsync with zero FindAsync calls");
        _recordsSpy.FindHeaderCalls.Should().Be(1);
        _recordsSpy.FindScopeStateCalls.Should().Be(1);
        raiseSelectCount.Should().BeLessThanOrEqualTo(34, "RaiseAsync eliminates triple 12-query FindAsync hydrations (down from 60)");
        raiseSqlCount.Should().BeLessThanOrEqualTo(42);

        // 3. Answer & Close StepAsync (eliminates all full FindAsync hydrations — was 3x FindAsync / 46 SQL commands)
        sql.Reset();
        _recordsSpy.Reset();
        var answered = await SendAsync<FormQueryResponse>(
            crc,
            HttpMethod.Post,
            $"/{formId.Value:D}/queries/{raised.QueryId:D}/answer",
            "Chart check",
            new { text = "Verified 122 mmHg on source" },
            HttpStatusCode.OK,
            ct);
        var answerSqlCount = sql.Count;
        var answerSelectCount = sql.SelectCount;
        answered.Status.Should().Be("answered");
        _recordsSpy.FindCalls.Should().Be(0, "StepAsync uses scalar EnrollmentId lookup + FindHeaderAsync + FindScopeStateAsync + FindQueryTargetsAsync with zero FindAsync calls");
        _recordsSpy.FindScopeStateCalls.Should().Be(1);
        answerSelectCount.Should().BeLessThanOrEqualTo(34, "StepAsync eliminates triple 12-query FindAsync hydrations (down from 46)");
        answerSqlCount.Should().BeLessThanOrEqualTo(40);

        sql.Reset();
        _recordsSpy.Reset();
        var closed = await SendAsync<FormQueryResponse>(
            monitor,
            HttpMethod.Post,
            $"/{formId.Value:D}/queries/{raised.QueryId:D}/close",
            "Closed after verification",
            null,
            HttpStatusCode.OK,
            ct);
        var closeSqlCount = sql.Count;
        var closeSelectCount = sql.SelectCount;
        output.WriteLine(
            $"Mutation SQL counts — SaveAsync: {saveSqlCount} (SELECTs: {saveSelectCount}), RaiseAsync: {raiseSqlCount} (SELECTs: {raiseSelectCount}), Answer: {answerSqlCount} (SELECTs: {answerSelectCount}), Close: {closeSqlCount} (SELECTs: {closeSelectCount})");
        closed.Status.Should().Be("closed");
        _recordsSpy.FindCalls.Should().Be(0);
        _recordsSpy.FindScopeStateCalls.Should().Be(1);
        closeSelectCount.Should().BeLessThanOrEqualTo(34);
        closeSqlCount.Should().BeLessThanOrEqualTo(40);

        await RelayOutboxAsync(tenant, ct);

        // Verify Part 11 ContentHash stability and K-LIN-1 zero lineage orphans.
        var reloadedForm = await SendAsync<FormResponse>(monitor, HttpMethod.Get, $"/{formId.Value:D}", null, null, HttpStatusCode.OK, ct);
        reloadedForm.Complete.Should().BeTrue();
        reloadedForm.ContentHash.Should().Be(saved.Form.ContentHash);

        var orphans = await fixture.LineageOrphanCheck.FindOrphansAsync(tenant, 100, ct);
        orphans.Should().BeEmpty("all K-LIN-1 lineage links must remain intact");
    }

    [Fact(DisplayName = "ListQueries_Across300StudyForms_PreFiltersBitemporalPagesAndCutsMemoryWellBelow89Mb")]
    public async Task ListQueriesAcross300StudyFormsPreFiltersBitemporalPagesAndCutsMemoryWellBelow89Mb()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, tenant, study, site, enrollment, version) = await SeedStudyAndActorsAsync(ct);
        var subject = new KernelObservationScenario.Subject(tenant, study, site, enrollment);

        // Seed 300 forms with recorded values, plus 60 queries (so the study has 300 forms and multiple pages of queries).
        var formsAndObs = new List<(CollectionInstanceId FormId, ObservationId ObsId)>(300);
        for (var i = 0; i < 300; i++)
        {
            var opened = await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct);
            var saved = await _scenario.Protocol.OkAsync(
                Save(
                    new Form(subject, version, opened.CollectionInstanceId),
                    Value(Systolic, Mmhg(110 + (i % 40))),
                    Value(Diastolic, Mmhg(70 + (i % 20)))),
                ct);
            formsAndObs.Add((opened.CollectionInstanceId, saved.Values.First(v => v.ItemId == Systolic).ObservationId));
        }

        await RelayOutboxAsync(tenant, ct);

        using var crc = Client(code, Crc);
        using var monitor = Client(code, Monitor);

        var queryIds = new List<Guid>(60);
        for (var i = 0; i < 60; i++)
        {
            var (formId, obsId) = formsAndObs[i * 5];
            var q = await SendAsync<FormQueryResponse>(
                monitor,
                HttpMethod.Post,
                $"/{formId.Value:D}/queries",
                "Batch SDV",
                new { observation_id = obsId.Value, revision = 1, text = $"Confirm vitals #{i}" },
                HttpStatusCode.Created,
                ct);
            queryIds.Add(q.QueryId);
        }

        // Correct the first 10 queried observations to revision 2 so value_changed = true for those 10 at asOfSnapshot!
        for (var i = 0; i < 10; i++)
        {
            var (formId, _) = formsAndObs[i * 5];
            await SendAsync<FormSavedResponse>(
                crc,
                HttpMethod.Put,
                $"/{formId.Value:D}/values",
                "Corrected from chart",
                new
                {
                    values = new[]
                    {
                        new { item_id = Systolic, value = new { type = "quantity", value = 128m, unit = "mm[Hg]" }, expected_revision = 1 },
                    },
                },
                HttpStatusCode.OK,
                ct);
        }

        var asOfSnapshot = fixture.Clock.UtcNow;
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));

        // Answer 5 of those 10 queries AFTER asOfSnapshot so bitemporal as_of=asOfSnapshot still sees all 10 as "open" with value_changed=true,
        // while current status=open&value_changed=true sees only 5!
        for (var i = 0; i < 5; i++)
        {
            var (formId, _) = formsAndObs[i * 5];
            await SendAsync<FormQueryResponse>(
                crc,
                HttpMethod.Post,
                $"/{formId.Value:D}/queries/{queryIds[i]:D}/answer",
                "Post-snapshot answer",
                new { text = "Updated after snapshot" },
                HttpStatusCode.OK,
                ct);
        }

        // Warm up query endpoint once before measuring allocations and SQL statements.
        _ = await SendAsync<FormQueryResponse[]>(
            monitor, HttpMethod.Get, $"/queries?study_id={study.Value:D}&limit=25", null, null, HttpStatusCode.OK, ct);

        using var sql = EfSqlCounter.Start();
        var beforeLiveBytes = GC.GetTotalAllocatedBytes(precise: true);
        sql.Reset();

        var livePage = await SendAsync<FormQueryResponse[]>(
            monitor, HttpMethod.Get, $"/queries?study_id={study.Value:D}&limit=25", null, null, HttpStatusCode.OK, ct);

        var liveAllocatedMb = (GC.GetTotalAllocatedBytes(precise: true) - beforeLiveBytes) / (1024.0 * 1024.0);
        var liveSqlCount = sql.Count;
        output.WriteLine($"300-form live ListQueriesAsync — SQL count: {liveSqlCount}, Allocated: {liveAllocatedMb:F2} MB");

        livePage.Should().HaveCount(25);
        liveSqlCount.Should().BeLessThan(15, "ShapeQueriesAsync uses narrow FindQueryTargetsAsync for the 25 page forms only");
        liveAllocatedMb.Should().BeLessThan(25.0, "300-form study query worklist must stay far below the 89.94 MB baseline");

        // Measure bitemporal as_of filtered + paged query across the 300-form study.
        var beforeAsOfBytes = GC.GetTotalAllocatedBytes(precise: true);
        sql.Reset();

        var encodedAsOf = Uri.EscapeDataString(asOfSnapshot.ToString("O"));
        var asOfPage = await SendAsync<FormQueryResponse[]>(
            monitor,
            HttpMethod.Get,
            $"/queries?study_id={study.Value:D}&status=open&value_changed=true&as_of={encodedAsOf}&limit=5&offset=2",
            null,
            null,
            HttpStatusCode.OK,
            ct);

        var asOfAllocatedMb = (GC.GetTotalAllocatedBytes(precise: true) - beforeAsOfBytes) / (1024.0 * 1024.0);
        var asOfSqlCount = sql.Count;
        output.WriteLine($"300-form as_of ListQueriesAsync — SQL count: {asOfSqlCount}, Allocated: {asOfAllocatedMb:F2} MB");

        asOfPage.Should().HaveCount(5, "all 10 value-changed queries were open at asOfSnapshot, so offset=2&limit=5 returns 5");
        asOfPage.Should().OnlyContain(q => q.Status == "open" && q.ValueChanged && q.CurrentRevision == 2);
        asOfSqlCount.Should().BeLessThan(18);
        asOfAllocatedMb.Should().BeLessThan(25.0);

        var currentFiltered = await SendAsync<FormQueryResponse[]>(
            monitor,
            HttpMethod.Get,
            $"/queries?study_id={study.Value:D}&status=open&value_changed=true&limit=25",
            null,
            null,
            HttpStatusCode.OK,
            ct);
        currentFiltered.Should().HaveCount(5, "5 of the 10 value-changed queries were answered after asOfSnapshot");
    }

    private async Task<(string Code, TenantId Tenant, StudyId Study, StudySiteId Site, EnrollmentId Enrollment, ProtocolVersionId Version)> SeedStudyAndActorsAsync(CancellationToken ct)
    {
        var form = await _scenario.NewFormAsync(ct);
        var tenant = form.Subject.Tenant;
        var study = form.Subject.Study;
        var site = form.Subject.StudySite;

        await using var admin = fixture.CreateAdminContext();
        var code = await admin.Tenants.AsNoTracking().Where(t => t.Id == tenant).Select(t => t.Code).SingleAsync(ct);

        foreach (var (actor, capabilities, scope) in new (Actor, string[], AuthorityScope)[]
        {
            (Actor.Person(Crc), ["collection.open", "collection.record", "execution.query.answer"], AuthorityScope.StudySite(site)),
            (Actor.Person(Monitor), ["execution.query.raise", "execution.query.manage"], AuthorityScope.Study(study)),
            (Actor.Person(UnblindedPharmacist), ["enrollment.unblind"], AuthorityScope.Study(study)),
            (Actor.ServiceAccount("execution-forms"), ["execution.form.open"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount("execution-queries"), ["observation.query", "observation.query.answer", "assertion.retract"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount(EvaluateEditChecksOnCollectionChanged.Name), ["validation_run.record", "collection.record_unasked", "execution.query.raise", "execution.query.manage"], AuthorityScope.Tenant()),
        })
        {
            foreach (var cap in capabilities)
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), actor, cap, null, scope, _scenario.Tick(), null, false, KernelProtocolScenario.Key(), "Test grant"),
                    ct);
            }
        }

        // Create a fresh enrollment for clean form counts (since NewFormAsync opened 1 initial form on form.Subject.Enrollment).
        var cleanEnrollment = await _scenario.Observations.ScreenAsync(tenant, site, ct);
        return (code, tenant, study, site, cleanEnrollment, form.Version);
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(fixture.Clock);
                services.AddDeclaredKernelErrorsCheck();
                services.RemoveAll<ICollectionInstanceRecords>();
                _recordsSpy = new DelegatingRecordsSpy(fixture.CollectionInstanceRecords);
                services.AddSingleton<ICollectionInstanceRecords>(_recordsSpy);
                services.RemoveAll<KernelBackgroundOptions>();
                services.AddSingleton(new KernelBackgroundOptions
                {
                    RelayInterval = TimeSpan.FromHours(1),
                    ActivityInterval = TimeSpan.FromHours(1),
                });
            }));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private async Task RelayOutboxAsync(TenantId tenant, CancellationToken ct)
    {
        for (var pass = 0; pass < 40; pass++)
        {
            await fixture.OutboxRelay.RelayBatchAsync(10_000, ct);
            await using var db = fixture.CreateAdminContext();
            var remaining = await db.OutboxMessages.AnyAsync(m => m.TenantId == tenant && m.DispatchedAt == null, ct);
            if (!remaining)
            {
                break;
            }

            await Task.Delay(20, ct);
        }
    }

    private static async Task<T> SendAsync<T>(
        HttpClient client, HttpMethod method, string path, string? reason, object? body, HttpStatusCode expected, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri($"{FormEndpoints.RoutePrefix}{path}", UriKind.Relative));
        request.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    /// <summary>
    /// Counts EF Core relational SQL commands executed during the active measurement window via <see cref="DiagnosticListener"/>.
    /// </summary>
    private sealed class EfSqlCounter : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private static readonly AsyncLocal<EfSqlCounter?> Current = new();
        private readonly ConcurrentQueue<string> _commands = new();
        private readonly IDisposable _subscription;
        private readonly List<IDisposable> _innerSubscriptions = [];
        private volatile bool _disposed;

        private EfSqlCounter()
        {
            _subscription = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public int Count => _commands.Count;

        public int SelectCount => _commands.Count(c =>
            c.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            && !c.Contains("set_config(", StringComparison.OrdinalIgnoreCase));

        public static EfSqlCounter Start()
        {
            var counter = new EfSqlCounter();
            Current.Value = counter;
            return counter;
        }

        public void Reset() => _commands.Clear();

        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == DbLoggerCategory.Name)
            {
                lock (_innerSubscriptions)
                {
                    _innerSubscriptions.Add(value.Subscribe(this));
                }
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!_disposed
                && ReferenceEquals(Current.Value, this)
                && value.Key == RelationalEventId.CommandExecuted.Name
                && value.Value is CommandExecutedEventData data)
            {
                _commands.Enqueue(data.Command.CommandText);
            }
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }

        public void Dispose()
        {
            _disposed = true;
            if (ReferenceEquals(Current.Value, this))
            {
                Current.Value = null;
            }

            _subscription.Dispose();
            lock (_innerSubscriptions)
            {
                foreach (var sub in _innerSubscriptions)
                {
                    sub.Dispose();
                }

                _innerSubscriptions.Clear();
            }
        }
    }

    private sealed class DelegatingRecordsSpy(ICollectionInstanceRecords inner) : ICollectionInstanceRecords
    {
        private int _findCalls;
        private int _findHeaderCalls;
        private int _findScopeStateCalls;
        private int _listRecordsCalls;

        public int FindCalls => Volatile.Read(ref _findCalls);
        public int FindHeaderCalls => Volatile.Read(ref _findHeaderCalls);
        public int FindScopeStateCalls => Volatile.Read(ref _findScopeStateCalls);
        public int ListRecordsForEnrollmentCalls => Volatile.Read(ref _listRecordsCalls);

        public void Reset()
        {
            Interlocked.Exchange(ref _findCalls, 0);
            Interlocked.Exchange(ref _findHeaderCalls, 0);
            Interlocked.Exchange(ref _findScopeStateCalls, 0);
            Interlocked.Exchange(ref _listRecordsCalls, 0);
        }

        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _findCalls);
            return inner.FindAsync(tenant, instance, cancellationToken);
        }

        public Task<CollectionInstanceRecord?> FindForReaderAsync(TenantId tenant, CollectionInstanceId instance, bool unblinded, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _findCalls);
            return inner.FindForReaderAsync(tenant, instance, unblinded, cancellationToken);
        }

        public Task<CollectionInstanceRecord?> FindForReaderAsync(
            TenantId tenant,
            CollectionInstanceId instance,
            Func<EnrollmentId, CancellationToken, ValueTask<bool>> canUnblindAsync,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _findCalls);
            return inner.FindForReaderAsync(tenant, instance, canUnblindAsync, cancellationToken);
        }

        public Task<CollectionInstanceHeader?> FindHeaderAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _findHeaderCalls);
            return inner.FindHeaderAsync(tenant, instance, cancellationToken);
        }

        public Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader>> FindHeadersAsync(
            TenantId tenant,
            IReadOnlyCollection<CollectionInstanceId> instances,
            CancellationToken cancellationToken = default) =>
            inner.FindHeadersAsync(tenant, instances, cancellationToken);

        public Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceQueryTarget>> FindQueryTargetsAsync(
            TenantId tenant,
            IReadOnlyCollection<CollectionInstanceId> instances,
            IReadOnlySet<EnrollmentId> unblindedEnrollments,
            CancellationToken cancellationToken = default) =>
            inner.FindQueryTargetsAsync(tenant, instances, unblindedEnrollments, cancellationToken);

        public Task<CollectionInstanceScopeState?> FindScopeStateAsync(
            TenantId tenant,
            CollectionInstanceId instance,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _findScopeStateCalls);
            return inner.FindScopeStateAsync(tenant, instance, cancellationToken);
        }

        public Task<IReadOnlyList<CollectionRuleInput>> ListRuleInputsForEnrollmentAsync(
            TenantId tenant,
            EnrollmentId enrollment,
            CancellationToken cancellationToken = default) =>
            inner.ListRuleInputsForEnrollmentAsync(tenant, enrollment, cancellationToken);

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(
            TenantId tenant,
            EnrollmentId enrollment,
            CancellationToken cancellationToken) =>
            inner.ListForEnrollmentAsync(tenant, enrollment, cancellationToken);

        public Task<IReadOnlyList<CollectionInstanceRecord>> ListRecordsForEnrollmentAsync(
            TenantId tenant,
            EnrollmentId enrollment,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _listRecordsCalls);
            return inner.ListRecordsForEnrollmentAsync(tenant, enrollment, cancellationToken);
        }

        public Task<IReadOnlyDictionary<ObservationId, int>> FindObservationRevisionsAsync(
            TenantId tenant,
            IReadOnlyCollection<ObservationId> observations,
            IReadOnlyCollection<CollectionInstanceId>? instances = null,
            CancellationToken cancellationToken = default) =>
            inner.FindObservationRevisionsAsync(tenant, observations, instances, cancellationToken);
    }
}
