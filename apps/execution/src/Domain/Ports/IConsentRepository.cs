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
/// conditions (<c>02_technical_design.md</c> §5.2).
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
