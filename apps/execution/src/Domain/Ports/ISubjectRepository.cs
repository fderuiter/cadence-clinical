using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Domain.Ports;

/// <summary>
/// Persistence port for <see cref="ClinicalSubject"/>. <see cref="EnrollNextIndexAsync"/> is the single
/// shared, transaction-scoped, advisory-locked code path both <c>SubjectApplicationService.CreateAsync</c>
/// (MIG-SUBJ-018) and <c>EConsentCompletedConsumer</c> (MIG-SUBJ-021) must call, closing the identical
/// sequential-enrollment-index race <c>01_spec.md</c> flags independently for both legacy call sites
/// (<c>02_technical_design.md</c> §4.2). <see cref="GetByIdAsync"/> and <see cref="GetByCodeAsync"/> are
/// deliberately kept separate — the dual-lookup <i>sequencing</i> (try the surrogate id first, fall back to
/// the domain code) is the application service's responsibility (MIG-SUBJ-018/019), not this repository's;
/// folding that fallback in here would prevent <c>EConsentCompletedConsumer</c>'s idempotency check from
/// using the code-only lookup in isolation.
/// Replaces <c>apps/execution/adapters/repositories.py</c> /
/// <c>apps/execution/infrastructure/repositories/execution_repositories.py</c> (<c>get_max_enrollment_index</c>,
/// dual-lookup query methods).
/// @req:PRD-SUB-001
/// </summary>
public interface ISubjectRepository
{
    /// <summary>
    /// Atomically computes the next sequential <c>enrollment_index</c> for <paramref name="studyCode"/> —
    /// <c>0</c> for an empty study, otherwise <c>MAX(enrollment_index) + 1</c> — inside a
    /// <see cref="System.Data.IsolationLevel.RepeatableRead"/> transaction serialized, on PostgreSQL, by a
    /// <c>pg_advisory_xact_lock(hashtext(studyCode))</c> keyed specifically to <paramref name="studyCode"/>,
    /// so a concurrent enrollment for a <i>different</i> study is never blocked behind this one. The
    /// transaction opened here stays live on this repository instance until the caller's subsequent
    /// <see cref="AddAsync"/> + <see cref="SaveChangesAsync"/> completes (that call commits it) — the lock is
    /// held for exactly the read-then-insert span, not any longer.
    /// </summary>
    Task<int> EnrollNextIndexAsync(StudyCode studyCode, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up by the surrogate primary key. <paramref name="id"/> is the raw caller-supplied "id-or-code"
    /// string; a value that does not parse as a <see cref="SubjectId"/> (<see cref="System.Guid"/>) can never
    /// match any row and returns <see langword="null"/> rather than throwing, matching
    /// <see cref="GetByCodeAsync"/>'s own never-throws-on-miss contract. Not-found handling (raising
    /// <c>SubjectErrors.NotFound</c>) is the application service's responsibility, not this repository's.
    /// The returned entity is tracked (not <c>AsNoTracking()</c>) so a caller can mutate it and persist the
    /// change via a subsequent <see cref="SaveChangesAsync"/> without an explicit re-attach.
    /// </summary>
    Task<ClinicalSubject?> GetByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up by the domain <see cref="SubjectCode"/>. Returns <see langword="null"/> on a miss — never
    /// throws — so callers such as <c>EConsentCompletedConsumer</c> can use this in isolation as an
    /// idempotency check without paying for the id-then-code fallback sequencing. Tracked, for the same
    /// reason as <see cref="GetByIdAsync"/>.
    /// </summary>
    Task<ClinicalSubject?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>Stages a new <see cref="ClinicalSubject"/> for insertion; takes effect on the next
    /// <see cref="SaveChangesAsync"/>.</summary>
    Task AddAsync(ClinicalSubject subject, CancellationToken cancellationToken);

    /// <summary>
    /// Persists all pending changes tracked by this repository's underlying
    /// <c>Infrastructure.Persistence.ExecutionDbContext</c> (shared across all three repositories in this
    /// slice — any one of them can be used as the unit-of-work commit point), completing (committing) any
    /// transaction <see cref="EnrollNextIndexAsync"/> left open, and wrapped in a
    /// <c>DbUpdateConcurrencyException</c> retry policy shared by all three repositories
    /// (<c>Infrastructure.Persistence.Repositories.ExecutionRepositoryBase</c>).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
