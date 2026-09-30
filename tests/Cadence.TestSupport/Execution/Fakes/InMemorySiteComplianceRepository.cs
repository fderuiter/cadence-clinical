using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.TestSupport.Execution.Fakes;

/// <summary>
/// Slice-local in-memory fake for <see cref="ISiteComplianceRepository"/>, added by `MIG-SUBJ-018` alongside
/// <see cref="InMemorySubjectRepository"/>/<see cref="InMemoryConsentRepository"/> purely to satisfy
/// <c>SubjectApplicationService</c>'s primary-constructor shape in tests that never exercise the Rule 2
/// compliance gate (<c>CreateAsync</c>/<c>RecordConsentAsync</c> never call this port at all). <see cref="Seed"/>
/// lets `MIG-SUBJ-019`'s <c>AdvanceStateAsync</c>/SCN-005 tests and `MIG-SUBJ-020`'s
/// <c>SiteComplianceApplicationService</c> tests preconfigure rows. See
/// <see cref="InMemorySubjectRepository"/>'s doc comment for why this lives under
/// <c>tests/Cadence.TestSupport</c> rather than <c>packages/testing/src</c>.
/// </summary>
public sealed class InMemorySiteComplianceRepository : ISiteComplianceRepository
{
    private readonly ConcurrentDictionary<Guid, SiteComplianceCache> _store = new();

    public void Seed(SiteComplianceCache cache) => _store[cache.Id] = cache;

    public Task<SiteComplianceCache?> GetAsync(StudyCode studyCode, SiteCode? siteCode, string milestone, CancellationToken cancellationToken)
    {
        if (siteCode is not { } site)
        {
            return Task.FromResult<SiteComplianceCache?>(null);
        }

        var match = _store.Values.FirstOrDefault(c => c.StudyCode == studyCode && c.SiteCode == site && c.Milestone == milestone);
        return Task.FromResult(match);
    }

    public Task AddAsync(SiteComplianceCache cache, CancellationToken cancellationToken)
    {
        _store[cache.Id] = cache;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
