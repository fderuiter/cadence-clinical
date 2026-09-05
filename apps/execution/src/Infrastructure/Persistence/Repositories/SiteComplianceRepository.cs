using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;

using Polly;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core adapter for <see cref="ISiteComplianceRepository"/>.
/// Replaces <c>apps/execution/adapters/repositories.py</c> (compliance-cache lookup methods).
/// @req:PRD-SUB-001
/// </summary>
public sealed class SiteComplianceRepository(ExecutionDbContext context, ResiliencePipeline saveChangesResilience)
    : ExecutionRepositoryBase(context, saveChangesResilience), ISiteComplianceRepository
{
    public Task<SiteComplianceCache?> GetAsync(
        StudyCode studyCode, SiteCode? siteCode, string milestone, CancellationToken cancellationToken) =>
        Context.SiteComplianceCaches.AsNoTracking().FirstOrDefaultAsync(
            c => c.StudyCode == studyCode
                && c.Milestone == milestone
                && siteCode.HasValue && c.SiteCode == siteCode.Value,
            cancellationToken);
}
