using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Domain.Ports;

/// <summary>
/// Persistence port for <see cref="ClinicalSubject"/>. <see cref="EnrollNextIndexAsync"/> is the single
/// shared, advisory-locked code path, run inside <see cref="ExecuteInTransactionAsync{TResult}"/>, both <c>SubjectApplicationService.CreateAsync</c>
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
    /// Computes the next sequential <c>enrollment_index</c> for <paramref name="studyCode"/>: <c>0</c> for an empty
    /// study, otherwise <c>MAX(enrollment_index) + 1</c>. It must be called inside
    /// <see cref="ExecuteInTransactionAsync{TResult}"/>, together with the <see cref="AddAsync"/> and
    /// <see cref="SaveChangesAsync"/> that insert the subject: on PostgreSQL it takes a
    /// <c>pg_advisory_xact_lock(hashtext(studyCode))</c> keyed to <paramref name="studyCode"/>, held until that
    /// transaction commits, so two enrollments in one study never read the same maximum and an enrollment in a
    /// different study is never blocked. Called outside a transaction it throws.
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

    /// <summary>
    /// Lists one page of a study's subjects, in enrollment order, without tracking them. Never throws on an
    /// empty result.
    /// </summary>
    Task<SubjectPage> ListAsync(SubjectListCriteria criteria, CancellationToken cancellationToken);

    /// <summary>Stages a new <see cref="ClinicalSubject"/> for insertion; takes effect on the next
    /// <see cref="SaveChangesAsync"/>.</summary>
    Task AddAsync(ClinicalSubject subject, CancellationToken cancellationToken);

    /// <summary>
    /// Persists all pending changes tracked by the underlying <c>ExecutionDbContext</c>, which the Execution
    /// repositories share, so any one of them can commit the unit of work. A concurrency conflict surfaces as a
    /// <c>ConcurrencyConflictException</c> (409), never as a retry.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="unitOfWork"/> in one database transaction, committed when it returns. A transient
    /// failure re-runs the whole unit of work from the start, so it must read what it needs inside the delegate.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        System.Func<CancellationToken, Task<TResult>> unitOfWork, CancellationToken cancellationToken);
}
