using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

using Microsoft.EntityFrameworkCore;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Builds observation scenarios through the production pipeline: an enrollment in a new tenant, recorded and corrected
/// observations, and signed locks and releases (ADR-2620, ADR-2621).
/// </summary>
internal sealed class KernelObservationScenario(KernelPostgresFixture fixture)
{
    public const string EntryReason = "Initial entry";
    public const string CorrectionReason = "Transcription error: source document reads 128";

    public static readonly ConceptRef SystolicBloodPressure = new(ConceptId.New(), 1);

    public KernelProtocolScenario Protocol { get; } = new(fixture);

    /// <summary>An enrollment, with its study site and study, in a new tenant.</summary>
    public async Task<Subject> NewEnrollmentAsync(CancellationToken ct)
    {
        var (tenant, study) = await Protocol.NewStudyAsync(ct);
        var studySite = await AddStudySiteAsync(tenant, study, ct);
        return new Subject(tenant, study, studySite, await ScreenAsync(tenant, studySite, ct));
    }

    public async Task<ObservationState> RecordAsync(TenantId tenant, EnrollmentId enrollment, ObservationValue value, CancellationToken ct) =>
        await Protocol.OkAsync(Record(tenant, enrollment, value), ct);

    public RecordObservation Record(TenantId tenant, EnrollmentId enrollment, ObservationValue value) =>
        new(tenant, ObservationId.New(), enrollment, SystolicBloodPressure, value, Tick().AddMinutes(-5), KernelProtocolScenario.Key(), EntryReason);

    public static CorrectObservation Correct(TenantId tenant, ObservationState observation, ObservationValue value, long expectedVersion = 1) =>
        new(tenant, observation.ObservationId, observation.EnrollmentId, value, expectedVersion, KernelProtocolScenario.Key(), CorrectionReason);

    public static QuantityValue Mmhg(decimal value) => new(value, "mm[Hg]");

    /// <summary>A signed command applying a <paramref name="kind"/> lock at <paramref name="scope"/>; the signature is recorded first.</summary>
    public async Task<ApplyLock> LockAsync(TenantId tenant, StudyId study, NodeRef scope, CancellationToken ct, LockKind kind = LockKind.Locked)
    {
        var id = AssertionId.New();
        var attestation = await Protocol.OkAsync(
            Protocol.AttestCommand(tenant, AttestationMeaning.Approved, LockStatement.Target(id, LockStatement.Apply(id, kind, scope))), ct);
        return new ApplyLock(tenant, id, study, scope, kind, attestation, KernelProtocolScenario.Key(), "Data review complete; locking the scope");
    }

    /// <summary>A signed command releasing <paramref name="lockId"/>; the signature is recorded first.</summary>
    public async Task<ReleaseLock> ReleaseAsync(TenantId tenant, StudyId study, AssertionId lockId, CancellationToken ct)
    {
        var id = AssertionId.New();
        var attestation = await Protocol.OkAsync(
            Protocol.AttestCommand(tenant, AttestationMeaning.Responsibility, LockStatement.Target(id, LockStatement.Release(id, lockId))), ct);
        return new ReleaseLock(tenant, id, study, lockId, attestation, KernelProtocolScenario.Key(), "Query found after lock; unlocking to correct");
    }

    public DateTimeOffset Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));

    public async Task<StudySiteId> AddStudySiteAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        var sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var site = new Site(SiteId.New(), tenant, sponsor, $"SITE-{suffix}", "Memorial Hospital", "US", KernelPostgresFixture.ActorId, "Seeded for test");
        var studySite = new StudySite(StudySiteId.New(), tenant, study, site.Id, "Selected", KernelPostgresFixture.ActorId, "Seeded for test");
        await fixture.SeedAsync(site, studySite);
        return studySite.Id;
    }

    public async Task<EnrollmentId> ScreenAsync(TenantId tenant, StudySiteId studySite, CancellationToken ct)
    {
        var participant = await Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented"), ct);
        SubjectCode.TryFrom($"S-{Guid.NewGuid():N}"[..12].ToUpperInvariant(), out var code);
        var enrollment = await Protocol.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participant, studySite, code, Tick().AddDays(-1), KernelProtocolScenario.Key(), "Screening visit"), ct);
        return enrollment.EnrollmentId;
    }

    /// <summary>An enrollment and the scopes above it.</summary>
    internal sealed record Subject(TenantId Tenant, StudyId Study, StudySiteId StudySite, EnrollmentId Enrollment)
    {
        /// <summary>The lock scope of <paramref name="kind"/> covering <paramref name="observation"/>.</summary>
        public NodeRef Scope(string kind, ObservationId observation) => kind switch
        {
            NodeKinds.Study => NodeRef.For(Study),
            NodeKinds.StudySite => NodeRef.For(StudySite),
            NodeKinds.Enrollment => NodeRef.For(Enrollment),
            _ => NodeRef.For(observation),
        };
    }
}
