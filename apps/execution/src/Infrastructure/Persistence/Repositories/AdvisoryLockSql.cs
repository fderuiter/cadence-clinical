using System;

using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// Builds the PostgreSQL advisory-lock statement <see cref="SubjectRepository.EnrollNextIndexAsync"/> executes
/// via <c>ExecuteSqlInterpolatedAsync</c> — which safely parameterizes the interpolation hole rather than
/// concatenating it into the SQL text, so <c>studyCode</c>'s value is always sent as a bound
/// parameter, never inlined as a literal. Factored out into its own pure, connection-free method specifically
/// so the "the lock key is derived from the study code, not a global constant" boundary-check acceptance
/// criterion can be asserted with a plain unit test against the returned <see cref="FormattableString"/>'s
/// arguments, without needing a real (or even a suppressed/faked) PostgreSQL connection — advisory locks are a
/// PostgreSQL-only concept and cannot be exercised against SQLite/InMemory.
/// @req:PRD-SUB-001
/// </summary>
public static class AdvisoryLockSql
{
    public static FormattableString ForStudy(StudyCode studyCode) =>
        $"SELECT pg_advisory_xact_lock(hashtext({studyCode.Value}))";
}
