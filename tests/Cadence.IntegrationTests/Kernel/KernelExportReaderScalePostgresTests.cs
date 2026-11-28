using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Verifies bounded PostgreSQL assertion queries in <see cref="KernelExportReader.ReadEnrollmentCasebookAsync"/> and
/// linear Phase III export scaling at 25,000 observations, 25,000 assertions, and 2,500 active locks under Row-Level Security.
/// </summary>
[Collection(KernelPerformanceQuotaCollection.Name)]
[Trait("Category", "Integration")]
public sealed class KernelExportReaderScalePostgresTests(KernelPostgresFixture fixture, ITestOutputHelper output)
    : IClassFixture<KernelPostgresFixture>
{
    private readonly KernelCollectionScenario _scenario = new(fixture);

    [Fact(DisplayName = "CasebookExport_ExecutesBoundedAssertionQueries_AndReadsZeroUnrelatedEnrollmentAssertions")]
    public async Task CasebookExportExecutesBoundedAssertionQueriesAndReadsZeroUnrelatedEnrollmentAssertions()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var targetForm = await _scenario.NewFormAsync(ct);
        var tenant = targetForm.Subject.Tenant;
        var study = targetForm.Subject.Study;
        var siteA = targetForm.Subject.StudySite;
        var siteB = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);

        // Save values, verify diastolic, raise & answer query (plus retract an initial answer) on targetForm.
        var savedTarget = await _scenario.Protocol.OkAsync(
            KernelCollectionScenario.Save(
                targetForm,
                KernelCollectionScenario.Value(KernelCollectionScenario.Systolic, KernelCollectionScenario.Mmhg(124)),
                KernelCollectionScenario.Value(KernelCollectionScenario.Diastolic, KernelCollectionScenario.Mmhg(82))),
            ct);
        var targetSbpObs = savedTarget.Values.Single(v => v.ItemId == KernelCollectionScenario.Systolic).ObservationId;
        var targetDbpObs = savedTarget.Values.Single(v => v.ItemId == KernelCollectionScenario.Diastolic).ObservationId;

        await _scenario.Protocol.OkAsync(
            new VerifyObservation(tenant, AssertionId.New(), targetForm.Subject.Enrollment, targetDbpObs, 1, KernelProtocolScenario.Key(), "SDV complete"),
            ct);

        var targetQuery = await _scenario.Protocol.OkAsync(
            new RaiseQuery(tenant, AssertionId.New(), targetForm.Subject.Enrollment, targetSbpObs, "Verify sitting rest period", KernelProtocolScenario.Key(), "Query opened"),
            ct);
        var retractedAnswer = await _scenario.Protocol.OkAsync(
            new AnswerQuery(tenant, AssertionId.New(), targetForm.Subject.Enrollment, targetQuery.AssertionId, "3 minutes rest", KernelProtocolScenario.Key(), "Initial answer"),
            ct);
        await _scenario.Protocol.OkAsync(
            new RetractAssertion(tenant, AssertionId.New(), targetForm.Subject.Enrollment, retractedAnswer.AssertionId, KernelProtocolScenario.Key(), "Superseded answer"),
            ct);
        var activeAnswer = await _scenario.Protocol.OkAsync(
            new AnswerQuery(tenant, AssertionId.New(), targetForm.Subject.Enrollment, targetQuery.AssertionId, "5 minutes seated rest documented", KernelProtocolScenario.Key(), "Corrected answer"),
            ct);

        // Quiet enrollment with zero queries (verifies follow-up QueryAnswered query is skipped when queryIds is empty).
        var quietSubject = new KernelObservationScenario.Subject(tenant, study, siteA, await _scenario.Observations.ScreenAsync(tenant, siteA, ct));
        var quietOpened = await _scenario.Protocol.OkAsync(KernelCollectionScenario.Open(quietSubject, CollectionInstanceId.New(), _scenario.Tick()), ct);
        var quietForm = new KernelCollectionScenario.Form(quietSubject, targetForm.Version, quietOpened.CollectionInstanceId);
        await _scenario.Protocol.OkAsync(
            KernelCollectionScenario.Save(
                quietForm,
                KernelCollectionScenario.Value(KernelCollectionScenario.Systolic, KernelCollectionScenario.Mmhg(116)),
                KernelCollectionScenario.Value(KernelCollectionScenario.Diastolic, KernelCollectionScenario.Mmhg(76))),
            ct);

        // Unrelated enrollments across both sites with observations, queries, answers, and retractions.
        var unrelatedAssertionIds = new HashSet<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var site = i % 2 == 0 ? siteA : siteB;
            var otherSubject = new KernelObservationScenario.Subject(tenant, study, site, await _scenario.Observations.ScreenAsync(tenant, site, ct));
            var otherOpened = await _scenario.Protocol.OkAsync(KernelCollectionScenario.Open(otherSubject, CollectionInstanceId.New(), _scenario.Tick()), ct);
            var otherForm = new KernelCollectionScenario.Form(otherSubject, targetForm.Version, otherOpened.CollectionInstanceId);
            var otherSaved = await _scenario.Protocol.OkAsync(
                KernelCollectionScenario.Save(
                    otherForm,
                    KernelCollectionScenario.Value(KernelCollectionScenario.Systolic, KernelCollectionScenario.Mmhg(130 + i)),
                    KernelCollectionScenario.Value(KernelCollectionScenario.Diastolic, KernelCollectionScenario.Mmhg(84 + i))),
                ct);
            var otherSbp = otherSaved.Values.Single(v => v.ItemId == KernelCollectionScenario.Systolic).ObservationId;
            var otherDbp = otherSaved.Values.Single(v => v.ItemId == KernelCollectionScenario.Diastolic).ObservationId;

            var ver = await _scenario.Protocol.OkAsync(
                new VerifyObservation(tenant, AssertionId.New(), otherSubject.Enrollment, otherDbp, 1, KernelProtocolScenario.Key(), "Source data verified"),
                ct);
            var qry = await _scenario.Protocol.OkAsync(
                new RaiseQuery(tenant, AssertionId.New(), otherSubject.Enrollment, otherSbp, $"Other query {i}", KernelProtocolScenario.Key(), "Other query"),
                ct);
            var ans = await _scenario.Protocol.OkAsync(
                new AnswerQuery(tenant, AssertionId.New(), otherSubject.Enrollment, qry.AssertionId, $"Other answer {i}", KernelProtocolScenario.Key(), "Other answer"),
                ct);
            unrelatedAssertionIds.Add(ver.AssertionId.Value);
            unrelatedAssertionIds.Add(qry.AssertionId.Value);
            unrelatedAssertionIds.Add(ans.AssertionId.Value);
        }

        // Apply a Frozen lock on siteA so covering-scope lock lookup is also exercised.
        var siteFreezeId = AssertionId.New();
        await _scenario.Protocol.OkAsync(
            new ApplyLock(
                tenant,
                siteFreezeId,
                study,
                NodeRef.For(siteA),
                LockKind.Frozen,
                AttestationId.New(),
                KernelProtocolScenario.Key(),
                "Site A interim freeze",
                AttestationMeaning.Approved,
                _scenario.Tick(),
                "pwd+otp"),
            ct);

        await using (var adminConn = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await adminConn.OpenAsync(ct);
            await using var analyze = new NpgsqlCommand("ANALYZE kernel.assertions", adminConn);
            await analyze.ExecuteNonQueryAsync(ct);
        }

        var cut = _scenario.Tick();
        var interceptor = new AssertionSqlRecorder();
        var instrumentedReader = CreateInstrumentedReader(interceptor);

        var casebook = await instrumentedReader.ReadEnrollmentCasebookAsync(
            tenant, targetForm.Subject.Enrollment, cut, cut, ct);
        casebook.Should().NotBeNull();

        // 1. Bounded SQL queries: enrollment_id query, covering lock target_id/kind query, and targeted QueryAnswered follow-up.
        interceptor.AssertionSelectQueries.Should().HaveCount(3);
        interceptor.AssertionSelectQueries.Should().AllSatisfy(sql =>
            sql.Should().NotContain("OR a.target_kind = 'assertion'", "study-wide target_kind = 'assertion' scans must not execute"));
        interceptor.AssertionSelectQueries[0].Should().Contain("a.enrollment_id =");
        interceptor.AssertionSelectQueries[1].Should().Contain("a.target_id =").And.Contain("a.kind IN");
        interceptor.AssertionSelectQueries[2].Should().Contain("a.target_id =").And.Contain("a.kind = 'QueryAnswered'");
        interceptor.MaterializedAssertionIds.Should().NotContain(
            id => unrelatedAssertionIds.Contains(id),
            "ReadEnrollmentCasebookAsync must read zero assertions belonging to unrelated enrollments");
        interceptor.MaterializedAssertionIds.Should().Contain(targetQuery.AssertionId.Value);
        interceptor.MaterializedAssertionIds.Should().Contain(activeAnswer.AssertionId.Value);
        interceptor.MaterializedAssertionIds.Should().Contain(retractedAnswer.AssertionId.Value);
        interceptor.MaterializedAssertionIds.Should().Contain(siteFreezeId.Value);

        // 2. Quiet enrollment with no Queried assertions skips the 3rd QueryAnswered follow-up read completely.
        interceptor.Reset();
        var quietCasebook = await instrumentedReader.ReadEnrollmentCasebookAsync(
            tenant, quietSubject.Enrollment, cut, cut, ct);
        quietCasebook.Should().NotBeNull();
        interceptor.AssertionSelectQueries.Should().HaveCount(
            2,
            "when an enrollment has zero Queried assertions, no follow-up QueryAnswered query is needed");
        interceptor.MaterializedAssertionIds.Should().NotContain(id => unrelatedAssertionIds.Contains(id));

        // 3. Parity between ReadEnrollmentCasebookAsync and ReadStudySnapshotAsync on targetEnrollment.
        var studySnapshot = await instrumentedReader.ReadStudySnapshotAsync(tenant, study, cut, cut, ct);
        studySnapshot.Should().NotBeNull();
        var expectedStudyObs = studySnapshot!.Observations
            .Where(o => o.EnrollmentId == targetForm.Subject.Enrollment)
            .ToList();
        casebook!.Observations.Should().BeEquivalentTo(expectedStudyObs, opts => opts.WithStrictOrdering());
        casebook.Observations.Should().AllSatisfy(o => o.EffectiveLock.Should().Be(LockKind.Frozen));
        casebook.Assertions.Select(a => a.AssertionId).Should().Contain(
            [targetQuery.AssertionId, retractedAnswer.AssertionId, activeAnswer.AssertionId]);
        casebook.Assertions.Single(a => a.AssertionId == retractedAnswer.AssertionId).RetractedBy.Should().NotBeNull();
    }

    [Fact(DisplayName = "Benchmark_PhaseThreeScale_25kObservations25kAssertions2500Locks_CompletesLinearlyUnderTimeout", Timeout = 120_000)]
    public async Task BenchmarkPhaseThreeScale25kObservations25kAssertions2500LocksCompletesLinearlyUnderTimeout()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        const int siteCount = 5;
        const int enrollmentCount = 100;
        const int collectionsCount = 5_000;
        const int itemsPerCollection = 5;
        const int totalObservations = collectionsCount * itemsPerCollection; // 25,000
        const int activeLocksCount = 2_500;
        const int verifiedCount = 10_000;
        const int queriedCount = 5_000;
        const int answeredCount = 5_000;
        const int retractionCount = 2_500;

        var (tenant, study) = await _scenario.Protocol.NewStudyAsync(ct);

        var studySites = new List<StudySiteId>(siteCount);
        for (var s = 0; s < siteCount; s++)
        {
            studySites.Add(await _scenario.Observations.AddStudySiteAsync(tenant, study, ct));
        }

        // Define 5 concepts (4 clinical vitals/labs + 1 blinded investigational treatment kit concept).
        var conceptRefs = new ConceptRef[itemsPerCollection];
        for (var c = 0; c < itemsPerCollection - 1; c++)
        {
            conceptRefs[c] = await _scenario.Protocol.OkAsync(
                new DefineConcept(
                    tenant,
                    ConceptId.New(),
                    ConceptOrigin.Sponsor,
                    null,
                    ConceptKind.BiomedicalConcept,
                    $"Clinical Measure {c + 1}",
                    $"Phase III clinical measure {c + 1}.",
                    KernelProtocolScenario.Key(),
                    "Benchmark concept"),
                ct);
        }

        var trtConceptRef = await _scenario.Protocol.OkAsync(
            new DefineConcept(
                tenant,
                ConceptId.New(),
                ConceptOrigin.Sponsor,
                null,
                ConceptKind.BiomedicalConcept,
                "Investigational Treatment Kit",
                "Blinded investigational drug kit identifier.",
                KernelProtocolScenario.Key(),
                "Benchmark blinded concept"),
            ct);
        conceptRefs[itemsPerCollection - 1] = trtConceptRef;

        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            Interventions = [new Intervention("int.blinded", "Blinded Study Drug", trtConceptRef)],
            PlannedActivities =
            [
                new PlannedActivity("act.visit1", "Baseline Visit", "ep.treatment", [ActivityLocation.Site], ["cs.panel", "cs.randomization"], new ActivityTiming(1, 0, 2)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.panel",
                    "Clinical Assessment Panel",
                    [
                        new CollectionItem("panel.item.1", conceptRefs[0], "mm[Hg]", Required: true),
                        new CollectionItem("panel.item.2", conceptRefs[1], "mm[Hg]", Required: true),
                        new CollectionItem("panel.item.3", conceptRefs[2], "mm[Hg]", Required: true),
                        new CollectionItem("panel.item.4", conceptRefs[3], "mm[Hg]", Required: true),
                        new CollectionItem("panel.item.5", trtConceptRef, ValueKind: ObservationValueKind.Text),
                    ]),
                new CollectionSpec(
                    "cs.randomization",
                    "Randomization Allocation Log",
                    [
                        new CollectionItem("rand.item.1", conceptRefs[0], "mm[Hg]"),
                        new CollectionItem("rand.item.2", conceptRefs[1], "mm[Hg]"),
                        new CollectionItem("rand.item.3", conceptRefs[2], "mm[Hg]"),
                        new CollectionItem("rand.item.4", conceptRefs[3], "mm[Hg]"),
                        new CollectionItem("rand.item.5", trtConceptRef, ValueKind: ObservationValueKind.Text),
                    ]),
            ],
        };

        var protocolVersionId = ProtocolVersionId.New();
        var draft = await _scenario.Protocol.CreateDraftAsync(tenant, study, protocolVersionId, ct, ProtocolDesignDocument.Write(design));
        draft.IsSuccess.Should().BeTrue(draft.Error?.Message);
        var inReview = await _scenario.Protocol.OkAsync(
            new SubmitProtocolVersionForReview(tenant, protocolVersionId, KernelProtocolScenario.Key(), "Ready for review"), ct);
        var protoSig = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _scenario.Protocol.OkAsync(
            new ApproveProtocolVersion(tenant, protocolVersionId, protoSig, KernelProtocolScenario.Key(), "Approved protocol"), ct);

        var effectivityId = EffectivityId.New();
        await _scenario.Protocol.OkAsync(
            new DeclareEffectivity(tenant, effectivityId, protocolVersionId, EffectivityScope.Study(), _scenario.Tick(), KernelProtocolScenario.Key(), "Study-wide"), ct);
        var ethicsBytes = System.Text.Encoding.UTF8.GetBytes("benchmark-ethics-approval");
        var ethicsVersionId = ArtifactVersionId.New();
        await _scenario.Protocol.OkAsync(
            new RegisterArtifact(
                tenant,
                ArtifactId.New(),
                ethicsVersionId,
                ArtifactKind.EthicsApproval,
                study,
                "Benchmark Ethics Approval",
                new ArtifactContent(ContentHash.OfBytes(ethicsBytes), ethicsBytes.Length, "application/pdf", "blob://bench/ethics"),
                KernelProtocolScenario.Key(),
                "Ethics filed"),
            ct);
        await _scenario.Protocol.OkAsync(
            new ChangeEffectivityStatus(tenant, effectivityId, EffectivityStatus.Effective, _scenario.Tick(), [ethicsVersionId], KernelProtocolScenario.Key(), "Made effective"),
            ct);

        var enrollments = new List<(EnrollmentId EnrollmentId, ParticipantId ParticipantId, StudySiteId StudySiteId)>(enrollmentCount);
        for (var e = 0; e < enrollmentCount; e++)
        {
            var site = studySites[e % siteCount];
            var enrollmentId = await _scenario.Observations.ScreenAsync(tenant, site, ct);
            enrollments.Add((enrollmentId, ParticipantId.Empty, site));
        }

        // Read back participant IDs assigned by ScreenAsync.
        await using (var db = fixture.CreateContext(tenant))
        {
            var participantMap = await db.Enrollments.AsNoTracking()
                .Where(e => e.StudyId == study)
                .ToDictionaryAsync(e => e.Id, e => e.ParticipantId, ct);
            for (var e = 0; e < enrollments.Count; e++)
            {
                var entry = enrollments[e];
                enrollments[e] = (entry.EnrollmentId, participantMap[entry.EnrollmentId], entry.StudySiteId);
            }
        }

        var baseTime = _scenario.Tick();
        var seedWatch = Stopwatch.StartNew();
        await using (var conn = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var enrollmentUuids = enrollments.Select(e => e.EnrollmentId.Value).ToArray();
            var participantUuids = enrollments.Select(e => e.ParticipantId.Value).ToArray();
            var siteUuids = enrollments.Select(e => e.StudySiteId.Value).ToArray();
            var conceptUuids = conceptRefs.Select(c => c.ConceptId.Value).ToArray();

            await using (var cmd = new NpgsqlCommand(
                """
                WITH e_map AS (
                    SELECT idx,
                           (@enrollment_ids::uuid[])[idx] AS enrollment_id,
                           (@participant_ids::uuid[])[idx] AS participant_id,
                           (@site_ids::uuid[])[idx] AS study_site_id
                    FROM generate_series(1, @enrollment_count) AS idx
                ),
                ins_collections AS (
                    INSERT INTO kernel.collection_instances
                        (id, tenant_id, study_id, study_site_id, enrollment_id, participant_id, protocol_version_id,
                         collection_spec_id, planned_activity_id, collected_at, opened_at, opened_by, last_saved_at, saves,
                         created_at, created_by, reason_for_change, version_index)
                    SELECT md5('bench-coll:' || c)::uuid,
                           @tenant_id,
                           @study_id,
                           em.study_site_id,
                           em.enrollment_id,
                           em.participant_id,
                           @protocol_version_id,
                           CASE WHEN c % 10 = 0 THEN 'cs.randomization' ELSE 'cs.panel' END,
                           'act.visit1',
                           @base_time + make_interval(secs => c),
                           @base_time + make_interval(secs => c),
                           'crc@bench.test',
                           @base_time + make_interval(secs => c),
                           1,
                           (@base_time + make_interval(secs => c)) AT TIME ZONE 'UTC',
                           'crc@bench.test',
                           'Benchmark collection open',
                           1
                    FROM generate_series(1, @collections_count) AS c
                    JOIN e_map em ON em.idx = ((c - 1) % @enrollment_count) + 1
                    RETURNING id
                )
                SELECT count(*) FROM ins_collections;
                """,
                conn,
                tx)
            { CommandTimeout = 0 })
            {
                cmd.Parameters.AddWithValue("enrollment_ids", enrollmentUuids);
                cmd.Parameters.AddWithValue("participant_ids", participantUuids);
                cmd.Parameters.AddWithValue("site_ids", siteUuids);
                cmd.Parameters.AddWithValue("enrollment_count", enrollmentCount);
                cmd.Parameters.AddWithValue("collections_count", collectionsCount);
                cmd.Parameters.AddWithValue("tenant_id", tenant.Value);
                cmd.Parameters.AddWithValue("study_id", study.Value);
                cmd.Parameters.AddWithValue("protocol_version_id", protocolVersionId.Value);
                cmd.Parameters.AddWithValue("base_time", baseTime);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await using (var obsCmd = new NpgsqlCommand(
                """
                WITH e_map AS (
                    SELECT idx,
                           (@enrollment_ids::uuid[])[idx] AS enrollment_id,
                           (@participant_ids::uuid[])[idx] AS participant_id
                    FROM generate_series(1, @enrollment_count) AS idx
                ),
                obs_gen AS (
                    SELECT o,
                           ((o - 1) / 5) + 1 AS coll_idx,
                           ((o - 1) % 5) + 1 AS item_idx,
                           ((((o - 1) / 5)) % @enrollment_count) + 1 AS enr_idx,
                           md5('bench-obs:' || o)::uuid AS obs_id,
                           CASE
                               WHEN o % 100 = 0 THEN '{"type":"missing","reason":"Masked"}'::jsonb
                               WHEN ((o - 1) % 5) = 4 THEN jsonb_build_object('type', 'text', 'value', 'KIT-' || o)
                               ELSE jsonb_build_object('type', 'quantity', 'value', 110 + (o % 35), 'unit', 'mm[Hg]')
                           END AS val_json,
                           @base_time + make_interval(secs => ((o - 1) / 5) + 1) AS ts
                    FROM generate_series(1, @obs_count) AS o
                ),
                ins_obs AS (
                    INSERT INTO kernel.observations
                        (id, tenant_id, study_id, enrollment_id, participant_id, concept_id, concept_version,
                         observed_at, value, revision, created_at, created_by, reason_for_change, version_index)
                    SELECT g.obs_id,
                           @tenant_id,
                           @study_id,
                           em.enrollment_id,
                           em.participant_id,
                           (@concept_ids::uuid[])[g.item_idx],
                           1,
                           g.ts,
                           g.val_json,
                           1,
                           g.ts AT TIME ZONE 'UTC',
                           'crc@bench.test',
                           'Initial entry',
                           1
                    FROM obs_gen g
                    JOIN e_map em ON em.idx = g.enr_idx
                ),
                ins_revs AS (
                    INSERT INTO kernel.observation_revisions
                        (id, tenant_id, observation_id, number, value, reason_for_change, recorded_by, recorded_at)
                    SELECT md5('bench-rev:' || g.o)::uuid,
                           @tenant_id,
                           g.obs_id,
                           1,
                           g.val_json,
                           'Initial entry',
                           'crc@bench.test',
                           g.ts
                    FROM obs_gen g
                )
                INSERT INTO kernel.collection_instance_members
                    (id, tenant_id, collection_instance_id, item_id, observation_id, joined_at)
                SELECT md5('bench-mem:' || g.o)::uuid,
                       @tenant_id,
                       md5('bench-coll:' || g.coll_idx)::uuid,
                       CASE WHEN (g.coll_idx % 10) = 0 THEN 'rand.item.' || g.item_idx ELSE 'panel.item.' || g.item_idx END,
                       g.obs_id,
                       g.ts
                FROM obs_gen g;
                """,
                conn,
                tx)
            { CommandTimeout = 0 })
            {
                obsCmd.Parameters.AddWithValue("enrollment_ids", enrollmentUuids);
                obsCmd.Parameters.AddWithValue("participant_ids", participantUuids);
                obsCmd.Parameters.AddWithValue("concept_ids", conceptUuids);
                obsCmd.Parameters.AddWithValue("enrollment_count", enrollmentCount);
                obsCmd.Parameters.AddWithValue("obs_count", totalObservations);
                obsCmd.Parameters.AddWithValue("tenant_id", tenant.Value);
                obsCmd.Parameters.AddWithValue("study_id", study.Value);
                obsCmd.Parameters.AddWithValue("base_time", baseTime);
                await obsCmd.ExecuteNonQueryAsync(ct);
            }

            // Seed 2,500 lock attestations + 25,000 assertions (2,500 active locks, 10,000 Verified, 5,000 Queried, 5,000 QueryAnswered, 2,500 retractions).
            await using (var assertCmd = new NpgsqlCommand(
                """
                WITH ins_att AS (
                    INSERT INTO kernel.attestations
                        (id, tenant_id, actor_id, meaning, target_kind, target_id, target_content_hash,
                         authenticated_at, method, recorded_at, reason, created_at, created_by, reason_for_change, version_index)
                    SELECT md5('bench-att:' || k)::uuid,
                           @tenant_id,
                           'dm@bench.test',
                           'Approved',
                           'assertion',
                           md5('bench-lock:' || k)::uuid,
                           'sha256:' || md5('bench-hash-a:' || k) || md5('bench-hash-b:' || k),
                           @base_time + make_interval(secs => 6000 + k),
                           'pwd+otp',
                           @base_time + make_interval(secs => 6000 + k),
                           'Benchmark lock signature',
                           (@base_time + make_interval(secs => 6000 + k)) AT TIME ZONE 'UTC',
                           'dm@bench.test',
                           'Benchmark lock signature',
                           1
                    FROM generate_series(1, @locks_count) AS k
                ),
                ins_locks AS (
                    INSERT INTO kernel.assertions
                        (id, tenant_id, study_id, enrollment_id, kind, target_kind, target_id, target_revision,
                         text, retracts_id, attestation_id, asserted_by, asserted_at, created_at, created_by, reason_for_change, version_index)
                    SELECT md5('bench-lock:' || k)::uuid,
                           @tenant_id,
                           @study_id,
                           (@enrollment_ids::uuid[])[((k - 1) % @enrollment_count) + 1],
                           CASE WHEN k % 3 = 0 THEN 'Locked' ELSE 'Frozen' END,
                           CASE WHEN k <= 2000 THEN 'collection_instance' ELSE 'observation' END,
                           CASE WHEN k <= 2000 THEN md5('bench-coll:' || k)::uuid ELSE md5('bench-obs:' || k)::uuid END,
                           NULL,
                           NULL,
                           NULL,
                           md5('bench-att:' || k)::uuid,
                           'dm@bench.test',
                           @base_time + make_interval(secs => 6000 + k),
                           (@base_time + make_interval(secs => 6000 + k)) AT TIME ZONE 'UTC',
                           'dm@bench.test',
                           'Benchmark lock apply',
                           1
                    FROM generate_series(1, @locks_count) AS k
                ),
                ins_verified AS (
                    INSERT INTO kernel.assertions
                        (id, tenant_id, study_id, enrollment_id, kind, target_kind, target_id, target_revision,
                         text, retracts_id, attestation_id, asserted_by, asserted_at, created_at, created_by, reason_for_change, version_index)
                    SELECT md5('bench-ver:' || v)::uuid,
                           @tenant_id,
                           @study_id,
                           (@enrollment_ids::uuid[])[((((v - 1) / 5)) % @enrollment_count) + 1],
                           'Verified',
                           'observation',
                           md5('bench-obs:' || v)::uuid,
                           1,
                           NULL,
                           NULL,
                           NULL,
                           'cra@bench.test',
                           @base_time + make_interval(secs => 9000 + v),
                           (@base_time + make_interval(secs => 9000 + v)) AT TIME ZONE 'UTC',
                           'cra@bench.test',
                           'Benchmark SDV',
                           1
                    FROM generate_series(1, @verified_count) AS v
                ),
                ins_queries AS (
                    INSERT INTO kernel.assertions
                        (id, tenant_id, study_id, enrollment_id, kind, target_kind, target_id, target_revision,
                         text, retracts_id, attestation_id, asserted_by, asserted_at, created_at, created_by, reason_for_change, version_index)
                    SELECT md5('bench-qry:' || q)::uuid,
                           @tenant_id,
                           @study_id,
                           (@enrollment_ids::uuid[])[((((q + 10000 - 1) / 5)) % @enrollment_count) + 1],
                           'Queried',
                           'observation',
                           md5('bench-obs:' || (q + 10000))::uuid,
                           1,
                           'Confirm source value #' || q,
                           NULL,
                           NULL,
                           'dm@bench.test',
                           @base_time + make_interval(secs => 20000 + q),
                           (@base_time + make_interval(secs => 20000 + q)) AT TIME ZONE 'UTC',
                           'dm@bench.test',
                           'Benchmark query',
                           1
                    FROM generate_series(1, @queried_count) AS q
                ),
                ins_answers AS (
                    INSERT INTO kernel.assertions
                        (id, tenant_id, study_id, enrollment_id, kind, target_kind, target_id, target_revision,
                         text, retracts_id, attestation_id, asserted_by, asserted_at, created_at, created_by, reason_for_change, version_index)
                    SELECT md5('bench-ans:' || q)::uuid,
                           @tenant_id,
                           @study_id,
                           (@enrollment_ids::uuid[])[((((q + 10000 - 1) / 5)) % @enrollment_count) + 1],
                           'QueryAnswered',
                           'assertion',
                           md5('bench-qry:' || q)::uuid,
                           1,
                           'Confirmed against chart #' || q,
                           NULL,
                           NULL,
                           'crc@bench.test',
                           @base_time + make_interval(secs => 26000 + q),
                           (@base_time + make_interval(secs => 26000 + q)) AT TIME ZONE 'UTC',
                           'crc@bench.test',
                           'Benchmark query answer',
                           1
                    FROM generate_series(1, @answered_count) AS q
                )
                INSERT INTO kernel.assertions
                    (id, tenant_id, study_id, enrollment_id, kind, target_kind, target_id, target_revision,
                     text, retracts_id, attestation_id, asserted_by, asserted_at, created_at, created_by, reason_for_change, version_index)
                SELECT md5('bench-ret:' || r)::uuid,
                       @tenant_id,
                       @study_id,
                       (@enrollment_ids::uuid[])[((((r + 10000 - 1) / 5)) % @enrollment_count) + 1],
                       'Queried',
                       'observation',
                       md5('bench-obs:' || (r + 10000))::uuid,
                       1,
                       NULL,
                       md5('bench-qry:' || r)::uuid,
                       NULL,
                       'dm@bench.test',
                       @base_time + make_interval(secs => 32000 + r),
                       (@base_time + make_interval(secs => 32000 + r)) AT TIME ZONE 'UTC',
                       'dm@bench.test',
                       'Benchmark query closed',
                       1
                FROM generate_series(1, @retraction_count) AS r;
                """,
                conn,
                tx)
            { CommandTimeout = 0 })
            {
                assertCmd.Parameters.AddWithValue("tenant_id", tenant.Value);
                assertCmd.Parameters.AddWithValue("study_id", study.Value);
                assertCmd.Parameters.AddWithValue("enrollment_ids", enrollmentUuids);
                assertCmd.Parameters.AddWithValue("enrollment_count", enrollmentCount);
                assertCmd.Parameters.AddWithValue("locks_count", activeLocksCount);
                assertCmd.Parameters.AddWithValue("verified_count", verifiedCount);
                assertCmd.Parameters.AddWithValue("queried_count", queriedCount);
                assertCmd.Parameters.AddWithValue("answered_count", answeredCount);
                assertCmd.Parameters.AddWithValue("retraction_count", retractionCount);
                assertCmd.Parameters.AddWithValue("base_time", baseTime);
                await assertCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);

            await using var analyzeCmd = new NpgsqlCommand(
                "ANALYZE kernel.collection_instances, kernel.collection_instance_members, kernel.observations, kernel.observation_revisions, kernel.attestations, kernel.assertions;",
                conn)
            { CommandTimeout = 0 };
            await analyzeCmd.ExecuteNonQueryAsync(ct);
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Seeded 25,000 observations, 5,000 collections, 25,000 assertions, and 2,500 active locks in {seedWatch.Elapsed.TotalMilliseconds:F0} ms."));

        var cut = baseTime.AddHours(12);
        var reader = new KernelExportReader(fixture.TenantContexts);

        // 1. End-to-end study snapshot read under Row-Level Security.
        var studyWatch = Stopwatch.StartNew();
        var snapshot = await reader.ReadStudySnapshotAsync(tenant, study, cut, cut, ct);
        studyWatch.Stop();

        snapshot.Should().NotBeNull();
        snapshot!.Observations.Should().HaveCount(totalObservations);
        snapshot.Collections.Should().HaveCount(collectionsCount);
        snapshot.Locks.Count(l => !l.Released).Should().Be(activeLocksCount);
        snapshot.Observations.Count(o => o.IsBlindedTreatment).Should().BeGreaterThan(5_000);
        snapshot.Observations.Count(o => o.EffectiveLock is not null).Should().BeGreaterThan(9_000);
        snapshot.Observations.Sum(o => o.Assertions.Count).Should().Be(verifiedCount + queriedCount + answeredCount);
        studyWatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(20),
            "25k-row Phase III study snapshot export must complete well under the 30s gateway timeout");

        // 2. End-to-end single-enrollment casebook read inside the 25k-row study, verifying bounded assertion reads.
        var interceptor = new AssertionSqlRecorder();
        var instrumentedReader = CreateInstrumentedReader(interceptor);
        var casebookWatch = Stopwatch.StartNew();
        var targetEnrollment = enrollments[0].EnrollmentId;
        var casebook = await instrumentedReader.ReadEnrollmentCasebookAsync(tenant, targetEnrollment, cut, cut, ct);
        casebookWatch.Stop();

        casebook.Should().NotBeNull();
        casebook!.Observations.Should().HaveCount(totalObservations / enrollmentCount);
        interceptor.AssertionSelectQueries.Should().HaveCountLessThanOrEqualTo(3);
        interceptor.MaterializedAssertionIds.Count.Should().BeLessThan(
            1_000,
            "bounded casebook queries must not materialize the 25,000 study-wide assertions");
        casebookWatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));

        // 3. Pure in-memory AssembleObservationsAndCollections + BuildBlindedAuditKeys benchmark (median of 3 runs).
        List<CollectionInstance> collectionsRaw;
        List<Observation> observationsRaw;
        List<Assertion> assertionsRaw;
        Dictionary<ConceptId, Concept> conceptsById;
        await using (var db = fixture.CreateContext(tenant))
        {
            collectionsRaw = await db.CollectionInstances.AsNoTracking()
                .Include(c => c.Members)
                .Where(c => c.StudyId == study)
                .ToListAsync(ct);
            observationsRaw = await db.Observations.AsNoTracking()
                .Include(o => o.Revisions)
                .Where(o => o.StudyId == study)
                .ToListAsync(ct);
            assertionsRaw = await db.Assertions.AsNoTracking()
                .Where(a => a.StudyId == study)
                .OrderBy(a => a.AssertedAt)
                .ThenBy(a => a.Id)
                .ToListAsync(ct);
            conceptsById = await db.Concepts.AsNoTracking()
                .Include(c => c.Revisions)
                .ToDictionaryAsync(c => c.Id, ct);
        }

        var studySiteByEnrollment = enrollments.ToDictionary(e => e.EnrollmentId, e => e.StudySiteId);
        var designs = snapshot.ProtocolVersions.Where(p => p.Design is not null).Select(p => p.Design!).ToList();

        // Warmup JIT
        _ = KernelExportReader.AssembleObservationsAndCollections(
            snapshot.ProtocolVersions, designs, conceptsById, studySiteByEnrollment,
            collectionsRaw, observationsRaw, assertionsRaw, snapshot.Locks, cut);

        var assembleTimingsMs = new List<double>(3);
        long allocatedBytesSample = 0;
        for (var iter = 0; iter < 3; iter++)
        {
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            var (colls, obs) = KernelExportReader.AssembleObservationsAndCollections(
                snapshot.ProtocolVersions, designs, conceptsById, studySiteByEnrollment,
                collectionsRaw, observationsRaw, assertionsRaw, snapshot.Locks, cut);
            var blindedKeys = KernelExportReader.BuildBlindedAuditKeys(
                colls, obs, snapshot.Locks, snapshot.Attestations, designs);
            sw.Stop();
            var afterBytes = GC.GetAllocatedBytesForCurrentThread();

            colls.Should().HaveCount(collectionsCount);
            obs.Should().HaveCount(totalObservations);
            blindedKeys.Should().NotBeEmpty();

            assembleTimingsMs.Add(sw.Elapsed.TotalMilliseconds);
            if (iter == 1)
            {
                allocatedBytesSample = afterBytes - beforeBytes;
            }
        }

        assembleTimingsMs.Sort();
        var medianMs = assembleTimingsMs[1];
        var allocatedMb = allocatedBytesSample / (1024.0 * 1024.0);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"25k benchmark results: ReadStudySnapshotAsync={studyWatch.Elapsed.TotalMilliseconds:F1} ms, ReadEnrollmentCasebookAsync={casebookWatch.Elapsed.TotalMilliseconds:F1} ms (materialized {interceptor.MaterializedAssertionIds.Count} assertions), InMemoryAssemble+BlindedAudit median={medianMs:F1} ms ({allocatedMb:F2} MB allocated)."));

        medianMs.Should().BeLessThan(5_000.0, "indexed in-memory snapshot assembly at 25k scale should run in well under 5 seconds");
    }

    private KernelExportReader CreateInstrumentedReader(AssertionSqlRecorder interceptor)
    {
        var connectionString = fixture.ConnectionString ?? throw new InvalidOperationException("PostgreSQL connection unavailable.");
        var factory = new InterceptedKernelDbContextFactory(connectionString, interceptor);
        return new KernelExportReader(new KernelTenantDbContextFactory(factory));
    }

    private sealed class InterceptedKernelDbContextFactory(string connectionString, IDbCommandInterceptor interceptor)
        : IDbContextFactory<KernelDbContext>
    {
        public KernelDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<KernelDbContext>();
            KernelDatabase.UseKernelNpgsql(options, connectionString);
            options.AddInterceptors(new KernelTenantConnectionInterceptor(), interceptor);
            return new KernelDbContext(options.Options);
        }
    }

    private sealed class AssertionSqlRecorder : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _queries = new();
        private readonly ConcurrentBag<Guid> _assertionIds = [];

        public IReadOnlyList<string> AssertionSelectQueries => [.. _queries];

        public IReadOnlyCollection<Guid> MaterializedAssertionIds => _assertionIds;

        public void Reset()
        {
            _queries.Clear();
            _assertionIds.Clear();
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (IsAssertionsSelect(command.CommandText))
            {
                _queries.Enqueue(command.CommandText);
                return ValueTask.FromResult<DbDataReader>(new RecordingDataReader(result, _assertionIds));
            }

            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        private static bool IsAssertionsSelect(string sql) =>
            sql.Contains("FROM kernel.assertions", StringComparison.Ordinal);
    }

    private sealed class RecordingDataReader(DbDataReader inner, ConcurrentBag<Guid> recordedIds) : DbDataReader
    {
        private int _idOrdinal = -2;

        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;

        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override System.Collections.IEnumerator GetEnumerator() => inner.GetEnumerator();
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) => inner.GetFieldValueAsync<T>(ordinal, cancellationToken);
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => inner.IsDBNullAsync(ordinal, cancellationToken);
        public override bool NextResult() => inner.NextResult();
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => inner.NextResultAsync(cancellationToken);
        public override void Close() => inner.Close();
        public override Task CloseAsync() => inner.CloseAsync();

        public override bool Read()
        {
            var hasRow = inner.Read();
            if (hasRow)
            {
                CaptureId();
            }

            return hasRow;
        }

        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            var hasRow = await inner.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (hasRow)
            {
                CaptureId();
            }

            return hasRow;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }

        private void CaptureId()
        {
            if (_idOrdinal == -2)
            {
                _idOrdinal = -1;
                for (var i = 0; i < inner.FieldCount; i++)
                {
                    if (string.Equals(inner.GetName(i), "id", StringComparison.OrdinalIgnoreCase)
                        && inner.GetFieldType(i) == typeof(Guid))
                    {
                        _idOrdinal = i;
                        break;
                    }
                }
            }

            if (_idOrdinal >= 0 && !inner.IsDBNull(_idOrdinal))
            {
                recordedIds.Add(inner.GetGuid(_idOrdinal));
            }
        }
    }
}
