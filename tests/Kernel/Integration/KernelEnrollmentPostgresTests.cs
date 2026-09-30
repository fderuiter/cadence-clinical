using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Kernel.Persistence.Execution;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace Cadence.UnitTests.Kernel.Integration;

/// <summary>
/// Participants and enrollments through the production pipeline against PostgreSQL (spec §7.4, #4776): the lifecycle
/// with its bitemporal history, gap-free enrollment numbers under the study's advisory lock (K-TX-4), subject codes
/// unique per study, attested withdrawal, and the database guards that repeat the lifecycle whoever connects.
/// Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelEnrollmentPostgresTests(KernelPostgresFixture fixture)
    : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private readonly KernelProtocolScenario _scenario = new(fixture);

    public void Dispose()
    {
        if (fixture.ConnectionString is not null)
        {
            fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
        }
    }

    /// <summary>
    /// Validates the whole path: a participant is registered and screened, enrolled with number 1, randomized, made
    /// active and completed. Each step raises its event, the status reads correctly as of any valid and known time,
    /// and the subject code resolves to the participant.
    /// </summary>
    [Fact(DisplayName = "Enrollment_ScreenEnrollRandomizeComplete_EndToEnd")]
    public async Task EnrollmentScreenEnrollRandomizeCompleteEndToEnd()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, studySite) = await NewStudySiteAsync(ct);
        var screenedAt = Tick();
        var (participant, enrollment) = await ScreenAsync(tenant, studySite, "SUBJ-001", screenedAt, ct);

        var enrolledAt = Tick();
        var enrolled = await ChangeAsync(tenant, enrollment, EnrollmentStatus.Enrolled, enrolledAt, ct);
        var enrolledKnownAt = fixture.Clock.UtcNow;
        await ChangeAsync(tenant, enrollment, EnrollmentStatus.Randomized, Tick(), ct);
        await ChangeAsync(tenant, enrollment, EnrollmentStatus.Active, Tick(), ct);
        var completed = await ChangeAsync(tenant, enrollment, EnrollmentStatus.Completed, Tick(), ct);

        enrolled.EnrollmentNumber.Should().Be(1);
        enrolled.StatusSince.Should().Be(enrolledAt);
        completed.Should().BeEquivalentTo(new
        {
            EnrollmentId = enrollment,
            ParticipantId = participant,
            StudyId = study,
            StudySiteId = studySite,
            SubjectCode = SubjectCode.From("SUBJ-001"),
            Status = EnrollmentStatus.Completed,
            EnrollmentNumber = (int?)1,
        });

        var directory = fixture.Enrollments;
        (await directory.ResolveParticipantAsync(tenant, study, SubjectCode.From("SUBJ-001"), ct))!.ParticipantId.Should().Be(participant);
        (await directory.ResolveParticipantAsync(tenant, study, SubjectCode.From("SUBJ-999"), ct)).Should().BeNull();
        (await directory.FindAsync(tenant, enrollment, ct)).Should().Be(completed);
        (await directory.StatusAsOfAsync(tenant, enrollment, enrolledAt, enrolledKnownAt, ct)).Should().Be(EnrollmentStatus.Enrolled);
        (await directory.StatusAsOfAsync(tenant, enrollment, enrolledAt.AddTicks(-10), fixture.Clock.UtcNow, ct)).Should().Be(EnrollmentStatus.Screening);
        (await directory.StatusAsOfAsync(tenant, enrollment, fixture.Clock.UtcNow, enrolledKnownAt, ct)).Should().Be(EnrollmentStatus.Enrolled,
            "what was known when enrollment was recorded did not include the later changes");
        (await directory.StatusAsOfAsync(tenant, enrollment, fixture.Clock.UtcNow, fixture.Clock.UtcNow, ct)).Should().Be(EnrollmentStatus.Completed);
        (await directory.StatusAsOfAsync(tenant, enrollment, screenedAt.AddTicks(-10), fixture.Clock.UtcNow, ct)).Should().BeNull();

        await using var db = fixture.CreateContext(tenant);
        (await db.EnrollmentStatusPeriods.AsNoTracking().CountAsync(p => p.EnrollmentId == enrollment, ct)).Should().Be(1 + (4 * 2));
        var events = await db.DomainEvents.AsNoTracking().Where(e => e.TenantId == tenant).OrderBy(e => e.Recorded).ThenBy(e => e.EventId).Select(e => e.Type).ToListAsync(ct);
        events.Should().ContainInOrder("ParticipantRegistered", "ParticipantScreened", "ParticipantEnrolled", "EnrollmentStatusChanged", "EnrollmentStatusChanged", "EnrollmentStatusChanged");
    }

    /// <summary>
    /// Validates gap-free numbering under concurrency (spec §7.4, K-TX-4): participants entering Enrolled at the same
    /// time in one study get exactly 1..N, and another study numbers from 1 on its own.
    /// </summary>
    [Fact(DisplayName = "Enrollment_ConcurrentEnrolments_GetGapFreeNumbersPerStudy")]
    public async Task EnrollmentConcurrentEnrolmentsGetGapFreeNumbersPerStudy()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const int count = 8;
        var (tenant, _, studySite) = await NewStudySiteAsync(ct);
        var screenedAt = Tick();
        var enrollments = new EnrollmentId[count];
        for (var i = 0; i < count; i++)
        {
            enrollments[i] = (await ScreenAsync(tenant, studySite, $"SUBJ-{i:000}", screenedAt, ct)).Enrollment;
        }

        var enrolledAt = Tick();
        var results = await Task.WhenAll(enrollments.Select(id => fixture.Sender.SendAsync(
            new ChangeEnrollmentStatus(tenant, id, EnrollmentStatus.Enrolled, enrolledAt, KernelProtocolScenario.Key(), "Eligible"),
            cancellationToken: ct)));

        results.Should().OnlyContain(r => r.IsSuccess);
        results.Select(r => r.Value!.EnrollmentNumber!.Value).Should().BeEquivalentTo(Enumerable.Range(1, count));

        var (otherStudySite, otherEnrollment) = await ScreenInNewStudyAsync(tenant, ct);
        otherStudySite.Should().NotBe(studySite);
        (await ChangeAsync(tenant, otherEnrollment, EnrollmentStatus.Enrolled, Tick(), ct)).EnrollmentNumber.Should().Be(1);
    }

    /// <summary>
    /// Validates the screening rules: a subject code is unique in its study (not only its site) but may repeat in
    /// another study, a participant cannot be actively enrolled twice at one study site, and unknown participants and
    /// study sites are refused.
    /// </summary>
    [Fact(DisplayName = "Screen_EnforcesCodeUniquenessAndOneActiveEnrollmentPerSite")]
    public async Task ScreenEnforcesCodeUniquenessAndOneActiveEnrollmentPerSite()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, studySite) = await NewStudySiteAsync(ct);
        var secondSite = await AddStudySiteAsync(tenant, study, ct);
        var (participant, first) = await ScreenAsync(tenant, studySite, "SUBJ-001", Tick(), ct);
        var other = await RegisterAsync(tenant, ct);

        (await SendScreenAsync(tenant, other, secondSite, "SUBJ-001", Tick(), ct)).Error.Should().Be(ExecutionErrors.SubjectCodeTaken);
        (await SendScreenAsync(tenant, participant, studySite, "SUBJ-002", Tick(), ct)).Error.Should().Be(ExecutionErrors.AlreadyActiveAtSite);
        (await SendScreenAsync(tenant, ParticipantId.New(), studySite, "SUBJ-003", Tick(), ct)).Error.Should().Be(ExecutionErrors.ParticipantUnknown);
        (await SendScreenAsync(tenant, other, StudySiteId.New(), "SUBJ-004", Tick(), ct)).Error.Should().Be(ExecutionErrors.StudySiteUnknown);
        (await SendScreenAsync(tenant, other, studySite, " SUBJ-005", Tick(), ct)).Error.Should().Be(ExecutionErrors.SubjectCodeInvalid);

        // After a screen failure the participant may be rescreened at the same site under a new code.
        await ChangeAsync(tenant, first, EnrollmentStatus.ScreenFailed, Tick(), ct);
        (await SendScreenAsync(tenant, participant, studySite, "SUBJ-001-R", Tick(), ct)).IsSuccess.Should().BeTrue();

        var (_, otherStudySite) = await NewStudyInTenantAsync(tenant, ct);
        (await SendScreenAsync(tenant, other, otherStudySite, "SUBJ-001", Tick(), ct)).IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Validates the command-side lifecycle rules: illegal moves, a change dated in the future or before the current
    /// status, and withdrawal through the generic status command are all refused and leave the enrollment unchanged.
    /// </summary>
    [Fact(DisplayName = "ChangeStatus_RefusesIllegalMovesAndTimes")]
    [GxPRequirement("PRD-SUB-001")]
    public async Task ChangeStatusRefusesIllegalMovesAndTimes()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, _, studySite) = await NewStudySiteAsync(ct);
        var screenedAt = Tick();
        var (_, enrollment) = await ScreenAsync(tenant, studySite, "SUBJ-001", screenedAt, ct);
        Tick();

        (await SendChangeAsync(tenant, enrollment, EnrollmentStatus.Active, fixture.Clock.UtcNow, ct)).Error!.Code.Should().Be(ExecutionErrors.InvalidTransition.Code);
        (await SendChangeAsync(tenant, enrollment, EnrollmentStatus.Withdrawn, fixture.Clock.UtcNow, ct)).Error.Should().Be(ExecutionErrors.WithdrawalRequiresCommand);
        (await SendChangeAsync(tenant, enrollment, EnrollmentStatus.Enrolled, fixture.Clock.UtcNow.AddHours(1), ct)).Error.Should().Be(ExecutionErrors.TransitionTimeInvalid);
        (await SendChangeAsync(tenant, enrollment, EnrollmentStatus.Enrolled, screenedAt, ct)).Error.Should().Be(ExecutionErrors.TransitionTimeInvalid);
        (await SendChangeAsync(tenant, EnrollmentId.New(), EnrollmentStatus.Enrolled, fixture.Clock.UtcNow, ct)).Error.Should().Be(KernelErrors.NotFound);

        var current = await fixture.Enrollments.FindAsync(tenant, enrollment, ct);
        current!.Status.Should().Be(EnrollmentStatus.Screening);
        current.EnrollmentNumber.Should().BeNull();
    }

    /// <summary>
    /// Validates attested withdrawal (spec §7.4): the acting person must have signed Withdrawn over the enrollment's
    /// current state. A Reviewed signature, another person's, one signed before a later change, or an unknown one are
    /// refused; a valid one withdraws the participant for good.
    /// </summary>
    [Fact(DisplayName = "Withdraw_RequiresTheActorsWithdrawnAttestation_OnTheCurrentState")]
    public async Task WithdrawRequiresTheActorsWithdrawnAttestationOnTheCurrentState()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, _, studySite) = await NewStudySiteAsync(ct);
        var (_, enrollment) = await ScreenAsync(tenant, studySite, "SUBJ-001", Tick(), ct);
        var enrolled = await ChangeAsync(tenant, enrollment, EnrollmentStatus.Enrolled, Tick(), ct);
        var stale = await AttestAsync(tenant, AttestationMeaning.Withdrawn, enrolled, ct);
        var active = await ChangeAsync(tenant, enrollment, EnrollmentStatus.Active, Tick(), ct);
        var reviewed = await AttestAsync(tenant, AttestationMeaning.Reviewed, active, ct);
        fixture.CurrentActor.Actor = Actor.Person("someone-else@cadence.test");
        var someoneElses = await AttestAsync(tenant, AttestationMeaning.Withdrawn, active, ct);
        fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);

        foreach (var offered in new[] { stale, reviewed, someoneElses, AttestationId.New() })
        {
            (await SendWithdrawAsync(tenant, enrollment, offered, ct)).Error!.Code.Should().Be(ExecutionErrors.AttestationRejected.Code);
        }

        var signed = await AttestAsync(tenant, AttestationMeaning.Withdrawn, active, ct);
        var withdrawnAt = Tick();
        var withdrawn = await SendWithdrawAsync(tenant, enrollment, signed, ct, withdrawnAt);

        withdrawn.IsSuccess.Should().BeTrue(withdrawn.Error?.Message);
        withdrawn.Value!.Status.Should().Be(EnrollmentStatus.Withdrawn);
        withdrawn.Value.StatusSince.Should().Be(withdrawnAt);
        withdrawn.Value.EnrollmentNumber.Should().Be(1);
        (await SendChangeAsync(tenant, enrollment, EnrollmentStatus.Completed, Tick(), ct)).Error!.Code.Should().Be(ExecutionErrors.InvalidTransition.Code);

        await using var db = fixture.CreateContext(tenant);
        var stored = await db.Enrollments.AsNoTracking().SingleAsync(e => e.Id == enrollment, ct);
        stored.WithdrawalKind.Should().Be(WithdrawalKind.WithdrawalBySubject);
        stored.WithdrawalAttestationId.Should().Be(signed);
        (await db.DomainEvents.AsNoTracking().CountAsync(e => e.TenantId == tenant && e.Type == "ParticipantWithdrawn", ct)).Should().Be(1);
    }

    /// <summary>
    /// Validates the database side of the lifecycle (spec §7.4, PRD-SUB-001): even the superuser cannot skip a
    /// transition, change an enrollment's identity or code, renumber it, withdraw it without an attestation, reopen a
    /// terminal one, delete or truncate (SQLSTATE KE001); status rows are append-only (KT001); and two active
    /// enrollments of one participant at one study site cannot overlap in time (exclusion constraint).
    /// </summary>
    [Fact(DisplayName = "Enrollments_DatabaseGuards_EnforceTheLifecycle_ForEveryDatabaseUser")]
    [GxPRequirement("PRD-SUB-001")]
    public async Task EnrollmentsDatabaseGuardsEnforceTheLifecycleForEveryDatabaseUser()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, studySite) = await NewStudySiteAsync(ct);
        var (participant, enrollment) = await ScreenAsync(tenant, studySite, "SUBJ-001", Tick(), ct);
        await ChangeAsync(tenant, enrollment, EnrollmentStatus.Enrolled, Tick(), ct);
        var (_, failed) = await ScreenAsync(tenant, studySite, "SUBJ-002", Tick(), ct);
        await ChangeAsync(tenant, failed, EnrollmentStatus.ScreenFailed, Tick(), ct);
        var id = enrollment.Value;

        foreach (var statement in new[]
        {
            $"UPDATE kernel.enrollments SET status = 'Completed' WHERE id = '{id}'",
            $"UPDATE kernel.enrollments SET status = 'Screening' WHERE id = '{id}'",
            $"UPDATE kernel.enrollments SET status = 'Withdrawn' WHERE id = '{id}'",
            $"UPDATE kernel.enrollments SET subject_code = 'SUBJ-X' WHERE id = '{id}'",
            $"UPDATE kernel.enrollments SET participant_id = '{Guid.CreateVersion7()}' WHERE id = '{id}'",
            $"UPDATE kernel.enrollments SET enrollment_number = 7 WHERE id = '{id}'",
            $"UPDATE kernel.enrollments SET status_since = status_since - interval '1 day' WHERE id = '{id}'",
            $"UPDATE kernel.enrollments SET status = 'Screening' WHERE id = '{failed.Value}'",
            $"DELETE FROM kernel.enrollments WHERE id = '{id}'",
            "TRUNCATE kernel.enrollments CASCADE",
            $$"""
            INSERT INTO kernel.enrollments (id, tenant_id, participant_id, study_id, study_site_id, subject_code, status, status_since, enrollment_number, created_at, created_by, reason_for_change, version_index)
            VALUES ('{{Guid.CreateVersion7()}}', '{{tenant.Value}}', '{{participant.Value}}', '{{study.Value}}', '{{studySite.Value}}', 'SUBJ-SQL', 'Enrolled', now(), 99, now(), 'sql', 'sql', 1)
            """,
        })
        {
            var act = () => ExecuteAsAdminAsync(statement, ct);
            (await act.Should().ThrowAsync<PostgresException>(statement))
                .Which.SqlState.Should().Be(EnrollmentSql.ViolationSqlState);
        }

        foreach (var statement in new[]
        {
            $"UPDATE kernel.enrollment_status_periods SET status = 'Active' WHERE enrollment_id = '{id}'",
            $"DELETE FROM kernel.enrollment_status_periods WHERE enrollment_id = '{id}'",
        })
        {
            var act = () => ExecuteAsAdminAsync(statement, ct);
            (await act.Should().ThrowAsync<PostgresException>(statement))
                .Which.SqlState.Should().Be(BitemporalSql.AppendOnlyViolationSqlState);
        }

        // A second enrollment of the same participant at the same study site, active while the first one is, is
        // refused by the exclusion constraint even though each enrollment's own history is consistent.
        var secondEnrollment = Guid.CreateVersion7();
        var overlapping = () => ExecuteAsAdminAsync(
            $"""
            INSERT INTO kernel.enrollments (id, tenant_id, participant_id, study_id, study_site_id, subject_code, status, status_since, created_at, created_by, reason_for_change, version_index)
            VALUES ('{secondEnrollment}', '{tenant.Value}', '{participant.Value}', '{study.Value}', '{studySite.Value}', 'SUBJ-SQL', 'Screening', now(), now(), 'sql', 'sql', 1);
            INSERT INTO kernel.enrollment_status_periods (id, tenant_id, enrollment_id, participant_id, study_site_id, status, valid_period, recorded_period)
            VALUES ('{Guid.CreateVersion7()}', '{tenant.Value}', '{secondEnrollment}', '{participant.Value}', '{studySite.Value}', 'Screening', tstzrange(now(), NULL), tstzrange(now(), NULL));
            """,
            ct);
        var violation = (await overlapping.Should().ThrowAsync<PostgresException>()).Which;
        violation.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);
        violation.ConstraintName.Should().Be(EnrollmentSql.ActiveGuardName);

        // A legal step still works for the superuser, so the guard is not a blanket lock.
        await ExecuteAsAdminAsync($"UPDATE kernel.enrollments SET status = 'Randomized', status_since = status_since + interval '1 second' WHERE id = '{id}'", ct);
    }

    /// <summary>
    /// Validates that two screenings sent at the same time with one subject code in one study cannot both succeed:
    /// the study's lock (K-TX-4) serializes them, so exactly one gets the code and the other gets SubjectCodeTaken.
    /// </summary>
    [Fact(DisplayName = "Screen_ConcurrentScreeningsWithOneCode_OnlyOneGetsTheCode")]
    public async Task ScreenConcurrentScreeningsWithOneCodeOnlyOneGetsTheCode()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const int count = 6;
        var (tenant, study, studySite) = await NewStudySiteAsync(ct);
        var sites = new StudySiteId[count];
        var participants = new ParticipantId[count];
        for (var i = 0; i < count; i++)
        {
            sites[i] = i % 2 == 0 ? studySite : await AddStudySiteAsync(tenant, study, ct);
            participants[i] = await RegisterAsync(tenant, ct);
        }

        var screenedAt = Tick();
        Tick();
        var results = await Task.WhenAll(Enumerable.Range(0, count).Select(i => fixture.Sender.SendAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participants[i], sites[i], SubjectCode.From("SUBJ-001"), screenedAt, KernelProtocolScenario.Key(), "Screening visit"),
            cancellationToken: ct)));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Where(r => !r.IsSuccess).Should().OnlyContain(r => r.Error == ExecutionErrors.SubjectCodeTaken);
        await using var db = fixture.CreateContext(tenant);
        (await db.Enrollments.AsNoTracking().CountAsync(e => e.StudyId == study && e.SubjectCode == SubjectCode.From("SUBJ-001"), ct)).Should().Be(1);
    }

    /// <summary>
    /// Validates that the one-active-enrollment rule is checked in valid time, not only against the current status: a
    /// screening back-dated into the period when an earlier enrollment at the same study site was still screening is
    /// refused, even though that enrollment has since screen-failed. A screening after the screen failure is accepted.
    /// </summary>
    [Fact(DisplayName = "Screen_BackdatedIntoAnEarlierActivePeriod_IsRefused")]
    public async Task ScreenBackdatedIntoAnEarlierActivePeriodIsRefused()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, _, studySite) = await NewStudySiteAsync(ct);
        var firstScreenedAt = Tick();
        var (participant, first) = await ScreenAsync(tenant, studySite, "SUBJ-001", firstScreenedAt, ct);
        var whileScreening = Tick();
        var failedAt = Tick();
        await ChangeAsync(tenant, first, EnrollmentStatus.ScreenFailed, failedAt, ct);

        (await SendScreenAsync(tenant, participant, studySite, "SUBJ-001-B", whileScreening, ct)).Error.Should().Be(ExecutionErrors.AlreadyActiveAtSite);
        (await SendScreenAsync(tenant, participant, studySite, "SUBJ-001-C", firstScreenedAt, ct)).Error.Should().Be(ExecutionErrors.AlreadyActiveAtSite);

        var rescreen = await SendScreenAsync(tenant, participant, studySite, "SUBJ-001-R", Tick(), ct);
        rescreen.IsSuccess.Should().BeTrue(rescreen.Error?.Message);
    }

    /// <summary>
    /// Validates that enrollment numbers stay gap-free when other participants screen-fail or withdraw in between: a
    /// number is taken only on entering Enrolled, and a withdrawn participant keeps theirs without it being reused.
    /// </summary>
    [Fact(DisplayName = "EnrollmentNumbers_StayGapFree_AcrossScreenFailuresAndWithdrawals")]
    public async Task EnrollmentNumbersStayGapFreeAcrossScreenFailuresAndWithdrawals()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, studySite) = await NewStudySiteAsync(ct);
        var (_, a) = await ScreenAsync(tenant, studySite, "SUBJ-A", Tick(), ct);
        var (_, b) = await ScreenAsync(tenant, studySite, "SUBJ-B", Tick(), ct);
        var (_, c) = await ScreenAsync(tenant, studySite, "SUBJ-C", Tick(), ct);
        var (_, d) = await ScreenAsync(tenant, studySite, "SUBJ-D", Tick(), ct);

        (await ChangeAsync(tenant, a, EnrollmentStatus.Enrolled, Tick(), ct)).EnrollmentNumber.Should().Be(1);
        await ChangeAsync(tenant, b, EnrollmentStatus.ScreenFailed, Tick(), ct);
        var cScreening = (await fixture.Enrollments.FindAsync(tenant, c, ct))!;
        (await SendWithdrawAsync(tenant, c, await AttestAsync(tenant, AttestationMeaning.Withdrawn, cScreening, ct), ct)).IsSuccess.Should().BeTrue();
        var aEnrolled = (await fixture.Enrollments.FindAsync(tenant, a, ct))!;
        (await SendWithdrawAsync(tenant, a, await AttestAsync(tenant, AttestationMeaning.Withdrawn, aEnrolled, ct), ct)).IsSuccess.Should().BeTrue();
        (await ChangeAsync(tenant, d, EnrollmentStatus.Enrolled, Tick(), ct)).EnrollmentNumber.Should().Be(2);

        await using var db = fixture.CreateContext(tenant);
        var numbers = await db.Enrollments.AsNoTracking().Where(e => e.StudyId == study)
            .OrderBy(e => e.SubjectCode).Select(e => new { e.SubjectCode, e.Status, e.EnrollmentNumber }).ToListAsync(ct);
        numbers.Should().Equal(
            new { SubjectCode = SubjectCode.From("SUBJ-A"), Status = EnrollmentStatus.Withdrawn, EnrollmentNumber = (int?)1 },
            new { SubjectCode = SubjectCode.From("SUBJ-B"), Status = EnrollmentStatus.ScreenFailed, EnrollmentNumber = (int?)null },
            new { SubjectCode = SubjectCode.From("SUBJ-C"), Status = EnrollmentStatus.Withdrawn, EnrollmentNumber = (int?)null },
            new { SubjectCode = SubjectCode.From("SUBJ-D"), Status = EnrollmentStatus.Enrolled, EnrollmentNumber = (int?)2 });
    }

    /// <summary>
    /// Validates the withdrawal rules checked before the signature: an unknown enrollment, an undefined withdrawal kind,
    /// a terminal enrollment and a withdrawal dated in the future are refused even when the actor offers a valid
    /// Withdrawn attestation over the enrollment's current state, and the enrollment is left unchanged.
    /// </summary>
    [Fact(DisplayName = "Withdraw_RefusesUnknownInvalidTerminalAndFutureRequests_EvenWithAValidSignature")]
    public async Task WithdrawRefusesUnknownInvalidTerminalAndFutureRequestsEvenWithAValidSignature()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, _, studySite) = await NewStudySiteAsync(ct);
        var (_, enrollment) = await ScreenAsync(tenant, studySite, "SUBJ-001", Tick(), ct);
        var enrolled = await ChangeAsync(tenant, enrollment, EnrollmentStatus.Enrolled, Tick(), ct);
        var signed = await AttestAsync(tenant, AttestationMeaning.Withdrawn, enrolled, ct);
        var (_, failed) = await ScreenAsync(tenant, studySite, "SUBJ-002", Tick(), ct);
        var screenFailed = await ChangeAsync(tenant, failed, EnrollmentStatus.ScreenFailed, Tick(), ct);
        var signedFailed = await AttestAsync(tenant, AttestationMeaning.Withdrawn, screenFailed, ct);

        (await SendWithdrawAsync(tenant, EnrollmentId.New(), signed, ct)).Error.Should().Be(KernelErrors.NotFound);
        (await SendWithdrawAsync(tenant, enrollment, signed, ct, kind: (WithdrawalKind)999)).Error.Should().Be(ExecutionErrors.WithdrawalKindInvalid);
        (await SendWithdrawAsync(tenant, enrollment, signed, ct, fixture.Clock.UtcNow.AddHours(1))).Error.Should().Be(ExecutionErrors.TransitionTimeInvalid);
        (await SendWithdrawAsync(tenant, enrollment, signed, ct, enrolled.StatusSince)).Error.Should().Be(ExecutionErrors.TransitionTimeInvalid);
        (await SendWithdrawAsync(tenant, failed, signedFailed, ct)).Error!.Code.Should().Be(ExecutionErrors.InvalidTransition.Code);

        (await fixture.Enrollments.FindAsync(tenant, enrollment, ct)).Should().Be(enrolled);
        (await fixture.Enrollments.FindAsync(tenant, failed, ct)).Should().Be(screenFailed);
        await using var db = fixture.CreateContext(tenant);
        (await db.DomainEvents.AsNoTracking().CountAsync(e => e.TenantId == tenant && e.Type == "ParticipantWithdrawn", ct)).Should().Be(0);
    }

    /// <summary>
    /// Validates tenant isolation for participants and enrollments (row-level security, #4865): another tenant cannot read, resolve,
    /// change, screen into, number against or sign another tenant's enrollment, participant or study site.
    /// </summary>
    [Fact(DisplayName = "Enrollments_AreInvisibleToAnotherTenant")]
    public async Task EnrollmentsAreInvisibleToAnotherTenant()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, studySite) = await NewStudySiteAsync(ct);
        var (participant, enrollment) = await ScreenAsync(tenant, studySite, "SUBJ-001", Tick(), ct);
        var enrolled = await ChangeAsync(tenant, enrollment, EnrollmentStatus.Enrolled, Tick(), ct);
        var (other, _, otherSite) = await NewStudySiteAsync(ct);
        var otherParticipant = await RegisterAsync(other, ct);
        var directory = fixture.Enrollments;

        (await directory.FindAsync(other, enrollment, ct)).Should().BeNull();
        (await directory.ResolveParticipantAsync(other, study, SubjectCode.From("SUBJ-001"), ct)).Should().BeNull();
        (await directory.StatusAsOfAsync(other, enrollment, fixture.Clock.UtcNow, fixture.Clock.UtcNow, ct)).Should().BeNull();
        (await directory.NextEnrollmentNumberAsync(other, study, ct)).Should().Be(1, "the other tenant sees none of this study's numbers");

        (await SendChangeAsync(other, enrollment, EnrollmentStatus.Randomized, Tick(), ct)).Error.Should().Be(KernelErrors.NotFound);
        (await SendChangeAsync(other, enrollment, EnrollmentStatus.Enrolled, Tick(), ct)).Error.Should().Be(KernelErrors.NotFound);
        (await SendScreenAsync(other, participant, otherSite, "SUBJ-X1", Tick(), ct)).Error.Should().Be(ExecutionErrors.ParticipantUnknown);
        (await SendScreenAsync(other, otherParticipant, studySite, "SUBJ-X2", Tick(), ct)).Error.Should().Be(ExecutionErrors.StudySiteUnknown);
        var signOther = await _scenario.AttestAsync(
            other, AttestationMeaning.Withdrawn, new AttestationTarget(NodeKinds.Enrollment, enrollment.Value, enrolled.ContentHash), ct);
        signOther.Error.Should().Be(TrustErrors.TargetUnknown);

        (await directory.FindAsync(tenant, enrollment, ct)).Should().Be(enrolled);
    }

    private DateTimeOffset Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));

    private async Task<(TenantId Tenant, StudyId Study, StudySiteId StudySite)> NewStudySiteAsync(CancellationToken ct)
    {
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        return (tenant, study, await AddStudySiteAsync(tenant, study, ct));
    }

    private async Task<(StudyId Study, StudySiteId StudySite)> NewStudyInTenantAsync(TenantId tenant, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        var sponsor = await db.Studies.AsNoTracking().Select(s => s.SponsorId).FirstAsync(ct);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var study = new Study(StudyId.New(), tenant, sponsor, $"STUDY-{suffix}", "Second study", "Phase 2", KernelPostgresFixture.ActorId, "Seeded for test");
        await fixture.SeedAsync(study);
        return (study.Id, await AddStudySiteAsync(tenant, study.Id, ct));
    }

    private async Task<(StudySiteId StudySite, EnrollmentId Enrollment)> ScreenInNewStudyAsync(TenantId tenant, CancellationToken ct)
    {
        var (_, studySite) = await NewStudyInTenantAsync(tenant, ct);
        return (studySite, (await ScreenAsync(tenant, studySite, "SUBJ-000", Tick(), ct)).Enrollment);
    }

    private async Task<StudySiteId> AddStudySiteAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        var sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var site = new Site(SiteId.New(), tenant, sponsor, $"SITE-{suffix}", "Memorial Hospital", "US", KernelPostgresFixture.ActorId, "Seeded for test");
        var studySite = new StudySite(StudySiteId.New(), tenant, study, site.Id, "Selected", KernelPostgresFixture.ActorId, "Seeded for test");
        await fixture.SeedAsync(site, studySite);
        return studySite.Id;
    }

    private Task<ParticipantId> RegisterAsync(TenantId tenant, CancellationToken ct) =>
        _scenario.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented"), ct);

    private async Task<(ParticipantId Participant, EnrollmentId Enrollment)> ScreenAsync(
        TenantId tenant, StudySiteId studySite, string code, DateTimeOffset screenedAt, CancellationToken ct)
    {
        var participant = await RegisterAsync(tenant, ct);
        var result = await SendScreenAsync(tenant, participant, studySite, code, screenedAt, ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return (participant, result.Value!.EnrollmentId);
    }

    private Task<CommandResult<EnrollmentState>> SendScreenAsync(
        TenantId tenant, ParticipantId participant, StudySiteId studySite, string code, DateTimeOffset screenedAt, CancellationToken ct)
    {
        Tick();
        SubjectCode.TryFrom(code, out var subjectCode);
        return fixture.Sender.SendAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participant, studySite, subjectCode, screenedAt, KernelProtocolScenario.Key(), "Screening visit"),
            cancellationToken: ct);
    }

    private async Task<EnrollmentState> ChangeAsync(TenantId tenant, EnrollmentId enrollment, EnrollmentStatus to, DateTimeOffset effectiveAt, CancellationToken ct)
    {
        var result = await SendChangeAsync(tenant, enrollment, to, effectiveAt, ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value!;
    }

    private Task<CommandResult<EnrollmentState>> SendChangeAsync(
        TenantId tenant, EnrollmentId enrollment, EnrollmentStatus to, DateTimeOffset effectiveAt, CancellationToken ct)
    {
        Tick();
        return fixture.Sender.SendAsync(
            new ChangeEnrollmentStatus(tenant, enrollment, to, effectiveAt, KernelProtocolScenario.Key(), $"Moved to {to}"),
            cancellationToken: ct);
    }

    private Task<CommandResult<EnrollmentState>> SendWithdrawAsync(
        TenantId tenant, EnrollmentId enrollment, AttestationId attestation, CancellationToken ct, DateTimeOffset? effectiveAt = null,
        WithdrawalKind kind = WithdrawalKind.WithdrawalBySubject)
    {
        var at = effectiveAt ?? fixture.Clock.UtcNow;
        Tick();
        return fixture.Sender.SendAsync(
            new WithdrawParticipant(tenant, enrollment, kind, attestation, at, KernelProtocolScenario.Key(), "Consent withdrawn"),
            cancellationToken: ct);
    }

    private Task<AttestationId> AttestAsync(TenantId tenant, AttestationMeaning meaning, EnrollmentState state, CancellationToken ct)
    {
        Tick();
        return _scenario.OkAsync(
            _scenario.AttestCommand(tenant, meaning, new AttestationTarget(NodeKinds.Enrollment, state.EnrollmentId.Value, state.ContentHash)), ct);
    }

    private async Task ExecuteAsAdminAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
