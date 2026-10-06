using System;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Kernel.Events;
using Cadence.Kernel.Time;

using MassTransit;

using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Execution.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for <see cref="FormVersionPublished"/> events.
/// Upserts <see cref="PublishedFormVersionRegistry"/> entries idempotently upon protocol approval in Kernel.
/// @req:PRD-SYS-053
/// </summary>
public sealed class FormVersionPublishedConsumer(
    IPublishedFormVersionRegistryRepository repository,
    IClock? clock = null,
    ILogger<FormVersionPublishedConsumer>? logger = null)
    : IConsumer<FormVersionPublished>
{
    public async Task Consume(ConsumeContext<FormVersionPublished> context)
    {
        var msg = context.Message;
        var studyIdStr = msg.StudyId.Value.ToString();
        var tenantIdStr = msg.TenantId.Value.ToString();
        var protocolVersionIdStr = msg.ProtocolVersionId.Value.ToString();

        if (string.IsNullOrWhiteSpace(studyIdStr) ||
            string.IsNullOrWhiteSpace(msg.ProtocolVersion) ||
            string.IsNullOrWhiteSpace(msg.FormId))
        {
            logger?.LogWarning("Dropped malformed FormVersionPublished event.");
            return;
        }

        var publishedAt = (clock ?? SystemClock.Instance).UtcNow;

        var registry = new PublishedFormVersionRegistry(
            Guid.NewGuid(),
            tenantIdStr,
            studyIdStr,
            protocolVersionIdStr,
            msg.ProtocolVersion,
            msg.FormId,
            msg.FormHash,
            msg.OpenRosaXml,
            msg.DdfIr,
            publishedAt);

        await repository.AddOrUpdateAsync(registry, context.CancellationToken).ConfigureAwait(false);
        await repository.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);

        logger?.LogInformation(
            "Registered form version snapshot for study '{StudyId}', protocol version '{ProtocolVersion}', form '{FormId}' with hash '{FormHash}'",
            studyIdStr, msg.ProtocolVersion, msg.FormId, msg.FormHash);
    }
}
