using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Domain.Ports;

/// <summary>
/// Persistence port for <see cref="SubjectConsent"/>. <see cref="ExistsAsync"/> backs
/// <c>EConsentCompletedConsumer</c>'s (MIG-SUBJ-021) independent consent-cache-backfill check — it is
/// deliberately separate from, and does not participate in, <see cref="Ports.ISubjectRepository"/>'s
/// subject-existence idempotency check, since the design's consumer flow treats "does the subject exist" and
/// "does the consent row for this exact protocol version exist" as two independent, individually-idempotent
/// conditions (<c>02_technical_design.md</c> §5.2). <see cref="GetAsync"/> and
/// <see cref="GetOtherRequiringReconsentAsync"/> were added by <c>SubjectApplicationService.RecordConsentAsync</c>
/// (MIG-SUBJ-018): the legacy <c>record_consent</c> this slice replaces reads the actual row before deciding
/// merge-vs-create (<c>consent_repo.get_subject_consent</c>) and enumerates every other row for the Rule 3
/// cascade (<c>consent_repo.list_subject_consents</c>) — a boolean-only <see cref="ExistsAsync"/> cannot back
/// either of those, so this port grew the two tracked-read methods actually needed, rather than MIG-SUBJ-018
/// inventing a second query path outside this repository.
/// Replaces <c>apps/execution/adapters/repositories.py</c> (consent-lookup query methods).
/// @req:PRD-SUB-001
/// </summary>
public interface IConsentRepository
{
    /// <summary>
    /// Returns whether a <see cref="SubjectConsent"/> row already exists for the exact
    /// <paramref name="subjectCode"/> / <paramref name="studyCode"/> / <paramref name="versionIndex"/>
    /// combination (matched against the <see cref="ProtocolVersionRef"/> complex-type members). Never
    /// throws on a miss — simply returns <see langword="false"/>.
    /// </summary>
    Task<bool> ExistsAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up the tracked <see cref="SubjectConsent"/> row for the exact <paramref name="subjectCode"/> /
    /// <paramref name="studyCode"/> / <paramref name="versionIndex"/> combination, or <see langword="null"/>
    /// if none exists — never throws. Tracked (not <c>AsNoTracking()</c>) so
    /// <c>SubjectApplicationService.RecordConsentAsync</c> (MIG-SUBJ-018) can call
    /// <see cref="SubjectConsent.MergeResubmission"/> on the result and persist the change via a subsequent
    /// <see cref="SaveChangesAsync"/> with no explicit re-attach — the same tracked-entity contract
    /// <see cref="Ports.ISubjectRepository.GetByIdAsync"/> follows.
    /// </summary>
    Task<SubjectConsent?> GetAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every <i>other</i> tracked <see cref="SubjectConsent"/> row for <paramref name="subjectCode"/> /
    /// <paramref name="studyCode"/> (i.e. excluding <paramref name="excludeVersionIndex"/>) whose
    /// <see cref="SubjectConsent.RequiresReconsent"/> is <see langword="true"/> — backs Rule 3's cascade
    /// clearance (<c>SubjectApplicationService.RecordConsentAsync</c>, MIG-SUBJ-018, SCN-002). Tracked, for
    /// the same reason as <see cref="GetAsync"/>. Scoped to <paramref name="subjectCode"/> so a different
    /// subject's rows are never touched, even if they share the same <paramref name="studyCode"/>.
    /// </summary>
    Task<IReadOnlyList<SubjectConsent>> GetOtherRequiringReconsentAsync(SubjectCode subjectCode, StudyCode studyCode,
        int excludeVersionIndex, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the participant holds a current informed consent in the study: at least one consent signed (or recorded
    /// on paper) and none waiting for re-consent. Randomisation is refused without one (#5607, ICH E6(R3) 2.8).
    /// </summary>
    Task<bool> HasCurrentConsentAsync(SubjectCode subjectCode, StudyCode studyCode, CancellationToken cancellationToken);

    /// <summary>Stages a new <see cref="SubjectConsent"/> for insertion; takes effect on the next
    /// <see cref="SaveChangesAsync"/>.</summary>
    Task AddAsync(SubjectConsent consent, CancellationToken cancellationToken);

    /// <summary>
    /// Persists all pending changes tracked by this repository's underlying
    /// <c>Infrastructure.Persistence.ExecutionDbContext</c> (the same scoped context every repository in this
    /// slice wraps), wrapped in the same <c>DbUpdateConcurrencyException</c> retry policy shared by all three
    /// repositories (<c>Infrastructure.Persistence.Repositories.ExecutionRepositoryBase</c>).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
