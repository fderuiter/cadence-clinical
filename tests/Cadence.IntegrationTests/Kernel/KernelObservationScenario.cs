using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Drives all observations and opened collection instances within <paramref name="scope"/> to clean readiness
    /// (verifying current observation revisions and signing non-empty collection instances) through the kernel command pipeline.
    /// </summary>
    public async Task MakeScopeReadyForLockAsync(TenantId tenant, StudyId study, NodeRef scope, CancellationToken ct)
    {
        List<Observation> observations;
        List<CollectionInstance> instances;
        HashSet<(ObservationId ObservationId, int? Revision)> verifiedSet;
        HashSet<(Guid TargetId, string Hash)> signedSet;

        await using (var db = fixture.CreateContext(tenant))
        {
            switch (scope.Kind)
            {
                case NodeKinds.Study:
                    instances = await db.CollectionInstances.Include(c => c.Members).AsNoTracking()
                        .Where(c => c.StudyId == study).ToListAsync(ct);
                    observations = await db.Observations.AsNoTracking()
                        .Where(o => o.StudyId == study).ToListAsync(ct);
                    break;
                case NodeKinds.StudySite:
                    var siteId = StudySiteId.FromGuid(scope.Id);
                    var siteEnrollmentIds = await db.Enrollments.AsNoTracking()
                        .Where(e => e.StudyId == study && e.StudySiteId == siteId)
                        .Select(e => e.Id)
                        .ToListAsync(ct);
                    instances = await db.CollectionInstances.Include(c => c.Members).AsNoTracking()
                        .Where(c => c.StudyId == study && c.StudySiteId == siteId).ToListAsync(ct);
                    observations = siteEnrollmentIds.Count == 0
                        ? []
                        : await db.Observations.AsNoTracking()
                            .Where(o => o.StudyId == study && siteEnrollmentIds.Contains(o.EnrollmentId)).ToListAsync(ct);
                    break;
                case NodeKinds.Enrollment:
                    var enrollmentId = EnrollmentId.FromGuid(scope.Id);
                    instances = await db.CollectionInstances.Include(c => c.Members).AsNoTracking()
                        .Where(c => c.StudyId == study && c.EnrollmentId == enrollmentId).ToListAsync(ct);
                    observations = await db.Observations.AsNoTracking()
                        .Where(o => o.StudyId == study && o.EnrollmentId == enrollmentId).ToListAsync(ct);
                    break;
                case NodeKinds.CollectionInstance:
                    var instanceId = CollectionInstanceId.FromGuid(scope.Id);
                    instances = await db.CollectionInstances.Include(c => c.Members).AsNoTracking()
                        .Where(c => c.StudyId == study && c.Id == instanceId).ToListAsync(ct);
                    var memberObsIds = instances.SelectMany(i => i.Members.Select(m => m.ObservationId)).Distinct().ToList();
                    observations = memberObsIds.Count == 0
                        ? []
                        : await db.Observations.AsNoTracking()
                            .Where(o => o.StudyId == study && memberObsIds.Contains(o.Id)).ToListAsync(ct);
                    break;
                default:
                    instances = [];
                    var obsId = ObservationId.FromGuid(scope.Id);
                    observations = await db.Observations.AsNoTracking()
                        .Where(o => o.StudyId == study && o.Id == obsId).ToListAsync(ct);
                    break;
            }

            var verifications = await db.Assertions.AsNoTracking()
                .Where(a => a.StudyId == study && a.Kind == AssertionKind.Verified && a.RetractsId == null)
                .ToListAsync(ct);
            var retractedIds = verifications.Count == 0
                ? []
                : (await db.Assertions.AsNoTracking()
                    .Where(a => a.RetractsId != null && verifications.Select(v => v.Id).Contains(a.RetractsId.Value))
                    .Select(a => a.RetractsId!.Value)
                    .ToListAsync(ct)).ToHashSet();
            verifiedSet = verifications
                .Where(v => !retractedIds.Contains(v.Id) && v.TargetKind == NodeKinds.Observation)
                .Select(v => (ObservationId.FromGuid(v.TargetId), v.TargetRevision))
                .ToHashSet();

            var instanceGuids = instances.Select(i => i.Id.Value).ToList();
            signedSet = instanceGuids.Count == 0
                ? []
                : (await db.Attestations.AsNoTracking()
                    .Where(a => a.TargetKind == NodeKinds.CollectionInstance && instanceGuids.Contains(a.TargetId))
                    .Select(a => new { a.TargetId, a.TargetContentHash })
                    .ToListAsync(ct))
                    .Select(a => (a.TargetId, a.TargetContentHash))
                    .ToHashSet();
        }

        var obsById = observations.ToDictionary(o => o.Id);
        foreach (var obs in observations)
        {
            if (obs.Value is MissingValue { Reason: NullFlavor.NotAsked })
            {
                continue;
            }

            if (!verifiedSet.Contains((obs.Id, obs.Revision)))
            {
                await Protocol.OkAsync(
                    new VerifyObservation(
                        tenant,
                        AssertionId.New(),
                        obs.EnrollmentId,
                        obs.Id,
                        obs.Revision,
                        KernelProtocolScenario.Key(),
                        "Source data verified prior to lock"),
                    ct);
            }
        }

        foreach (var instance in instances)
        {
            var entries = new List<CollectionContentEntry>();
            var hasRecordedMember = false;
            foreach (var member in instance.Members)
            {
                if (obsById.TryGetValue(member.ObservationId, out var obs))
                {
                    entries.Add(new CollectionContentEntry(obs.Id, obs.Revision, obs.Value));
                    if (obs.Value is not MissingValue { Reason: NullFlavor.NotAsked })
                    {
                        hasRecordedMember = true;
                    }
                }
            }

            if (!hasRecordedMember)
            {
                continue;
            }

            var hash = CollectionInstance.ContentHashOf(entries);
            if (!signedSet.Contains((instance.Id.Value, hash)))
            {
                await Protocol.OkAsync(
                    new SignCollectionInstance(
                        tenant,
                        AttestationId.New(),
                        instance.Id,
                        instance.EnrollmentId,
                        AttestationMeaning.Approved,
                        hash,
                        Tick(),
                        "gateway-step-up:v3",
                        KernelProtocolScenario.Key(),
                        "Investigator signed form prior to lock"),
                    ct);
            }
        }

        Tick();
    }

    /// <summary>A signed command releasing <paramref name="lockId"/>; the signature is recorded first.</summary>
    public async Task<ReleaseLock> ReleaseAsync(TenantId tenant, StudyId study, AssertionId lockId, CancellationToken ct)
    {
        var id = AssertionId.New();
        var attestation = await Protocol.OkAsync(
            Protocol.AttestCommand(tenant, AttestationMeaning.Responsibility, LockStatement.Target(id, LockStatement.Release(id, lockId))), ct);
        return new ReleaseLock(tenant, id, study, lockId, attestation, KernelProtocolScenario.Key(), "Query found after lock; unlocking to correct");
    }

    /// <summary>The command withdrawing <paramref name="enrollment"/>'s participant; the investigator's signature is recorded first.</summary>
    public async Task<WithdrawParticipant> WithdrawAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken ct)
    {
        AttestationTarget target;
        await using (var db = fixture.CreateContext(tenant))
        {
            target = (await db.Enrollments.AsNoTracking().SingleAsync(e => e.Id == enrollment, ct)).SignableTarget;
        }

        var signature = await Protocol.OkAsync(Protocol.AttestCommand(tenant, AttestationMeaning.Withdrawn, target), ct);
        return new WithdrawParticipant(
            tenant, enrollment, WithdrawalKind.WithdrawalBySubject, signature, Tick(), KernelProtocolScenario.Key(), "Participant withdrew consent");
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
