using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.UnitTests.Execution.Fakes;

/// <summary>
/// Slice-local in-memory fake for <see cref="IConsentRepository"/> (`MIG-SUBJ-018`), including the
/// <see cref="GetAsync"/>/<see cref="GetOtherRequiringReconsentAsync"/> tracked-read methods that ticket
/// added to the port itself. See <see cref="InMemorySubjectRepository"/>'s doc comment for why this lives
/// under <c>tests/Execution/Fakes</c> rather than <c>packages/testing/src</c>, and for the shared
/// simplifications (immediate-write <see cref="AddAsync"/>, no-op <see cref="SaveChangesAsync"/>) — a stored
/// <see cref="SubjectConsent"/> is the same in-memory instance returned by a later <see cref="GetAsync"/>,
/// so mutating it (e.g. via <c>MergeResubmission</c>) simulates EF Core's tracked-entity behavior without
/// any extra bookkeeping. Reused by `MIG-SUBJ-021` (<c>EConsentCompletedConsumer</c>'s idempotent
/// consent-cache backfill) and `MIG-SUBJ-026` (golden-master fixture replay).
/// @req:PRD-SUB-001
/// </summary>
public sealed class InMemoryConsentRepository : IConsentRepository
{
    private readonly ConcurrentDictionary<SubjectConsentId, SubjectConsent> _store = new();

    public Task<bool> ExistsAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken) =>
        Task.FromResult(_store.Values.Any(c => Matches(c, subjectCode, studyCode, versionIndex)));

    public Task<SubjectConsent?> GetAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken)
    {
        var match = _store.Values.FirstOrDefault(c => Matches(c, subjectCode, studyCode, versionIndex));
        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<SubjectConsent>> GetOtherRequiringReconsentAsync(SubjectCode subjectCode, StudyCode studyCode,
        int excludeVersionIndex, CancellationToken cancellationToken)
    {
        IReadOnlyList<SubjectConsent> others = _store.Values
            .Where(c => c.SubjectCode == subjectCode
                && c.ProtocolVersion.StudyCode == studyCode
                && c.ProtocolVersion.VersionIndex != excludeVersionIndex
                && c.RequiresReconsent)
            .ToList();
        return Task.FromResult(others);
    }

    public Task AddAsync(SubjectConsent consent, CancellationToken cancellationToken)
    {
        _store[consent.Id] = consent;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool Matches(SubjectConsent consent, SubjectCode subjectCode, StudyCode studyCode, int versionIndex) =>
        consent.SubjectCode == subjectCode
            && consent.ProtocolVersion.StudyCode == studyCode
            && consent.ProtocolVersion.VersionIndex == versionIndex;
}
