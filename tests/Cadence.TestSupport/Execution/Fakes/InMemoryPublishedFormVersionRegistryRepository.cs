using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Ports;

namespace Cadence.TestSupport.Execution.Fakes;

/// <summary>
/// In-memory fake implementation of <see cref="IPublishedFormVersionRegistryRepository"/> for unit tests.
/// </summary>
public sealed class InMemoryPublishedFormVersionRegistryRepository : IPublishedFormVersionRegistryRepository
{
    private readonly ConcurrentDictionary<Guid, PublishedFormVersionRegistry> _store = new();

    public Task<PublishedFormVersionRegistry?> GetByVersionAndFormAsync(
        string studyId, string protocolVersion, string formId, CancellationToken cancellationToken)
    {
        var match = _store.Values.FirstOrDefault(r =>
            string.Equals(r.StudyId, studyId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.ProtocolVersion, protocolVersion, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.FormId, formId, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<PublishedFormVersionRegistry>> ListByStudyAndVersionAsync(
        string studyId, string protocolVersion, CancellationToken cancellationToken)
    {
        IReadOnlyList<PublishedFormVersionRegistry> list = _store.Values
            .Where(r =>
                string.Equals(r.StudyId, studyId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.ProtocolVersion, protocolVersion, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult(list);
    }

    public Task AddOrUpdateAsync(PublishedFormVersionRegistry registry, CancellationToken cancellationToken)
    {
        var existing = _store.Values.FirstOrDefault(r =>
            string.Equals(r.StudyId, registry.StudyId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.ProtocolVersion, registry.ProtocolVersion, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.FormId, registry.FormId, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            _store[registry.Id] = registry;
        }
        else
        {
            existing.Update(registry.FormHash, registry.OpenRosaXml, registry.DdfIr, registry.PublishedAt);
        }

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
