using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.E2ETests.Journeys;

/// <summary>
/// Clinical journeys across modules in the single host (#5471, ADR-2610, ADR-2601), on PostgreSQL, with gateway-signed
/// callers. Each journey follows one kernel <see cref="ParticipantId"/> from start to end and checks that every write
/// left a kernel audit entry naming the caller who made it. Skipped, not passed, without a database.
/// <para>
/// Today the single host runs the kernel, the Intelligence module and Execution's eCRF forms (#5576); the rest of
/// Execution, eConsent and Safety still run as their own services until they become modules (#5563, #5378). So forms are
/// opened, read and saved over the Execution module's routes, the other EDC steps run on the kernel commands the
/// Execution module will issue (enrollment, queries, SDV and locks, ADR-2620 to ADR-2622), and the steps
/// whose feature does not exist yet are dated TODOs naming the issue that adds it, not stand-ins. A step whose kernel
/// command exists but whose route does not yet goes through <see cref="KernelHostCommandRelay"/>, still signed and
/// still authorized by the host.
/// </para>
/// </summary>
[SuppressMessage(
    "SonarAnalyzer.CSharp",
    "S1135:Track uses of \"TODO\" tags",
    Justification = "#5471 asks for each journey step whose feature does not exist yet to be a dated TODO naming the issue that adds it.")]
public sealed class CrossModuleJourneyTests(KernelHostJourneyFixture fixture) : IClassFixture<KernelHostJourneyFixture>
{
    private const string FormsRoute = "/api/v1/execution/forms";

    /// <summary>The Execution module's form consumer, as the kernel names its service account.</summary>
    private static readonly string FormsConsumer = Actor.ServiceAccount(JourneyStudy.ExecutionFormsAccount).Id;

    /// <summary>
    /// Consent to export: a participant is registered, screened and enrolled at the site, a vital signs form is opened
    /// and its systolic pressure saved, a monitor raises a query that the coordinator answers and the monitor closes,
    /// the monitor verifies the value against source, and the data manager signs and applies the database lock, after
    /// which a correction is refused. The value's lineage names the protocol version that governed it. One participant
    /// throughout, and an audit entry naming the caller for every write.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ConsentToExport_EnrollCaptureQuerySdvLock_OneParticipantAndEveryWriteAuditedAsItsCaller()
    {
        fixture.SkipUnlessPostgres();
        var ct = TestContext.Current.CancellationToken;
        var study = await JourneyStudy.ArrangeAsync(fixture, ct);
        using var crc = fixture.Caller(study.TenantCode, JourneyStudy.Coordinator);
        using var cra = fixture.Caller(study.TenantCode, JourneyStudy.Monitor);
        using var dm = fixture.Caller(study.TenantCode, JourneyStudy.DataManager);
        var written = new List<(string AggregateId, string Caller)>();

        // TODO(2026-10-20): record consent before enrollment once the kernel has computable consent: ConsentStatus with
        // Attestation(Consented) and the ICF artifact, recorded by eConsent as a module (spec §9.4; #4777, eConsent as a
        // module #5378). Until then nothing records or requires consent in the single host, so the journey starts at
        // registration and does not stand consent in with something else.
        var (participant, enrollment) = await EnrollAsync(study, crc, written, ct);

        // Form captured over the Execution module's eCRF routes in the host (#5576).
        var opened = await OpenFormAsync(crc, enrollment, JourneyStudy.VitalsSpec, JourneyStudy.Week12, "Week 12 visit performed", ct);
        opened.ProtocolVersionId.Should().Be(study.ProtocolVersion.Value, "the version effective at the site governs the form (K-TIME-8)");
        var formId = CollectionInstanceId.FromGuid(opened.FormId);
        written.Add((formId.ToString(), JourneyStudy.Coordinator));
        var saved = await SaveFormAsync(crc, opened.FormId, "Entered from the source document", ct,
            (JourneyStudy.Systolic, new { type = "quantity", value = 128, unit = "mm[Hg]" }));
        var observation = ObservationId.FromGuid(saved.Values.Should().ContainSingle().Which.ObservationId);
        written.Add((observation.ToString(), JourneyStudy.Coordinator));
        await AwaitFormConsumerAsync(study, formId, written, ct);
        var form = await ReadFormAsync(crc, opened.FormId, ct);
        form.ParticipantId.Should().Be(participant.Value);
        form.Complete.Should().BeTrue();
        form.Items.Should().ContainSingle().Which.Value.Should().Be(new QuantityValue(128m, "mm[Hg]"));

        // TODO(2026-10-20): the investigator signs the form here, an Attestation over the form instance's member revisions
        // (spec §9.5, ADR-2622). The kernel's Attest command does not accept a CollectionInstance target yet, so there is
        // no form signature to give (#5463).

        // Query raised, answered and closed. TODO(2026-10-20): replace the relay with the query routes (#5461).
        var query = await fixture.OkThroughHostAsync(cra, "Source shows a different reading", w => new RaiseQuery(
            w.Tenant, AssertionId.New(), enrollment.EnrollmentId, observation, "Please confirm the systolic pressure against the source.",
            w.IdempotencyKey, w.ReasonForChange), ct);
        var answer = await fixture.OkThroughHostAsync(crc, "Answering the monitor's query", w => new AnswerQuery(
            w.Tenant, AssertionId.New(), enrollment.EnrollmentId, query.AssertionId, "Confirmed: the source document reads 128.",
            w.IdempotencyKey, w.ReasonForChange), ct);
        var closed = await fixture.OkThroughHostAsync(cra, "Answer accepted; closing the query", w => new RetractAssertion(
            w.Tenant, AssertionId.New(), enrollment.EnrollmentId, query.AssertionId, w.IdempotencyKey, w.ReasonForChange), ct);
        written.AddRange([(query.AssertionId.ToString(), JourneyStudy.Monitor), (answer.AssertionId.ToString(), JourneyStudy.Coordinator), (closed.AssertionId.ToString(), JourneyStudy.Monitor)]);

        // SDV of the exact revision. TODO(2026-10-20): replace the relay with the SDV route (#5465).
        var verified = await fixture.OkThroughHostAsync(cra, "Source data verified", w => new VerifyObservation(
            w.Tenant, AssertionId.New(), enrollment.EnrollmentId, observation, 1, w.IdempotencyKey, w.ReasonForChange), ct);
        verified.Revision.Should().Be(1);
        written.Add((verified.AssertionId.ToString(), JourneyStudy.Monitor));

        // Database lock, signed by the data manager. TODO(2026-10-20): replace the relay with the lock route (#5466) and
        // the signature with signing over HTTP (#5552).
        var lockId = AssertionId.New();
        var studyScope = NodeRef.For(study.Study);
        var lockSignature = await fixture.OkThroughHostAsync(dm, "Signing the database lock", w => new Attest(
            w.Tenant, AttestationId.New(), AttestationMeaning.Approved, LockStatement.Target(lockId, LockStatement.Apply(lockId, LockKind.Locked, studyScope)),
            SystemClock.Instance.UtcNow.AddMinutes(-1), "pwd+otp", w.IdempotencyKey, w.ReasonForChange), ct);
        var locked = await fixture.OkThroughHostAsync(dm, "Data review complete; locking the database", w => new ApplyLock(
            w.Tenant, lockId, study.Study, studyScope, LockKind.Locked, lockSignature, w.IdempotencyKey, w.ReasonForChange), ct);
        written.AddRange([(lockSignature.ToString(), JourneyStudy.DataManager), (locked.LockId.ToString(), JourneyStudy.DataManager)]);

        var observationRef = NodeRef.For(observation);
        var (refused, refusal) = await KernelHostJourneyFixture.CallAsync(crc, HttpMethod.Post, $"observations/{observationRef}/corrections", "Late change after lock",
            new { enrollment_ref = NodeRef.For(enrollment.EnrollmentId).ToString(), value = new { type = "quantity", value = 130, unit = "mm[Hg]" }, expected_version = 1 }, ct);
        using (refused)
        {
            refused.StatusCode.Should().Be((HttpStatusCode)423, refusal);
        }

        // The locked value as a reader sees it over HTTP: one revision, the closed query and its answer, the SDV of
        // revision 1, and the study lock.
        var read = await ReadObservationAsync(dm, observationRef, ct);
        read.Value.Should().Be(new QuantityValue(128m, "mm[Hg]"));
        read.EnrollmentRef.Should().Be(NodeRef.For(enrollment.EnrollmentId).ToString());
        read.Revisions.Should().ContainSingle().Which.RecordedBy.Should().Be(JourneyStudy.Coordinator);
        read.Assertions.Should().Contain(a => a.AssertionId == query.AssertionId && a.Kind == AssertionKind.Queried && a.RetractedBy == closed.AssertionId);
        read.Assertions.Should().Contain(a => a.AssertionId == answer.AssertionId && a.Kind == AssertionKind.QueryAnswered && a.AssertedBy == JourneyStudy.Coordinator);
        read.Assertions.Should().Contain(a => a.AssertionId == verified.AssertionId && a.Kind == AssertionKind.Verified && a.Revision == 1 && a.AssertedBy == JourneyStudy.Monitor);
        read.Locks.Should().ContainSingle().Which.LockId.Should().Be(locked.LockId);

        // Provenance: the value's lineage reaches the protocol version that governed its collection.
        var (traced, trace) = await KernelHostJourneyFixture.CallAsync(dm, HttpMethod.Get, $"nodes/{observationRef}/lineage?direction=Backward&depth=5", null, null, ct);
        using (traced)
        {
            traced.StatusCode.Should().Be(HttpStatusCode.OK, trace);
        }

        var lineage = JsonSerializer.Deserialize<KernelLineageResponse>(trace, KernelHostJourneyFixture.Json)!;
        lineage.Nodes.Should().Contain(n => n.Ref != null && NodeRef.Parse(n.Ref).Unversioned() == NodeRef.For(study.ProtocolVersion));

        // TODO(2026-10-20): export the locked study to SDTM Dataset-JSON and assert the VS record carries 128 mm[Hg] with
        // lineage back to this observation revision. No export from kernel observations exists yet (port of
        // dataset_json_etl.py #5042; continuous SDTM projection #4814).

        await AssertOneParticipantAsync(participant, enrollment, [("observations", observation.Value), ("collection_instances", opened.FormId)], ct);
        await AssertAuditedAsync(study, written, ct);
    }

    /// <summary>
    /// SAE to safety: an enrolled participant's serious adverse event is captured on the EDC adverse event form; the
    /// committed event that safety consumes names the same participant. A later EDC correction appends revision 2 with its
    /// reason and keeps revision 1 (K-FACT-1), and commits the correction event a reconciliation consumes, so the change is
    /// published rather than silently diverging. Every write audited as its caller.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    [KernelRule("K-FACT-1")]
    public async Task SaeToSafety_CapturedAndCorrectedInEdc_PublishedUnderTheSameParticipantAndAuditedAsItsCaller()
    {
        fixture.SkipUnlessPostgres();
        var ct = TestContext.Current.CancellationToken;
        var study = await JourneyStudy.ArrangeAsync(fixture, ct);
        using var crc = fixture.Caller(study.TenantCode, JourneyStudy.Coordinator);
        using var investigator = fixture.Caller(study.TenantCode, JourneyStudy.Investigator);
        var written = new List<(string AggregateId, string Caller)>();
        var (participant, enrollment) = await EnrollAsync(study, crc, written, ct);

        // SAE captured on the adverse event log form, over the Execution module's eCRF routes (#5576).
        var form = await OpenFormAsync(crc, enrollment, JourneyStudy.AdverseEventSpec, null, "Adverse event reported by the participant", ct);
        var saved = await SaveFormAsync(crc, form.FormId, "Entered from the hospital discharge letter", ct,
            (JourneyStudy.AeTerm, new { type = "text", value = "Myocardial infarction" }),
            (JourneyStudy.AeSerious, new { type = "boolean", value = true }));
        var term = ObservationId.FromGuid(saved.Values.Single(v => v.ItemId == JourneyStudy.AeTerm).ObservationId);
        var serious = ObservationId.FromGuid(saved.Values.Single(v => v.ItemId == JourneyStudy.AeSerious).ObservationId);
        written.AddRange([(CollectionInstanceId.FromGuid(form.FormId).ToString(), JourneyStudy.Coordinator), (term.ToString(), JourneyStudy.Coordinator), (serious.ToString(), JourneyStudy.Coordinator)]);

        await AwaitFormConsumerAsync(study, CollectionInstanceId.FromGuid(form.FormId), written, ct);

        // What safety receives: the committed ObservationRecorded events, under the same kernel participant.
        var recorded = await fixture.EventsAsync(study.Tenant, "ObservationRecorded", ct);
        recorded.Select(e => (Guid.Parse(Member(e, "observation_id")), Guid.Parse(Member(e, "participant_id"))))
            .Should().Contain([(term.Value, participant.Value), (serious.Value, participant.Value)]);

        // TODO(2026-10-20): assert safety sees the SAE under this participant, through case intake or SAE reconciliation
        // in the Safety module. Safety maps neither (its SaeReconciliationRun and SaeDiscrepancy tables have no route and
        // key subjects by string), and it is not yet a module of the single host (#5579, #5378).

        // The EDC correction, over the host's correction route.
        var termRef = NodeRef.For(term);
        const string CorrectionReason = "Discharge letter amended: acute myocardial infarction";
        var (corrected, body) = await KernelHostJourneyFixture.CallAsync(investigator, HttpMethod.Post, $"observations/{termRef}/corrections", CorrectionReason,
            new { enrollment_ref = NodeRef.For(enrollment.EnrollmentId).ToString(), value = new { type = "text", value = "Acute myocardial infarction" }, expected_version = 1 }, ct);
        using (corrected)
        {
            corrected.StatusCode.Should().Be(HttpStatusCode.OK, body);
        }

        var read = await ReadObservationAsync(crc, termRef, ct);
        read.Value.Should().Be(new TextValue("Acute myocardial infarction"));
        read.Revisions.Select(r => (r.Number, r.Value, r.RecordedBy)).Should().Equal(
            (1, new TextValue("Myocardial infarction"), JourneyStudy.Coordinator),
            (2, new TextValue("Acute myocardial infarction"), JourneyStudy.Investigator));
        read.Revisions[1].ReasonForChange.Should().Be(CorrectionReason);
        read.Audit.Should().Contain(a => a.Action == "Updated" && a.ActorId == JourneyStudy.Investigator && a.ReasonForChange == CorrectionReason);

        var corrections = await fixture.EventsAsync(study.Tenant, "ObservationCorrected", ct);
        corrections.Should().ContainSingle(e => Guid.Parse(Member(e, "observation_id")) == term.Value);

        // TODO(2026-10-20): assert the correction raises a discrepancy in safety's SAE reconciliation instead of the two
        // copies drifting apart; there is no reconciliation consumer of ObservationCorrected yet (#5579, #5378).

        await AssertOneParticipantAsync(participant, enrollment, [("observations", term.Value), ("observations", serious.Value), ("collection_instances", form.FormId)], ct);
        await AssertAuditedAsync(study, written, ct);
    }

    /// <summary>
    /// Withdrawal: a value captured while the participant takes part, then the investigator signs and records the
    /// participant's withdrawal; the value captured before it stays readable, unchanged, with its history. One participant
    /// throughout, every write audited as its caller.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Withdrawal_DataCapturedBeforeStaysReadable_OneParticipantAndEveryWriteAuditedAsItsCaller()
    {
        fixture.SkipUnlessPostgres();
        var ct = TestContext.Current.CancellationToken;
        var study = await JourneyStudy.ArrangeAsync(fixture, ct);
        using var crc = fixture.Caller(study.TenantCode, JourneyStudy.Coordinator);
        using var investigator = fixture.Caller(study.TenantCode, JourneyStudy.Investigator);
        var written = new List<(string AggregateId, string Caller)>();
        var (participant, enrollment) = await EnrollAsync(study, crc, written, ct);

        // Captured before withdrawal, over the host's observation route.
        var (created, body) = await KernelHostJourneyFixture.CallAsync(crc, HttpMethod.Post, "observations", "Entered from the source document", new
        {
            enrollment_ref = NodeRef.For(enrollment.EnrollmentId).ToString(),
            concept = study.SystolicBloodPressure.ToString(),
            value = new { type = "quantity", value = 141, unit = "mm[Hg]" },
            observed_at = SystemClock.Instance.UtcNow.AddMinutes(-15),
        }, ct);
        using (created)
        {
            created.StatusCode.Should().Be(HttpStatusCode.Created, body);
        }

        var captured = JsonSerializer.Deserialize<KernelObservationStateResponse>(body, KernelHostJourneyFixture.Json)!;
        var observationRef = NodeRef.Parse(captured.Ref).Unversioned();
        var observation = ObservationId.FromGuid(observationRef.Id);
        written.Add((observation.ToString(), JourneyStudy.Coordinator));

        // TODO(2026-10-20): withdraw consent (ConsentWithdrawn closing the withdrawn scopes' valid periods, K-CONS-2) once
        // the kernel has computable consent (#4777). What the kernel has today is the participant's withdrawal from the
        // study, signed by the investigator (spec §7.4), which the journey records.
        var withdrawalSignature = await fixture.OkThroughHostAsync(investigator, "Signing the participant's withdrawal", w => new Attest(
            w.Tenant, AttestationId.New(), AttestationMeaning.Withdrawn, new AttestationTarget(NodeKinds.Enrollment, enrollment.EnrollmentId.Value, enrollment.ContentHash),
            SystemClock.Instance.UtcNow.AddMinutes(-1), "pwd+otp", w.IdempotencyKey, w.ReasonForChange), ct);
        var withdrawn = await fixture.OkThroughHostAsync(investigator, "Participant withdrew consent at the week 12 visit", w => new WithdrawParticipant(
            w.Tenant, enrollment.EnrollmentId, WithdrawalKind.WithdrawalBySubject, withdrawalSignature, SystemClock.Instance.UtcNow.AddMinutes(-1),
            w.IdempotencyKey, w.ReasonForChange), ct);
        withdrawn.Status.Should().Be(EnrollmentStatus.Withdrawn);
        withdrawn.ParticipantId.Should().Be(participant);
        written.AddRange([(withdrawalSignature.ToString(), JourneyStudy.Investigator), (enrollment.EnrollmentId.ToString(), JourneyStudy.Investigator)]);

        // Data captured before the withdrawal stays readable, unchanged.
        var read = await ReadObservationAsync(crc, observationRef, ct);
        read.Value.Should().Be(new QuantityValue(141m, "mm[Hg]"));
        read.Revisions.Should().ContainSingle().Which.RecordedBy.Should().Be(JourneyStudy.Coordinator);

        // TODO(2026-10-20): assert that new capture for the withdrawn scope is refused in every module: a kernel
        // observation or form save (K-CONS-1 evaluated in the command pipeline, #4777), Execution's capture routes once
        // it is a module (#5563) and eConsent's (#5378). Today no consent scope exists to refuse it, and the kernel's
        // RecordObservation and OpenCollectionInstance do not check the enrollment's status, so capture after a
        // withdrawal is still accepted (#5578); asserting either outcome here would only pin today's gap.

        await AssertOneParticipantAsync(participant, withdrawn, [("observations", observation.Value)], ct);
        await AssertAuditedAsync(study, written, ct);
    }

    /// <summary>The coordinator registers, screens and enrolls a participant through the host; returns both ids.</summary>
    private async Task<(ParticipantId Participant, EnrollmentState Enrollment)> EnrollAsync(
        JourneyStudy study, HttpClient crc, List<(string AggregateId, string Caller)> written, CancellationToken ct)
    {
        // TODO(2026-10-20): replace the relay with the Execution module's subject routes (#5563).
        var participant = await fixture.OkThroughHostAsync(crc, "Participant signed the informed consent form", w => new RegisterParticipant(
            w.Tenant, ParticipantId.New(), w.IdempotencyKey, w.ReasonForChange), ct);
        SubjectCode.TryFrom($"J-{Guid.NewGuid():N}"[..12].ToUpperInvariant(), out var code).Should().BeTrue();
        var screened = await fixture.OkThroughHostAsync(crc, "Screening visit", w => new ScreenParticipant(
            w.Tenant, EnrollmentId.New(), participant, study.StudySite, code, SystemClock.Instance.UtcNow.AddMinutes(-30), w.IdempotencyKey, w.ReasonForChange), ct);
        var enrolled = await fixture.OkThroughHostAsync(crc, "Eligibility confirmed", w => new ChangeEnrollmentStatus(
            w.Tenant, screened.EnrollmentId, EnrollmentStatus.Enrolled, SystemClock.Instance.UtcNow.AddMinutes(-20), w.IdempotencyKey, w.ReasonForChange), ct);
        enrolled.Status.Should().Be(EnrollmentStatus.Enrolled);
        enrolled.EnrollmentNumber.Should().NotBeNull();
        written.AddRange([(participant.ToString(), JourneyStudy.Coordinator), (screened.EnrollmentId.ToString(), JourneyStudy.Coordinator)]);
        return (participant, enrolled);
    }

    /// <summary>
    /// The Execution module in the same host consumes the kernel's form-opened event and opens its own form, with the
    /// kernel form's id, as its service account: the module write is audited as that account, not as the person.
    /// </summary>
    private async Task AwaitFormConsumerAsync(JourneyStudy study, CollectionInstanceId form, List<(string AggregateId, string Caller)> written, CancellationToken ct)
    {
        await fixture.AwaitAuditAsync(study.Tenant, form.ToString(), FormsConsumer, ct);
        written.Add((form.ToString(), FormsConsumer));
    }

    /// <summary>Opens a form over <c>POST /api/v1/execution/forms</c> as <paramref name="caller"/>.</summary>
    private static async Task<FormStateResponse> OpenFormAsync(
        HttpClient caller, EnrollmentState enrollment, string spec, string? visit, string reason, CancellationToken ct)
    {
        var (response, body) = await KernelHostJourneyFixture.CallAsync(caller, HttpMethod.Post, FormsRoute, reason, new
        {
            enrollment_id = enrollment.EnrollmentId.Value,
            collection_spec_id = spec,
            planned_activity_id = visit,
            collected_at = SystemClock.Instance.UtcNow.AddMinutes(-10),
        }, ct);
        using (response)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        }

        return JsonSerializer.Deserialize<FormStateResponse>(body, KernelHostJourneyFixture.Json)!;
    }

    /// <summary>Saves new values (expected revision 0) over <c>PUT /api/v1/execution/forms/{id}/values</c>.</summary>
    private static async Task<FormSavedResponse> SaveFormAsync(
        HttpClient caller, Guid form, string reason, CancellationToken ct, params (string Item, object Value)[] values)
    {
        var page = new { values = values.Select(v => new { item_id = v.Item, value = v.Value, expected_revision = 0 }).ToArray() };
        var (response, body) = await KernelHostJourneyFixture.CallAsync(caller, HttpMethod.Put, $"{FormsRoute}/{form:D}/values", reason, page, ct);
        using (response)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        }

        return JsonSerializer.Deserialize<FormSavedResponse>(body, KernelHostJourneyFixture.Json)!;
    }

    /// <summary>Reads a form over <c>GET /api/v1/execution/forms/{id}</c>.</summary>
    private static async Task<FormResponse> ReadFormAsync(HttpClient reader, Guid form, CancellationToken ct)
    {
        var (response, body) = await KernelHostJourneyFixture.CallAsync(reader, HttpMethod.Get, $"{FormsRoute}/{form:D}", null, null, ct);
        using (response)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        }

        return JsonSerializer.Deserialize<FormResponse>(body, KernelHostJourneyFixture.Json)!;
    }

    private static async Task<KernelObservationResponse> ReadObservationAsync(HttpClient reader, NodeRef observation, CancellationToken ct)
    {
        var (response, body) = await KernelHostJourneyFixture.CallAsync(reader, HttpMethod.Get, $"observations/{observation}", null, null, ct);
        using (response)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        }

        return JsonSerializer.Deserialize<KernelObservationResponse>(body, KernelHostJourneyFixture.Json)!;
    }

    /// <summary>The enrollment and every kernel row the journey wrote about the participant name the same participant.</summary>
    private async Task AssertOneParticipantAsync(
        ParticipantId participant, EnrollmentState enrollment, IReadOnlyList<(string Table, Guid Id)> rows, CancellationToken ct)
    {
        enrollment.ParticipantId.Should().Be(participant);
        (await fixture.ParticipantOfAsync("enrollments", enrollment.EnrollmentId.Value, ct)).Should().Be(participant.Value);
        foreach (var (table, id) in rows)
        {
            (await fixture.ParticipantOfAsync(table, id, ct)).Should().Be(participant.Value, $"the {table} row {id} belongs to the journey's participant");
        }
    }

    /// <summary>Every journey write has a kernel audit entry naming its caller, and no journey write is attributed to anyone else.</summary>
    private async Task AssertAuditedAsync(JourneyStudy study, IReadOnlyList<(string AggregateId, string Caller)> written, CancellationToken ct)
    {
        var audit = await fixture.AuditAsync(study.Tenant, ct);
        foreach (var (aggregate, caller) in written)
        {
            audit.Should().Contain(
                a => a.AggregateId == aggregate && a.ActorId == caller && a.ReasonForChange.Length > 0,
                $"the write of {aggregate} is audited as {caller}");
        }

        var journeyCallers = new[] { JourneyStudy.Coordinator, JourneyStudy.Investigator, JourneyStudy.Monitor, JourneyStudy.DataManager, FormsConsumer };
        audit.Where(a => written.Any(w => w.AggregateId == a.AggregateId)).Select(a => a.ActorId).Should().OnlyContain(actor => journeyCallers.Contains(actor));
    }

    private static string Member(JsonDocument payload, string snakeCaseName)
    {
        foreach (var property in payload.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name.Replace("_", string.Empty, StringComparison.Ordinal), snakeCaseName.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty("value", out var inner) ? inner.ToString() : property.Value.ToString();
            }
        }

        throw new InvalidOperationException($"The event payload has no {snakeCaseName}: {payload.RootElement}");
    }
}
