using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;

using Polly;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core adapter for <see cref="IConsentRepository"/>.
/// Replaces <c>apps/execution/adapters/repositories.py</c> (consent-lookup query methods).
/// @req:PRD-SUB-001
/// </summary>
public sealed class ConsentRepository(ExecutionDbContext context, ResiliencePipeline saveChangesResilience)
    : ExecutionRepositoryBase(context, saveChangesResilience), IConsentRepository
{
    public Task<bool> ExistsAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken) =>
        Context.SubjectConsents.AsNoTracking().AnyAsync(
            c => c.SubjectCode == subjectCode
                && c.ProtocolVersion.StudyCode == studyCode
                && c.ProtocolVersion.VersionIndex == versionIndex,
            cancellationToken);

    public async Task AddAsync(SubjectConsent consent, CancellationToken cancellationToken) =>
        await Context.SubjectConsents.AddAsync(consent, cancellationToken).ConfigureAwait(false);
}
