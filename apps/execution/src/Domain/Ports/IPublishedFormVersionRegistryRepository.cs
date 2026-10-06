using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Forms;

namespace Cadence.Apps.Execution.Domain.Ports;

/// <summary>
/// Port for persisting and retrieving <see cref="PublishedFormVersionRegistry"/> entries.
/// @req:PRD-SYS-053
/// </summary>
public interface IPublishedFormVersionRegistryRepository
{
    Task<PublishedFormVersionRegistry?> GetByVersionAndFormAsync(
        string studyId, string protocolVersion, string formId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PublishedFormVersionRegistry>> ListByStudyAndVersionAsync(
        string studyId, string protocolVersion, CancellationToken cancellationToken);

    Task AddOrUpdateAsync(PublishedFormVersionRegistry registry, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
