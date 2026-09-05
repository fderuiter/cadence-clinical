using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Domain.Ports;

/// <summary>
/// Persistence port for <see cref="SiteComplianceCache"/>. <see cref="GetAsync"/> backs Rule 2's
/// enrollment/activation compliance gates (<c>02_technical_design.md</c> §3.2's
/// <c>SubjectApplicationService.AdvanceStateAsync</c> is the representative caller: it looks up the
/// <c>SITE_ACTIVATION</c> milestone for the subject's own site before allowing a transition into
/// <c>Enrolled</c>).
/// Replaces <c>apps/execution/adapters/repositories.py</c> (compliance-cache lookup methods).
/// @req:PRD-SUB-001
/// </summary>
public interface ISiteComplianceRepository
{
    /// <summary>
    /// Looks up the compliance-cache row for <paramref name="studyCode"/> / <paramref name="siteCode"/> /
    /// <paramref name="milestone"/>, or <see langword="null"/> if none exists — never throws.
    /// <paramref name="siteCode"/> is nullable because a <see cref="ClinicalSubject"/> may legally have no
    /// assigned site yet; since <see cref="SiteComplianceCache.SiteCode"/> is itself never null (every cache
    /// row names a definite site), a <see langword="null"/> <paramref name="siteCode"/> can never match any
    /// row and this deliberately always resolves to <see langword="null"/> in that case, rather than treating
    /// it as "any site" — a subject with no site cannot satisfy a site-specific compliance gate.
    /// </summary>
    Task<SiteComplianceCache?> GetAsync(StudyCode studyCode, SiteCode? siteCode, string milestone, CancellationToken cancellationToken);

    /// <summary>
    /// Persists all pending changes tracked by this repository's underlying
    /// <c>Infrastructure.Persistence.ExecutionDbContext</c> (the same scoped context every repository in this
    /// slice wraps), wrapped in the same <c>DbUpdateConcurrencyException</c> retry policy shared by all three
    /// repositories (<c>Infrastructure.Persistence.Repositories.ExecutionRepositoryBase</c>).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
