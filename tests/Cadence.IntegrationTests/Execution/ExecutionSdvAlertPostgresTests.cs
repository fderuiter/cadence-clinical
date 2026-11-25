using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Cadence.Kernel.Api;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence.Background;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// End-to-end PostgreSQL integration tests for CRA SDV modification alerts (<c>PRD-QRY-006</c>):
/// <see cref="AlertMonitorOnVerifiedObservationCorrected"/> consumes <see cref="ObservationCorrected"/> via the kernel outbox,
/// reads <see cref="IObservationRecords"/> (<c>K-XS-2</c>), records an idempotent <c>FormSdvAlert</c> with kernel
/// <see cref="StudyId"/> (<c>K-MOD-1</c>) only when the superseded revision carried an un-retracted <c>Verified</c> assertion,
/// exposes <c>study_id</c>, <c>study_code</c>, and <c>enrollment_id</c> filters at <c>GET /api/v1/execution/forms/sdv-alerts</c> (<c>K-XS-1</c>),
/// folds active <c>sdv_alerts</c> into <c>GET /api/v1/kernel/studies/{studyRef}/attention</c> via <see cref="IStudySdvAlertReader"/>,
/// collapses multi-episode edits per observation with latest revision attribution, enforces <c>K-AUTH-4</c> blinding once per enrollment,
/// and pre-filters re-verified historical edits in SQL without <c>FindForReaderAsync</c>, <c>ProtocolDesignDocument.Read</c>, or <c>audit_entries</c> reads.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionSdvAlertPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string Crc = "crc@sdv-alerts.test";
    private const string Crc2 = "crc2@sdv-alerts.test";
    private const string Monitor = "cra@sdv-alerts.test";
    private const string UnblindedMonitor = "unblinded-cra@sdv-alerts.test";
    private const string Stranger = "stranger@sdv-alerts.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private readonly SqlCommandCollector _sql = new();
    private readonly ObservationProbeCounters _probes = new();
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose()
    {
        _host?.Dispose();
        _sql.Dispose();
    }

    [Fact(DisplayName = "ObservationCorrected_AlertsMonitorOnlyWhenVerified_IsIdempotent_FiltersByStudyAndEnrollment_AndClearsOnReVerification")]
    [GxPRequirement("PRD-QRY-006")]
    public async Task ObservationCorrectedAlertsMonitorOnlyWhenVerifiedIsIdempotentFiltersByStudyAndEnrollmentAndClearsOnReVerification()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, tenant, study, _, enrollment, form, systolicObs, diastolicObs) = await SeedAsync(ct);
        await RelayOutboxAsync(tenant, ct);

        using var crc = Client(code, Crc);
        using var monitor = Client(code, Monitor);
        using var stranger = Client(code, Stranger);

        // 1. Editing an UNVERIFIED value (Systolic rev 1 -> rev 2) emits ObservationCorrected, which must NOT record an SDV alert.
        await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{form:D}/values",
            "Corrected unverified entry",
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 124m, unit = "mm[Hg]" }, expected_revision = 1 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(tenant, ct);

        var alertsAfterUnverifiedEdit = await SendAsync<FormSdvAlertResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_id={study.Value:D}", null, null, HttpStatusCode.OK, ct);
        alertsAfterUnverifiedEdit.Should().BeEmpty("correcting an unverified revision must not alert CRAs");

        // 2. Monitor verifies Systolic (rev 2) and Diastolic (rev 1).
        await SendAsync<KernelAssertionStateResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/observations/observation:{systolicObs:D}/verifications",
            "Verified systolic BP against nursing flowsheet",
            new { enrollment_ref = $"enrollment:{enrollment.Value:D}", revision = 2 },
            HttpStatusCode.Created,
            ct);
        await SendAsync<KernelAssertionStateResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/observations/observation:{diastolicObs:D}/verifications",
            "Verified diastolic BP against nursing flowsheet",
            new { enrollment_ref = $"enrollment:{enrollment.Value:D}", revision = 1 },
            HttpStatusCode.Created,
            ct);

        // 3. Coordinator modifies Systolic via eCRF PUT /values (rev 2 -> rev 3) AND corrects Diastolic via kernel POST /corrections (rev 1 -> rev 2).
        await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{form:D}/values",
            "Late source addendum on systolic BP",
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 132m, unit = "mm[Hg]" }, expected_revision = 2 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await SendAsync<KernelObservationStateResponse>(
            crc,
            HttpMethod.Post,
            $"/api/v1/kernel/observations/observation:{diastolicObs:D}/corrections",
            "Late source addendum on diastolic BP",
            new
            {
                enrollment_ref = $"enrollment:{enrollment.Value:D}",
                value = new { type = "quantity", value = 84m, unit = "mm[Hg]" },
                expected_version = 1,
            },
            HttpStatusCode.OK,
            ct);

        await RelayOutboxAsync(tenant, ct);

        // 4. Replaying outbox deliveries is idempotent: no duplicate FormSdvAlert rows are created.
        await fixture.ExecuteAdminAsync(
            $"UPDATE kernel.outbox_messages SET dispatched_at = NULL WHERE tenant_id = '{tenant.Value:D}'", ct);
        await RelayOutboxAsync(tenant, ct);

        var studyAlerts = await SendAsync<FormSdvAlertResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_id={study.Value:D}", null, null, HttpStatusCode.OK, ct);
        studyAlerts.Should().HaveCount(2);

        var systolicAlert = studyAlerts.Single(a => a.ObservationId == systolicObs);
        systolicAlert.StudyId.Should().Be(study.Value);
        systolicAlert.EnrollmentId.Should().Be(enrollment.Value);
        systolicAlert.FormId.Should().Be(form);
        systolicAlert.ItemId.Should().Be(Systolic);
        systolicAlert.SupersededRevision.Should().Be(2);
        systolicAlert.CurrentRevision.Should().Be(3);
        systolicAlert.VerifiedBy.Should().Be(Monitor);
        systolicAlert.ModifiedBy.Should().Be(Crc);
        systolicAlert.Message.Should().Contain("Previously verified field modified on Subject")
            .And.Contain($"Visit {VisitId} ({Systolic})");

        var diastolicAlert = studyAlerts.Single(a => a.ObservationId == diastolicObs);
        diastolicAlert.ItemId.Should().Be(Diastolic);
        diastolicAlert.SupersededRevision.Should().Be(1);
        diastolicAlert.CurrentRevision.Should().Be(2);

        // Enrollment filter returns the same active alerts; missing filter returns 400; unauthorized caller gets 403.
        var enrollmentAlerts = await SendAsync<FormSdvAlertResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?enrollment_id={enrollment.Value:D}", null, null, HttpStatusCode.OK, ct);
        enrollmentAlerts.Select(a => a.AlertId).Should().BeEquivalentTo(studyAlerts.Select(a => a.AlertId));

        using (var badFilter = await monitor.SendAsync(Request(HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts", null, null), ct))
        {
            badFilter.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using (var denied = await stranger.SendAsync(Request(HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_id={study.Value:D}", null, null), ct))
        {
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 5. Once the monitor re-verifies Systolic at revision 3, GET /sdv-alerts omits Systolic while keeping Diastolic active.
        await SendAsync<KernelAssertionStateResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/observations/observation:{systolicObs:D}/verifications",
            "Re-verified systolic BP revision 3 against addendum",
            new { enrollment_ref = $"enrollment:{enrollment.Value:D}", revision = 3 },
            HttpStatusCode.Created,
            ct);

        var remainingAlerts = await SendAsync<FormSdvAlertResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_id={study.Value:D}", null, null, HttpStatusCode.OK, ct);
        remainingAlerts.Should().ContainSingle().Which.ObservationId.Should().Be(diastolicObs);
    }

    [Fact(DisplayName = "StudyCodeFilter_StudyAttentionSdvAlerts_EpisodeCollapsing_LatestAttribution_AndBlindingMasks")]
    [GxPRequirement("PRD-QRY-006")]
    public async Task StudyCodeFilterStudyAttentionSdvAlertsEpisodeCollapsingLatestAttributionAndBlindingMasks()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const string treatmentItem = "item.randomization_treatment_assignment";
        const string kitItem = "item.vial_barcode";

        fixture.Clock.Set(SystemClock.Instance.UtcNow.AddHours(-3));
        var code = $"T-ATN-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Attention SDV alerts tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var studyCode = await GetStudyCodeAsync(tenant, study, ct);

        var sbpConcept = await DefineConceptAsync(tenant, "Systolic blood pressure", ct);
        var dbpConcept = await DefineConceptAsync(tenant, "Diastolic blood pressure", ct);
        var treatmentConcept = await DefineConceptAsync(tenant, "Randomized treatment arm (randomization_treatment_assignment)", ct);
        var vialConcept = await DefineConceptAsync(tenant, "Bottle barcode", ct);

        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity(VisitId, "Week 12 visit", "ep.treatment", [ActivityLocation.Site], [SpecId], new ActivityTiming(84, 3, 3)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    SpecId,
                    "Vital signs panel",
                    [
                        new CollectionItem(Systolic, sbpConcept, "mm[Hg]", Required: true),
                        new CollectionItem(Diastolic, dbpConcept, "mm[Hg]", Required: true),
                        new CollectionItem(treatmentItem, treatmentConcept, ValueKind: ObservationValueKind.Text, Required: true),
                        new CollectionItem(kitItem, vialConcept, ValueKind: ObservationValueKind.Text, Required: true, Prompt: "Kit number dispensed"),
                    ]),
            ],
        };

        var version = await MakeEffectiveAsync(tenant, study, design, ct);
        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);

        await GrantAsync(
            tenant,
            [
                (Actor.Person(Crc), ["collection.open", "collection.record", "observation.correct"], AuthorityScope.StudySite(studySite)),
                (Actor.Person(Crc2), ["collection.open", "collection.record", "observation.correct"], AuthorityScope.StudySite(studySite)),
                (Actor.Person(Monitor), ["observation.verify", "assertion.retract", "execution.query.raise", "execution.query.manage"], AuthorityScope.Study(study)),
                (Actor.Person(UnblindedMonitor), ["observation.verify", "assertion.retract", "execution.query.raise", "execution.query.manage", "enrollment.unblind"], AuthorityScope.Study(study)),
                (Actor.ServiceAccount("execution-forms"), ["execution.form.open"], AuthorityScope.Tenant()),
                (Actor.ServiceAccount("execution-queries"), ["observation.query", "observation.query.answer", "assertion.retract"], AuthorityScope.Tenant()),
                (Actor.ServiceAccount(AlertMonitorOnVerifiedObservationCorrected.Name), ["execution.query.manage"], AuthorityScope.Tenant()),
            ],
            ct);

        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, enrollment);
        var opened = await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct);
        var saved = await _scenario.Protocol.OkAsync(
            Save(
                new Form(subject, version, opened.CollectionInstanceId),
                Value(Systolic, Mmhg(120)),
                Value(Diastolic, Mmhg(80)),
                Value(treatmentItem, new TextValue("ARM-ACTIVE-200MG")),
                Value(kitItem, new TextValue("KIT-9001"))),
            ct);
        var sbpObs = saved.Values.Single(v => v.ItemId == Systolic).ObservationId.Value;
        var dbpObs = saved.Values.Single(v => v.ItemId == Diastolic).ObservationId.Value;
        var armObs = saved.Values.Single(v => v.ItemId == treatmentItem).ObservationId.Value;
        var kitObs = saved.Values.Single(v => v.ItemId == kitItem).ObservationId.Value;
        await RelayOutboxAsync(tenant, ct);

        using var crc = Client(code, Crc);
        using var crc2 = Client(code, Crc2);
        using var monitor = Client(code, Monitor);
        using var unblindedMonitor = Client(code, UnblindedMonitor);
        using var stranger = Client(code, Stranger);

        // Unblinded monitor verifies rev 1 on all four items (2 unblinded vitals + 2 blinded randomization items).
        foreach (var obsId in new[] { sbpObs, dbpObs, armObs, kitObs })
        {
            await SendAsync<KernelAssertionStateResponse>(
                unblindedMonitor,
                HttpMethod.Post,
                $"/api/v1/kernel/observations/observation:{obsId:D}/verifications",
                "Initial SDV against source",
                new { enrollment_ref = $"enrollment:{enrollment.Value:D}", revision = 1 },
                HttpStatusCode.Created,
                ct);
        }

        // Episode 1 on all four items (rev 1 -> rev 2 by Crc).
        await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{opened.CollectionInstanceId.Value:D}/values",
            "Episode 1 correction by CRC",
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 128m, unit = "mm[Hg]" }, expected_revision = 1 },
                    new { item_id = Diastolic, value = new { type = "quantity", value = 84m, unit = "mm[Hg]" }, expected_revision = 1 },
                    new { item_id = treatmentItem, value = new { type = "text", value = "ARM-ACTIVE-300MG" }, expected_revision = 1 },
                    new { item_id = kitItem, value = new { type = "text", value = "KIT-9002" }, expected_revision = 1 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(tenant, ct);

        // Diastolic is re-verified at rev 2, edited again by Crc (rev 2 -> rev 3, producing a SECOND FormSdvAlert row),
        // and then edited once more WITHOUT re-verification by Crc2 (rev 3 -> rev 4).
        await SendAsync<KernelAssertionStateResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/observations/observation:{dbpObs:D}/verifications",
            "Re-verified diastolic rev 2",
            new { enrollment_ref = $"enrollment:{enrollment.Value:D}", revision = 2 },
            HttpStatusCode.Created,
            ct);

        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{opened.CollectionInstanceId.Value:D}/values",
            "Episode 2 verified edit by CRC",
            new
            {
                values = new object[]
                {
                    new { item_id = Diastolic, value = new { type = "quantity", value = 86m, unit = "mm[Hg]" }, expected_revision = 2 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(tenant, ct);

        var rev4Timestamp = fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        const string latestUnverifiedReason = "Subsequent unverified edit on diastolic rev 4 by CRC2";
        var rev4State = await SendAsync<KernelObservationStateResponse>(
            crc2,
            HttpMethod.Post,
            $"/api/v1/kernel/observations/observation:{dbpObs:D}/corrections",
            latestUnverifiedReason,
            new
            {
                enrollment_ref = $"enrollment:{enrollment.Value:D}",
                value = new { type = "quantity", value = 88m, unit = "mm[Hg]" },
                expected_version = 3,
            },
            HttpStatusCode.OK,
            ct);
        rev4State.Revision.Should().Be(4);
        await RelayOutboxAsync(tenant, ct);

        // Verify insert-only table holds two distinct FormSdvAlert rows for Diastolic (1->2 and 2->3).
        await using (var execDb = CreateExecutionContext())
        {
            var dbpAlertRows = await execDb.FormSdvAlerts.IgnoreQueryFilters()
                .CountAsync(a => a.TenantId == tenant && a.ObservationId == ObservationId.FromGuid(dbpObs), ct);
            dbpAlertRows.Should().Be(2, "FormSdvAlert remains strictly insert-only across episodes");
        }

        // 1. Blinded monitor calling ?study_code= (case-insensitive) sees only the 2 non-blinded items,
        //    with Diastolic collapsed to 1 episode (superseded=2, current=4) and rev 4 attribution from Crc2,
        //    zero ProtocolDesignDocument.Read calls, and CanUnblindAsync evaluated once for the enrollment (not twice).
        _probes.Reset();
        _sql.Reset();
        FormSdvAlertResponse[] byStudyCode;
        using (var reads = ProtocolDesignDocument.MeasureReads())
        {
            byStudyCode = await SendAsync<FormSdvAlertResponse[]>(
                monitor,
                HttpMethod.Get,
                $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_code={studyCode.ToLowerInvariant()}",
                null,
                null,
                HttpStatusCode.OK,
                ct);
            reads.Count.Should().Be(0, "study_code SDV alerts must not parse ProtocolDesignDocument JSON");
        }

        _probes.FindForReaderCalls.Should().Be(0, "SDV alert evaluation must use FindUnverifiedSdvTargetsAsync");
        _probes.FindUnverifiedSdvTargetsCalls.Should().Be(1);
        _probes.CanUnblindCalls.Should().Be(1, "CanUnblindAsync must be memoized once per distinct enrollment across multiple blinded items");
        _sql.Snapshot().Should().NotContain(sql => sql.Contains("audit_entries", StringComparison.OrdinalIgnoreCase));
        _sql.Snapshot().Should().NotContain(sql => sql.Contains("code_system_entries", StringComparison.OrdinalIgnoreCase));

        byStudyCode.Should().HaveCount(2, "blinded randomization items must be omitted for blinded monitors (K-AUTH-4)");
        byStudyCode.Select(a => a.ObservationId).Should().BeEquivalentTo([sbpObs, dbpObs]);

        var collapsedDbp = byStudyCode.Single(a => a.ObservationId == dbpObs);
        collapsedDbp.SupersededRevision.Should().Be(2, "episode collapsing selects the latest superseded verified revision");
        collapsedDbp.CurrentRevision.Should().Be(4, "current_revision reflects subsequent unverified edits");
        collapsedDbp.VerifiedBy.Should().Be(Monitor);
        collapsedDbp.ModifiedBy.Should().Be(Crc2, "modified_by reflects the latest revision editor when currentRevision > alert.NewRevision");
        collapsedDbp.ReasonForChange.Should().Be(latestUnverifiedReason);
        collapsedDbp.ModifiedAt.Should().BeAfter(rev4Timestamp.AddSeconds(-5));

        // 2. Unblinded monitor (holding enrollment.unblind) sees all 4 active SDV alerts via both /sdv-alerts and /attention.
        var unblindedAlerts = await SendAsync<FormSdvAlertResponse[]>(
            unblindedMonitor,
            HttpMethod.Get,
            $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_code={studyCode}",
            null,
            null,
            HttpStatusCode.OK,
            ct);
        unblindedAlerts.Should().HaveCount(4);
        unblindedAlerts.Select(a => a.ObservationId).Should().BeEquivalentTo([sbpObs, dbpObs, armObs, kitObs]);

        var unblindedAttention = await SendAsync<KernelStudyAttentionResponse>(
            unblindedMonitor,
            HttpMethod.Get,
            $"/api/v1/kernel/studies/{studyCode}/attention",
            null,
            null,
            HttpStatusCode.OK,
            ct);
        unblindedAttention.SdvAlerts.Should().NotBeNull().And.HaveCount(4);

        // 3. Blinded monitor calling Wave-1 GET /api/v1/kernel/studies/{studyCode}/attention receives the exact 2 non-blinded SDV alerts.
        var blindedAttention = await SendAsync<KernelStudyAttentionResponse>(
            monitor,
            HttpMethod.Get,
            $"/api/v1/kernel/studies/{studyCode}/attention",
            null,
            null,
            HttpStatusCode.OK,
            ct);
        blindedAttention.SdvAlerts.Should().NotBeNull();
        blindedAttention.SdvAlerts!.Select(a => a.AlertId).Should().Equal(byStudyCode.Select(a => a.AlertId));
        blindedAttention.SdvAlerts.Single(a => a.ObservationId == dbpObs).ModifiedBy.Should().Be(Crc2);

        // 4. Error semantics on study_code: unknown -> 404, mismatched study_id + study_code -> 400, unauthorized -> 403.
        using (var unknownStudy = await monitor.SendAsync(Request(HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_code=UNKNOWN-STUDY-999", null, null), ct))
        {
            unknownStudy.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using (var mismatchedStudy = await monitor.SendAsync(
            Request(HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_id={Guid.NewGuid():D}&study_code={studyCode}", null, null), ct))
        {
            mismatchedStudy.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using (var strangerStudyCode = await stranger.SendAsync(
            Request(HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_code={studyCode}", null, null), ct))
        {
            strangerStudyCode.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 5. Synchronous alert clearing: re-verifying Diastolic at revision 4 WITHOUT relaying outbox immediately clears it
        //    from both Wave-1 /attention and /sdv-alerts.
        await SendAsync<KernelAssertionStateResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/observations/observation:{dbpObs:D}/verifications",
            "Re-verified diastolic rev 4 against final source",
            new { enrollment_ref = $"enrollment:{enrollment.Value:D}", revision = 4 },
            HttpStatusCode.Created,
            ct);

        var attentionAfterVerify = await SendAsync<KernelStudyAttentionResponse>(
            monitor,
            HttpMethod.Get,
            $"/api/v1/kernel/studies/{studyCode}/attention",
            null,
            null,
            HttpStatusCode.OK,
            ct);
        attentionAfterVerify.SdvAlerts.Should().ContainSingle().Which.ObservationId.Should().Be(sbpObs);

        var sdvAfterVerify = await SendAsync<FormSdvAlertResponse[]>(
            monitor,
            HttpMethod.Get,
            $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_code={studyCode}",
            null,
            null,
            HttpStatusCode.OK,
            ct);
        sdvAfterVerify.Should().ContainSingle().Which.ObservationId.Should().Be(sbpObs);
    }

    [Fact(DisplayName = "ThreeHundredReVerifiedHistoricalEdits_PreFiltersInSql_ZeroFindForReader_ZeroProtocolReads_ZeroAuditQueries")]
    [GxPRequirement("PRD-QRY-006")]
    public async Task ThreeHundredReVerifiedHistoricalEditsPreFiltersInSqlZeroFindForReaderZeroProtocolReadsZeroAuditQueries()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const int formCount = 6;
        const int itemsPerForm = 50;
        const int totalObservations = formCount * itemsPerForm;

        fixture.Clock.Set(SystemClock.Instance.UtcNow.AddHours(-4));
        var code = $"T-300-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "300 historical SDV alerts tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var studyCode = await GetStudyCodeAsync(tenant, study, ct);

        var sbpConcept = await DefineConceptAsync(tenant, "Systolic blood pressure", ct);
        var collectionItems = Enumerable.Range(1, itemsPerForm)
            .Select(idx => new CollectionItem($"item.metric_{idx:D3}", sbpConcept, "mm[Hg]", Required: false))
            .ToArray();

        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity(VisitId, "Week 12 visit", "ep.treatment", [ActivityLocation.Site], [SpecId], new ActivityTiming(84, 3, 3)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(SpecId, "High-density vitals panel", collectionItems),
            ],
        };

        var version = await MakeEffectiveAsync(tenant, study, design, ct);
        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);

        await GrantAsync(
            tenant,
            [
                (Actor.Person(Monitor), ["observation.verify", "assertion.retract", "execution.query.raise", "execution.query.manage"], AuthorityScope.Study(study)),
                (Actor.ServiceAccount("execution-forms"), ["execution.form.open"], AuthorityScope.Tenant()),
                (Actor.ServiceAccount("execution-queries"), ["observation.query", "observation.query.answer", "assertion.retract"], AuthorityScope.Tenant()),
                (Actor.ServiceAccount(AlertMonitorOnVerifiedObservationCorrected.Name), ["execution.query.manage"], AuthorityScope.Tenant()),
            ],
            ct);

        var recordedObservations = new List<(EnrollmentId Enrollment, ObservationId Observation)>(totalObservations);
        for (var f = 0; f < formCount; f++)
        {
            var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);
            var subject = new KernelObservationScenario.Subject(tenant, study, studySite, enrollment);
            var opened = await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct);
            var form = new Form(subject, version, opened.CollectionInstanceId);

            var initialValues = collectionItems
                .Select((item, idx) => Value(item.Id, Mmhg(100 + idx), expectedRevision: 0))
                .ToArray();
            var savedRev1 = await _scenario.Protocol.OkAsync(Save(form, initialValues), ct);

            foreach (var v in savedRev1.Values)
            {
                await _scenario.Protocol.OkAsync(
                    new VerifyObservation(tenant, AssertionId.New(), enrollment, v.ObservationId, 1, KernelProtocolScenario.Key(), "Verified rev 1"),
                    ct);
                recordedObservations.Add((enrollment, v.ObservationId));
            }

            var updatedValues = collectionItems
                .Select((item, idx) => Value(item.Id, Mmhg(110 + idx), expectedRevision: 1))
                .ToArray();
            await _scenario.Protocol.OkAsync(Save(form, updatedValues), ct);
        }

        recordedObservations.Should().HaveCount(totalObservations);
        await RelayOutboxAsync(tenant, ct);

        await using (var execDb = CreateExecutionContext())
        {
            var alertRows = await execDb.FormSdvAlerts.IgnoreQueryFilters()
                .CountAsync(a => a.TenantId == tenant && a.StudyId == study, ct);
            alertRows.Should().Be(totalObservations, "all 300 verified-then-edited observations produce historical FormSdvAlert rows");
        }

        // Re-verify all 300 observations at revision 2 so all 300 historical alert rows are now resolved.
        foreach (var (enrollment, observation) in recordedObservations)
        {
            await _scenario.Protocol.OkAsync(
                new VerifyObservation(tenant, AssertionId.New(), enrollment, observation, 2, KernelProtocolScenario.Key(), "Re-verified rev 2"),
                ct);
        }

        using var monitor = Client(code, Monitor);

        // Warm caches/connections once before measuring steady-state SQL and port calls.
        var warmAlerts = await SendAsync<FormSdvAlertResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_code={studyCode}", null, null, HttpStatusCode.OK, ct);
        warmAlerts.Should().BeEmpty();

        // Measure GET /api/v1/execution/forms/sdv-alerts?study_code=...
        _probes.Reset();
        _sql.Reset();
        using (var reads = ProtocolDesignDocument.MeasureReads())
        {
            var activeAlerts = await SendAsync<FormSdvAlertResponse[]>(
                monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_code={studyCode}", null, null, HttpStatusCode.OK, ct);
            activeAlerts.Should().BeEmpty("all 300 historical edits were re-verified at their current revision");
            reads.Count.Should().Be(0, "300 re-verified historical edits must execute zero ProtocolDesignDocument.Read calls");
        }

        var sdvSql = _sql.Snapshot();
        _probes.FindForReaderCalls.Should().Be(0, "300 re-verified historical edits must execute zero FindForReaderAsync calls");
        _probes.FindUnverifiedSdvTargetsCalls.Should().Be(1);
        _probes.CanUnblindCalls.Should().Be(0, "re-verified rows are pre-filtered in SQL before unblinding checks");
        sdvSql.Should().NotContain(sql => sql.Contains("audit_entries", StringComparison.OrdinalIgnoreCase));
        sdvSql.Should().NotContain(sql => sql.Contains("code_system_entries", StringComparison.OrdinalIgnoreCase));
        sdvSql.Count(sql => sql.Contains("kernel.observations", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
        sdvSql.Count.Should().BeLessThanOrEqualTo(10, "300 historical alerts collapse to constant bounded SQL queries instead of an O(N) waterfall");

        // Measure Wave-1 GET /api/v1/kernel/studies/{studyCode}/attention under the same 300 historical alerts.
        _probes.Reset();
        _sql.Reset();
        using (var reads = ProtocolDesignDocument.MeasureReads())
        {
            var attention = await SendAsync<KernelStudyAttentionResponse>(
                monitor, HttpMethod.Get, $"/api/v1/kernel/studies/{studyCode}/attention", null, null, HttpStatusCode.OK, ct);
            attention.SdvAlerts.Should().NotBeNull().And.BeEmpty();
            reads.Count.Should().Be(0, "Wave-1 /attention with 300 re-verified SDV alerts must execute zero ProtocolDesignDocument.Read calls");
        }

        var attentionSql = _sql.Snapshot();
        _probes.FindForReaderCalls.Should().Be(0);
        _probes.FindUnverifiedSdvTargetsCalls.Should().Be(1);
        attentionSql.Should().NotContain(sql => sql.Contains("audit_entries", StringComparison.OrdinalIgnoreCase));
        attentionSql.Should().NotContain(sql => sql.Contains("code_system_entries", StringComparison.OrdinalIgnoreCase));
        attentionSql.Count(sql => sql.Contains("WITH unverified AS MATERIALIZED", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
    }

    private async Task<(string Code, TenantId Tenant, StudyId Study, string StudyCode, EnrollmentId Enrollment, Guid Form, Guid SystolicObs, Guid DiastolicObs)> SeedAsync(
        CancellationToken ct)
    {
        fixture.Clock.Set(SystemClock.Instance.UtcNow.AddHours(-2));
        var code = $"T-SDV-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Execution SDV alerts tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var studyCode = await GetStudyCodeAsync(tenant, study, ct);
        var version = await _scenario.EffectiveVersionAsync(tenant, study, ct);
        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);

        await GrantAsync(
            tenant,
            [
                (Actor.Person(Crc), ["collection.open", "collection.record", "observation.correct"], AuthorityScope.StudySite(studySite)),
                (Actor.Person(Monitor), ["observation.verify", "assertion.retract", "execution.query.raise", "execution.query.manage"], AuthorityScope.Study(study)),
                (Actor.ServiceAccount("execution-forms"), ["execution.form.open"], AuthorityScope.Tenant()),
                (Actor.ServiceAccount("execution-queries"), ["observation.query", "observation.query.answer", "assertion.retract"], AuthorityScope.Tenant()),
                (Actor.ServiceAccount(AlertMonitorOnVerifiedObservationCorrected.Name), ["execution.query.manage"], AuthorityScope.Tenant()),
            ],
            ct);

        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, enrollment);
        var opened = await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct);
        var saved = await _scenario.Protocol.OkAsync(
            Save(new Form(subject, version, opened.CollectionInstanceId), Value(Systolic, Mmhg(120)), Value(Diastolic, Mmhg(80))), ct);
        var sbp = saved.Values.Single(v => v.ItemId == Systolic).ObservationId.Value;
        var dbp = saved.Values.Single(v => v.ItemId == Diastolic).ObservationId.Value;
        return (code, tenant, study, studyCode, enrollment, opened.CollectionInstanceId.Value, sbp, dbp);
    }

    private async Task<string> GetStudyCodeAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        return await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.StudyCode).SingleAsync(ct);
    }

    private Task<ConceptRef> DefineConceptAsync(TenantId tenant, string preferredLabel, CancellationToken ct) =>
        _scenario.Protocol.OkAsync(
            new DefineConcept(
                tenant,
                ConceptId.New(),
                ConceptOrigin.Sponsor,
                null,
                ConceptKind.BiomedicalConcept,
                preferredLabel,
                $"{preferredLabel}.",
                KernelProtocolScenario.Key(),
                "Defined test concept"),
            ct);

    private async Task<ProtocolVersionId> MakeEffectiveAsync(TenantId tenant, StudyId study, ProtocolDesign design, CancellationToken ct)
    {
        var version = ProtocolVersionId.New();
        var draft = await _scenario.Protocol.CreateDraftAsync(tenant, study, version, ct, ProtocolDesignDocument.Write(design));
        draft.IsSuccess.Should().BeTrue(draft.Error?.Message);
        var inReview = await _scenario.Protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, version, KernelProtocolScenario.Key(), "Ready"), ct);
        var signature = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _scenario.Protocol.OkAsync(new ApproveProtocolVersion(tenant, version, signature, KernelProtocolScenario.Key(), "Approved"), ct);

        var effectivity = EffectivityId.New();
        await _scenario.Protocol.OkAsync(
            new DeclareEffectivity(tenant, effectivity, version, EffectivityScope.Study(), _scenario.Tick(), KernelProtocolScenario.Key(), "Study-wide"), ct);
        var ethicsBytes = $"ethics-{Guid.NewGuid():N}";
        var ethicsVersion = ArtifactVersionId.New();
        var content = new ArtifactContent(
            ContentHash.OfBytes(Encoding.UTF8.GetBytes(ethicsBytes)),
            ethicsBytes.Length,
            "application/pdf",
            $"blob://evidence/{ethicsBytes}");
        await _scenario.Protocol.OkAsync(
            new RegisterArtifact(
                tenant,
                ArtifactId.New(),
                ethicsVersion,
                ArtifactKind.EthicsApproval,
                study,
                "Ethics approval",
                content,
                KernelProtocolScenario.Key(),
                "IRB approval"),
            ct);
        await _scenario.Protocol.OkAsync(
            new ChangeEffectivityStatus(tenant, effectivity, EffectivityStatus.Effective, _scenario.Tick(), [ethicsVersion], KernelProtocolScenario.Key(), "Effective"),
            ct);
        return version;
    }

    private async Task GrantAsync(TenantId tenant, IEnumerable<(Actor Actor, string[] Capabilities, AuthorityScope Scope)> grants, CancellationToken ct)
    {
        foreach (var (actor, capabilities, scope) in grants)
        {
            foreach (var capability in capabilities)
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), actor, capability, null, scope, _scenario.Tick(), null, false, KernelProtocolScenario.Key(), "Appointed"),
                    ct);
            }
        }
    }

    private ExecutionModuleDbContext CreateExecutionContext()
    {
        var options = new DbContextOptionsBuilder<ExecutionModuleDbContext>()
            .UseNpgsql(fixture.AdminConnectionString)
            .Options;
        return new ExecutionModuleDbContext(options);
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseTestBiometricLivenessKey()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(_sql);
            })
            .ConfigureTestServices(services =>
            {
                services.AddDeclaredKernelErrorsCheck();
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(fixture.Clock);
                services.RemoveAll<KernelBackgroundOptions>();
                services.AddSingleton(new KernelBackgroundOptions
                {
                    RelayInterval = TimeSpan.FromHours(1),
                    ActivityInterval = TimeSpan.FromHours(1),
                });
                DecorateService<IObservationRecords>(services, inner => new CountingObservationRecords(inner, _probes));
                DecorateService<IKernelReadAccess>(services, inner => new CountingKernelReadAccess(inner, _probes));
            }));
        _host.Server.PreserveExecutionContext = true;
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static void DecorateService<TService>(IServiceCollection services, Func<TService, TService> wrap)
        where TService : class
    {
        var existing = services.LastOrDefault(d => d.ServiceType == typeof(TService))
            ?? throw new InvalidOperationException($"Service {typeof(TService).Name} is not registered.");
        services.Remove(existing);
        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            sp =>
            {
                var inner = existing.ImplementationInstance as TService
                    ?? (existing.ImplementationFactory is not null
                        ? (TService)existing.ImplementationFactory(sp)
                        : (TService)ActivatorUtilities.CreateInstance(sp, existing.ImplementationType!));
                return wrap(inner);
            },
            existing.Lifetime));
    }

    private async Task RelayOutboxAsync(TenantId tenant, CancellationToken ct)
    {
        var now = SystemClock.Instance.UtcNow;
        if (now > fixture.Clock.UtcNow)
        {
            fixture.Clock.Set(now);
        }

        for (var pass = 0; pass < 40; pass++)
        {
            await fixture.OutboxRelay.RelayBatchAsync(10_000, ct);
            await using var db = fixture.CreateAdminContext();
            var errors = await db.OutboxMessages
                .Where(m => m.TenantId == tenant && m.LastError != null)
                .Select(m => m.LastError!)
                .ToListAsync(ct);
            errors.Should().BeEmpty(string.Join(" | ", errors));

            var remaining = await db.OutboxMessages
                .AnyAsync(m => m.TenantId == tenant && m.DispatchedAt == null, ct);
            if (!remaining)
            {
                break;
            }

            await Task.Delay(25, ct);
        }
    }

    private static async Task<T> SendAsync<T>(
        HttpClient client, HttpMethod method, string path, string? reason, object? body, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.SendAsync(Request(method, path, reason, body), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? reason, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private sealed class ObservationProbeCounters
    {
        private readonly AsyncLocal<ProbeFrame?> _frame = new();

        public int FindForReaderCalls => _frame.Value?.FindForReaderCalls ?? 0;

        public int FindUnverifiedSdvTargetsCalls => _frame.Value?.FindUnverifiedSdvTargetsCalls ?? 0;

        public int CanUnblindCalls => _frame.Value?.CanUnblindCalls ?? 0;

        public void Reset() => _frame.Value = new ProbeFrame();

        public void RecordFindForReader()
        {
            if (_frame.Value is { } frame)
            {
                Interlocked.Increment(ref frame.FindForReaderCalls);
            }
        }

        public void RecordFindUnverifiedSdvTargets()
        {
            if (_frame.Value is { } frame)
            {
                Interlocked.Increment(ref frame.FindUnverifiedSdvTargetsCalls);
            }
        }

        public void RecordCanUnblind()
        {
            if (_frame.Value is { } frame)
            {
                Interlocked.Increment(ref frame.CanUnblindCalls);
            }
        }

        private sealed class ProbeFrame
        {
            public int FindForReaderCalls;
            public int FindUnverifiedSdvTargetsCalls;
            public int CanUnblindCalls;
        }
    }

    private sealed class CountingObservationRecords(IObservationRecords inner, ObservationProbeCounters counters) : IObservationRecords
    {
        public Task<ObservationRecord?> FindAsync(TenantId tenant, ObservationId observation, CancellationToken cancellationToken) =>
            inner.FindAsync(tenant, observation, cancellationToken);

        public Task<ObservationRecord?> FindForReaderAsync(
            TenantId tenant, ObservationId observation, bool unblinded, CancellationToken cancellationToken)
        {
            counters.RecordFindForReader();
            return inner.FindForReaderAsync(tenant, observation, unblinded, cancellationToken);
        }

        public Task<ObservationRecord?> FindForReaderAsync(
            TenantId tenant,
            ObservationId observation,
            Func<EnrollmentId, CancellationToken, ValueTask<bool>> canUnblindAsync,
            CancellationToken cancellationToken)
        {
            counters.RecordFindForReader();
            return inner.FindForReaderAsync(tenant, observation, canUnblindAsync, cancellationToken);
        }

        public Task<ObservationRecord?> FindForReaderAsync(TenantId tenant, ObservationId observation, CancellationToken cancellationToken)
        {
            counters.RecordFindForReader();
            return inner.FindForReaderAsync(tenant, observation, cancellationToken);
        }

        public Task<IReadOnlyDictionary<ObservationId, ObservationSdvAlertTarget>> FindUnverifiedSdvTargetsAsync(
            TenantId tenant,
            IReadOnlyCollection<ObservationId> observations,
            Func<EnrollmentId, CancellationToken, ValueTask<bool>> canUnblindAsync,
            CancellationToken cancellationToken = default)
        {
            counters.RecordFindUnverifiedSdvTargets();
            return inner.FindUnverifiedSdvTargetsAsync(tenant, observations, canUnblindAsync, cancellationToken);
        }
    }

    private sealed class CountingKernelReadAccess(IKernelReadAccess inner, ObservationProbeCounters counters) : IKernelReadAccess
    {
        public ValueTask<bool> CanSeeAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            inner.CanSeeAsync(tenant, node, cancellationToken);

        public ValueTask<MaskReason?> MaskForAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            inner.MaskForAsync(tenant, node, cancellationToken);

        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken) =>
            inner.MaskAsync(tenant, nodes, cancellationToken);

        public ValueTask<IReadOnlySet<NodeRef>> FilterVisibleAsync(
            TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken) =>
            inner.FilterVisibleAsync(tenant, nodes, cancellationToken);

        public ValueTask<bool> CoversScopeAsync(TenantId tenant, NodeRef scope, CancellationToken cancellationToken) =>
            inner.CoversScopeAsync(tenant, scope, cancellationToken);

        public ValueTask<bool> CanExerciseAsync(
            TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken) =>
            inner.CanExerciseAsync(tenant, capability, resource, cancellationToken);

        public ValueTask<bool> CanUnblindAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken)
        {
            counters.RecordCanUnblind();
            return inner.CanUnblindAsync(tenant, enrollment, cancellationToken);
        }

        public ValueTask<IReadOnlySet<EnrollmentId>> CanUnblindManyAsync(
            TenantId tenant, IReadOnlyCollection<EnrollmentId> enrollments, CancellationToken cancellationToken) =>
            inner.CanUnblindManyAsync(tenant, enrollments, cancellationToken);
    }

    private sealed class SqlCommandCollector : ILoggerProvider
    {
        private readonly AsyncLocal<ConcurrentQueue<string>?> _activeCommands = new();

        public void Reset() => _activeCommands.Value = new ConcurrentQueue<string>();

        public IReadOnlyList<string> Snapshot() => _activeCommands.Value is { } active ? [.. active] : [];

        public ILogger CreateLogger(string categoryName) =>
            string.Equals(categoryName, DbLoggerCategory.Database.Command.Name, StringComparison.Ordinal)
                ? new CommandLogger(_activeCommands)
                : NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class CommandLogger(AsyncLocal<ConcurrentQueue<string>?> activeCommands) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (activeCommands.Value is not { } commands
                    || eventId.Id != 20101
                    || state is not IReadOnlyList<KeyValuePair<string, object?>> fields)
                {
                    return;
                }

                foreach (var (key, value) in fields)
                {
                    if (string.Equals(key, "commandText", StringComparison.Ordinal) && value is string sql)
                    {
                        commands.Enqueue(sql);
                        return;
                    }
                }
            }
        }

        private sealed class NullLogger : ILogger
        {
            public static readonly NullLogger Instance = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => false;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
            }
        }
    }
}
