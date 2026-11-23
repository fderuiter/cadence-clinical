using System;
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
/// @req:PRD-SYS-192
/// </summary>
public sealed class ConsentRepository(ExecutionDbContext context)
    : ExecutionRepositoryBase(context), IConsentRepository
{
    public Task<bool> ExistsAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken)
    {
        EnsureVerifiedTenantContext();
        Guid? relationalTenantFilter = Context.ActiveRelationalTenantFilter;
        return Context.SubjectConsents.AsNoTracking().AnyAsync(
            c => c.SubjectCode == subjectCode
                && c.ProtocolVersion.StudyCode == studyCode
                && c.ProtocolVersion.VersionIndex == versionIndex
                && (relationalTenantFilter == null || c.TenantId == relationalTenantFilter),
            cancellationToken);
    }

    // Tracked (not AsNoTracking): SubjectApplicationService.RecordConsentAsync (MIG-SUBJ-018) calls
    // SubjectConsent.MergeResubmission on the result and persists the change via a subsequent
    // SaveChangesAsync with no intervening Attach/Update call — see ISubjectRepository.GetByIdAsync's
    // identical rationale.
    public Task<SubjectConsent?> GetAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken)
    {
        EnsureVerifiedTenantContext();
        Guid? relationalTenantFilter = Context.ActiveRelationalTenantFilter;
        return Context.SubjectConsents.SingleOrDefaultAsync(
            c => c.SubjectCode == subjectCode
                && c.ProtocolVersion.StudyCode == studyCode
                && c.ProtocolVersion.VersionIndex == versionIndex
                && (relationalTenantFilter == null || c.TenantId == relationalTenantFilter),
            cancellationToken);
    }

    public async Task<IReadOnlyList<SubjectConsent>> GetOtherRequiringReconsentAsync(SubjectCode subjectCode, StudyCode studyCode,
        int excludeVersionIndex, CancellationToken cancellationToken)
    {
        EnsureVerifiedTenantContext();
        Guid? relationalTenantFilter = Context.ActiveRelationalTenantFilter;
        return await Context.SubjectConsents
            .Where(c => c.SubjectCode == subjectCode
                && c.ProtocolVersion.StudyCode == studyCode
                && c.ProtocolVersion.VersionIndex != excludeVersionIndex
                && c.RequiresReconsent
                && (relationalTenantFilter == null || c.TenantId == relationalTenantFilter))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> HasCurrentConsentAsync(SubjectCode subjectCode, StudyCode studyCode, CancellationToken cancellationToken)
    {
        EnsureVerifiedTenantContext();
        Guid? relationalTenantFilter = Context.ActiveRelationalTenantFilter;
        var consents = Context.SubjectConsents.AsNoTracking()
            .Where(c => c.SubjectCode == subjectCode
                && c.ProtocolVersion.StudyCode == studyCode
                && (relationalTenantFilter == null || c.TenantId == relationalTenantFilter));
        bool signed = await consents.AnyAsync(c => c.IcfSigned || c.IsPaperOverride, cancellationToken).ConfigureAwait(false);
        return signed && !await consents.AnyAsync(c => c.RequiresReconsent, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(SubjectConsent consent, CancellationToken cancellationToken)
    {
        EnsureVerifiedTenantContext();
        if (Context.ActiveBoundTenantId is { } tenantId)
        {
            consent.TenantId = tenantId;
        }

        await Context.SubjectConsents.AddAsync(consent, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureVerifiedTenantContext()
    {
        if (Context.HasUnverifiedRequestTenant)
        {
            throw new InvalidOperationException("Verified workspace tenant context is required for consent persistence.");
        }
    }
}
