using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core adapter for <see cref="IConsentRepository"/>.
/// Replaces <c>apps/execution/adapters/repositories.py</c> (consent-lookup query methods).
/// @req:PRD-SUB-001
/// </summary>
public sealed class ConsentRepository(ExecutionDbContext context)
    : ExecutionRepositoryBase(context), IConsentRepository
{
    public Task<bool> ExistsAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken) =>
        Context.SubjectConsents.AsNoTracking().AnyAsync(
            c => c.SubjectCode == subjectCode
                && c.ProtocolVersion.StudyCode == studyCode
                && c.ProtocolVersion.VersionIndex == versionIndex,
            cancellationToken);

    // Tracked (not AsNoTracking): SubjectApplicationService.RecordConsentAsync (MIG-SUBJ-018) calls
    // SubjectConsent.MergeResubmission on the result and persists the change via a subsequent
    // SaveChangesAsync with no intervening Attach/Update call — see ISubjectRepository.GetByIdAsync's
    // identical rationale.
    public Task<SubjectConsent?> GetAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken) =>
        Context.SubjectConsents.SingleOrDefaultAsync(
            c => c.SubjectCode == subjectCode
                && c.ProtocolVersion.StudyCode == studyCode
                && c.ProtocolVersion.VersionIndex == versionIndex,
            cancellationToken);

    public async Task<IReadOnlyList<SubjectConsent>> GetOtherRequiringReconsentAsync(SubjectCode subjectCode, StudyCode studyCode,
        int excludeVersionIndex, CancellationToken cancellationToken) =>
        await Context.SubjectConsents
            .Where(c => c.SubjectCode == subjectCode
                && c.ProtocolVersion.StudyCode == studyCode
                && c.ProtocolVersion.VersionIndex != excludeVersionIndex
                && c.RequiresReconsent)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<bool> HasCurrentConsentAsync(SubjectCode subjectCode, StudyCode studyCode, CancellationToken cancellationToken)
    {
        var consents = Context.SubjectConsents.AsNoTracking()
            .Where(c => c.SubjectCode == subjectCode && c.ProtocolVersion.StudyCode == studyCode);
        bool signed = await consents.AnyAsync(c => c.IcfSigned || c.IsPaperOverride, cancellationToken).ConfigureAwait(false);
        return signed && !await consents.AnyAsync(c => c.RequiresReconsent, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(SubjectConsent consent, CancellationToken cancellationToken) =>
        await Context.SubjectConsents.AddAsync(consent, cancellationToken).ConfigureAwait(false);
}
