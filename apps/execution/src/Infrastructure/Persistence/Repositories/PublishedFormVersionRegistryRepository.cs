using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Ports;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository adapter for <see cref="PublishedFormVersionRegistry"/>.
/// </summary>
public sealed class PublishedFormVersionRegistryRepository(ExecutionDbContext context)
    : ExecutionRepositoryBase(context), IPublishedFormVersionRegistryRepository
{
    public async Task<PublishedFormVersionRegistry?> GetByVersionAndFormAsync(
        string studyId, string protocolVersion, string formId, CancellationToken cancellationToken) =>
        await Context.PublishedFormVersionRegistries
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.StudyId == studyId && r.ProtocolVersion == protocolVersion && r.FormId == formId, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<PublishedFormVersionRegistry>> ListByStudyAndVersionAsync(
        string studyId, string protocolVersion, CancellationToken cancellationToken)
    {
        var list = await Context.PublishedFormVersionRegistries
            .AsNoTracking()
            .Where(r => r.StudyId == studyId && r.ProtocolVersion == protocolVersion)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return list;
    }

    public async Task AddOrUpdateAsync(PublishedFormVersionRegistry registry, CancellationToken cancellationToken)
    {
        var existing = await Context.PublishedFormVersionRegistries
            .FirstOrDefaultAsync(r => r.StudyId == registry.StudyId && r.ProtocolVersion == registry.ProtocolVersion && r.FormId == registry.FormId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await Context.PublishedFormVersionRegistries.AddAsync(registry, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            existing.Update(registry.FormHash, registry.OpenRosaXml, registry.DdfIr, registry.PublishedAt);
        }
    }
}
