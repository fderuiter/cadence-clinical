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
        TenantId tenant, EnrollmentId enrollment, AttestationId attestation, CancellationToken ct, DateTimeOffset? effectiveAt = null)
    {
        var at = effectiveAt ?? fixture.Clock.UtcNow;
        Tick();
        return fixture.Sender.SendAsync(
            new WithdrawParticipant(tenant, enrollment, WithdrawalKind.WithdrawalBySubject, attestation, at, KernelProtocolScenario.Key(), "Consent withdrawn"),
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
