using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.References;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Capture after a participant's withdrawal (spec §7.4, #5578) through the kernel command pipeline on PostgreSQL, as the
/// application role under Row-Level Security: recording an observation, opening a form and saving a new value in a form
/// are refused with <c>K.Observation.ParticipantWithdrawn</c> and an audited refusal, while values captured before the
/// withdrawal stay readable and correctable; and a withdrawal and a capture serialise on the enrollment's scope lock, so
/// no new value commits after the withdrawal. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelCaptureAfterWithdrawalPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private const string CaptureProducer = "kernel.capture";

    private readonly KernelCollectionScenario _forms = new(fixture);

    public void Dispose()
    {
        if (fixture.ConnectionString is not null)
        {
            fixture.UnitOfWork.FaultInjector = null;
        }
    }

    /// <summary>
    /// After the withdrawal, recording an observation, opening a form and saving a page with a new value (alone or with
    /// a correction) are each refused with 409 and an explanation naming the withdrawn enrollment. Nothing is written but
    /// one "Refused" audit entry per refusal, naming the caller, the command, the error code and the evaluated condition,
    /// on a provenance activity that keeps the command's permit; no event or outbox row is raised.
    /// </summary>
    [Fact(DisplayName = "Capture_AfterWithdrawal_IsRefusedWithAnAuditedRefusal_AndWritesNothingElse")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task CaptureAfterWithdrawalIsRefusedWithAnAuditedRefusalAndWritesNothingElse()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await _forms.NewFormAsync(ct);
        var subject = form.Subject;
        var saved = await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(182))), ct);
        await _forms.Protocol.OkAsync(await _forms.Observations.WithdrawAsync(subject.Tenant, subject.Enrollment, ct), ct);
        var before = await CountsAsync(subject.Tenant, ct);

        var record = await fixture.Sender.SendAsync(_forms.Observations.Record(subject.Tenant, subject.Enrollment, Mmhg(120)), cancellationToken: ct);
        var open = await fixture.Sender.SendAsync(Open(subject, CollectionInstanceId.New(), Tick()), cancellationToken: ct);
        var newValue = await fixture.Sender.SendAsync(Save(form, Value(Diastolic, Mmhg(91))), cancellationToken: ct);
        var mixed = await fixture.Sender.SendAsync(
            Save(form, Value(Systolic, Mmhg(128), saved.Values.Single().Revision), Value(Diastolic, Mmhg(91))), cancellationToken: ct);

        foreach (var error in new[] { record.Error, open.Error, newValue.Error, mixed.Error })
        {
            error!.Code.Should().Be(ObservationErrors.ParticipantWithdrawn.Code);
            error.HttpStatus.Should().Be(409);
            error.Explanation!.Producer.Should().Be(CaptureProducer);
            var condition = error.Explanation.Conditions.Should().ContainSingle().Which;
            condition.Met.Should().BeFalse();
            condition.Code.Should().Be("participation.not-withdrawn");
            condition.Evidence.Should().ContainSingle().Which.Unversioned().Should().Be(NodeRef.For(subject.Enrollment));
        }

        var after = await CountsAsync(subject.Tenant, ct);
        after.Should().Be(
            (before.Observations, before.Instances, before.Revisions, before.Audit + 4, before.Activities + 4, before.Events, before.Outbox),
            "a refusal writes its audit entry and provenance only");

        await using var db = fixture.CreateContext(subject.Tenant);
        var refusals = await db.AuditEntries.AsNoTracking().Where(a => a.Action == "Refused").OrderBy(a => a.Sequence).ToListAsync(ct);
        refusals.Should().HaveCount(4);
        refusals.Should().OnlyContain(a => a.ActorId == KernelPostgresFixture.ActorId && a.AggregateId == null && a.ReasonForChange.Length > 0);
        var commands = refusals.Select(a => JsonDocument.Parse(a.Changes)).ToList();
        commands.Select(c => c.RootElement.GetProperty("command").GetString())
            .Should().Equal(nameof(RecordObservation), nameof(OpenCollectionInstance), nameof(RecordCollectedValues), nameof(RecordCollectedValues));
        commands.Should().OnlyContain(c =>
            c.RootElement.GetProperty("code").GetString() == ObservationErrors.ParticipantWithdrawn.Code
            && c.RootElement.GetProperty("conditions")[0].GetProperty("evidence")[0].GetString()!.StartsWith(NodeRef.For(subject.Enrollment).ToString(), StringComparison.Ordinal));
        var activities = refusals.Select(a => a.ActivityId).ToList();
        var provenance = await db.ProvenanceActivities.AsNoTracking().Where(p => activities.Contains(p.Id)).ToListAsync(ct);
        provenance.Should().HaveCount(4).And.OnlyContain(p => p.Outcome == "Refused" && JsonDocument.Parse(p.Authorization!).RootElement.GetProperty("permitted").GetBoolean());
    }

    /// <summary>
    /// The withdrawal stops new capture, not the data captured before it (K-CONS-2 as far as the kernel has it today):
    /// those values stay readable, a correction of an observation and a form save that only corrects values commit
    /// as new revisions with their reasons.
    /// </summary>
    [Fact(DisplayName = "PreWithdrawalValues_StayReadable_AndCorrectable")]
    public async Task PreWithdrawalValuesStayReadableAndCorrectable()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await _forms.NewFormAsync(ct);
        var subject = form.Subject;
        var recorded = await _forms.Observations.RecordAsync(subject.Tenant, subject.Enrollment, Mmhg(141), ct);
        var saved = await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(182)), Value(Diastolic, Mmhg(91))), ct);
        await _forms.Protocol.OkAsync(await _forms.Observations.WithdrawAsync(subject.Tenant, subject.Enrollment, ct), ct);

        var read = await fixture.CollectionInstanceRecords.FindAsync(subject.Tenant, form.Instance, ct);
        read!.Values.Select(v => (v.ItemId, v.Value)).Should().Equal((Diastolic, (ObservationValue)Mmhg(91)), (Systolic, (ObservationValue)Mmhg(182)));

        Tick();
        var corrected = await _forms.Protocol.OkAsync(KernelObservationScenario.Correct(subject.Tenant, recorded, Mmhg(140)), ct);
        corrected.Revision.Should().Be(2);
        Tick();
        var page = await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(128), 1), Value(Diastolic, Mmhg(91), 1)), ct);
        page.Values.Select(v => (v.ItemId, v.Revision, v.Changed)).Should().Equal((Diastolic, 1, false), (Systolic, 2, true));
        page.Values[1].ObservationId.Should().Be(saved.Values[1].ObservationId);

        await using var db = fixture.CreateContext(subject.Tenant);
        (await db.AuditEntries.AsNoTracking().CountAsync(a => a.Action == "Refused", ct)).Should().Be(0);
    }

    /// <summary>
    /// The withdrawal-versus-capture race, made deterministic (ADR-2621): a withdrawal held inside its commit makes a
    /// concurrent observation wait on the enrollment's exclusive scope lock and then refuse; in the other order the
    /// withdrawal waits for the observation's shared scope lock and commits after it. Either way no observation commits
    /// after the withdrawal.
    /// </summary>
    [Fact(DisplayName = "WithdrawalAndCapture_Race_NoCaptureCommitsAfterTheWithdrawal")]
    [KernelRule("K-TX-4")]
    public async Task WithdrawalAndCaptureRaceNoCaptureCommitsAfterTheWithdrawal()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        // 1. The withdrawal is mid-commit when the capture arrives.
        var subject = await _forms.Observations.NewEnrollmentAsync(ct);
        var withdrawal = await _forms.Observations.WithdrawAsync(subject.Tenant, subject.Enrollment, ct);
        using (var paused = new KernelCommitPause(fixture))
        {
            var withdrawing = paused.HoldAsync(() => fixture.Sender.SendAsync(withdrawal, cancellationToken: ct));
            await paused.Reached.WaitAsync(TimeSpan.FromSeconds(30), ct);
            var capture = fixture.Sender.SendAsync(_forms.Observations.Record(subject.Tenant, subject.Enrollment, Mmhg(120)), cancellationToken: ct);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            capture.IsCompleted.Should().BeFalse("the capture waits for the withdrawal's exclusive scope lock");
            paused.Release();

            (await withdrawing).IsSuccess.Should().BeTrue();
            (await capture).Error!.Code.Should().Be(ObservationErrors.ParticipantWithdrawn.Code);
        }

        await AssertNoCaptureAfterWithdrawalAsync(subject, expectedObservations: 0, ct);

        // 2. The capture is mid-commit when the withdrawal arrives.
        subject = await _forms.Observations.NewEnrollmentAsync(ct);
        withdrawal = await _forms.Observations.WithdrawAsync(subject.Tenant, subject.Enrollment, ct);
        using (var paused = new KernelCommitPause(fixture))
        {
            var capture = paused.HoldAsync(() => fixture.Sender.SendAsync(_forms.Observations.Record(subject.Tenant, subject.Enrollment, Mmhg(120)), cancellationToken: ct));
            await paused.Reached.WaitAsync(TimeSpan.FromSeconds(30), ct);
            var withdrawing = fixture.Sender.SendAsync(withdrawal, cancellationToken: ct);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            withdrawing.IsCompleted.Should().BeFalse("the withdrawal waits for the capture's shared scope lock");
            paused.Release();

            (await capture).IsSuccess.Should().BeTrue();
            (await withdrawing).IsSuccess.Should().BeTrue();
        }

        await AssertNoCaptureAfterWithdrawalAsync(subject, expectedObservations: 1, ct);
    }

    /// <summary>
    /// After a participant fails screening (<c>EnrollmentStatus.ScreenFailed</c>), recording a new observation, opening
    /// a new form, or saving a new item on a pre-existing form is refused with 409,
    /// <c>K.Observation.ParticipantScreenFailed</c>, and a <c>UX-9</c> explanation naming the enrollment, writing only
    /// audited refusal entries.
    /// </summary>
    [Fact(DisplayName = "Capture_AfterScreenFailure_IsRefusedWithAnAuditedRefusal_AndWritesNothingElse")]
    public async Task CaptureAfterScreenFailureIsRefusedWithAnAuditedRefusalAndWritesNothingElse()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await _forms.NewFormAsync(ct);
        var subject = form.Subject;
        var saved = await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(164))), ct);
        await _forms.Protocol.OkAsync(
            new ChangeEnrollmentStatus(subject.Tenant, subject.Enrollment, EnrollmentStatus.ScreenFailed, Tick(), KernelProtocolScenario.Key(), "Exclusion criterion met"),
            ct);
        var before = await CountsAsync(subject.Tenant, ct);

        var record = await fixture.Sender.SendAsync(_forms.Observations.Record(subject.Tenant, subject.Enrollment, Mmhg(120)), cancellationToken: ct);
        var open = await fixture.Sender.SendAsync(Open(subject, CollectionInstanceId.New(), Tick()), cancellationToken: ct);
        var newValue = await fixture.Sender.SendAsync(Save(form, Value(Diastolic, Mmhg(91))), cancellationToken: ct);
        var mixed = await fixture.Sender.SendAsync(
            Save(form, Value(Systolic, Mmhg(158), saved.Values.Single().Revision), Value(Diastolic, Mmhg(91))), cancellationToken: ct);

        foreach (var error in new[] { record.Error, open.Error, newValue.Error, mixed.Error })
        {
            error!.Code.Should().Be(ObservationErrors.ParticipantScreenFailed.Code);
            error.HttpStatus.Should().Be(409);
            error.Explanation!.Producer.Should().Be(CaptureProducer);
            var condition = error.Explanation.Conditions.Should().ContainSingle().Which;
            condition.Met.Should().BeFalse();
            condition.Code.Should().Be("participation.not-screen-failed");
            condition.Evidence.Should().ContainSingle().Which.Unversioned().Should().Be(NodeRef.For(subject.Enrollment));
        }

        var after = await CountsAsync(subject.Tenant, ct);
        after.Should().Be(
            (before.Observations, before.Instances, before.Revisions, before.Audit + 4, before.Activities + 4, before.Events, before.Outbox),
            "a screen-failure refusal writes its audit entry and provenance only");
    }

    /// <summary>
    /// Screening observations captured before screen failure remain readable and correctable (<c>K-CONS-2</c>).
    /// </summary>
    [Fact(DisplayName = "PreScreenFailureValues_StayReadable_AndCorrectable")]
    public async Task PreScreenFailureValuesStayReadableAndCorrectable()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await _forms.NewFormAsync(ct);
        var subject = form.Subject;
        var recorded = await _forms.Observations.RecordAsync(subject.Tenant, subject.Enrollment, Mmhg(152), ct);
        var saved = await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(164)), Value(Diastolic, Mmhg(94))), ct);
        await _forms.Protocol.OkAsync(
            new ChangeEnrollmentStatus(subject.Tenant, subject.Enrollment, EnrollmentStatus.ScreenFailed, Tick(), KernelProtocolScenario.Key(), "Exclusion criterion met"),
            ct);

        var read = await fixture.CollectionInstanceRecords.FindAsync(subject.Tenant, form.Instance, ct);
        read!.Values.Select(v => (v.ItemId, v.Value)).Should().Equal((Diastolic, (ObservationValue)Mmhg(94)), (Systolic, (ObservationValue)Mmhg(164)));

        Tick();
        var corrected = await _forms.Protocol.OkAsync(KernelObservationScenario.Correct(subject.Tenant, recorded, Mmhg(150)), ct);
        corrected.Revision.Should().Be(2);
        Tick();
        var page = await _forms.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(160), 1), Value(Diastolic, Mmhg(94), 1)), ct);
        page.Values.Select(v => (v.ItemId, v.Revision, v.Changed)).Should().Equal((Diastolic, 1, false), (Systolic, 2, true));
        page.Values[1].ObservationId.Should().Be(saved.Values[1].ObservationId);

        await using var db = fixture.CreateContext(subject.Tenant);
        (await db.AuditEntries.AsNoTracking().CountAsync(a => a.Action == "Refused", ct)).Should().Be(0);
    }

    private async Task AssertNoCaptureAfterWithdrawalAsync(KernelObservationScenario.Subject subject, int expectedObservations, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(subject.Tenant);
        var enrollmentKey = subject.Enrollment.ToString();
        var withdrawnAt = (await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateType == nameof(Enrollment) && a.AggregateId == enrollmentKey && a.Action == "Updated")
            .SingleAsync(ct)).Sequence;
        var captured = await db.AuditEntries.AsNoTracking().Where(a => a.AggregateType == nameof(Observation)).ToListAsync(ct);
        captured.Should().HaveCount(expectedObservations);
        captured.Should().OnlyContain(c => c.Sequence < withdrawnAt, "no observation commits after the withdrawal");
    }

    private async Task<(int Observations, int Instances, int Revisions, int Audit, int Activities, int Events, int Outbox)> CountsAsync(
        TenantId tenant, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        return (
            await db.Observations.CountAsync(ct),
            await db.CollectionInstances.CountAsync(ct),
            await db.ObservationRevisions.CountAsync(ct),
            await db.AuditEntries.CountAsync(ct),
            await db.ProvenanceActivities.CountAsync(ct),
            await db.DomainEvents.CountAsync(ct),
            await db.OutboxMessages.CountAsync(ct));
    }

    private DateTimeOffset Tick() => _forms.Tick();
}
