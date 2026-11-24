using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Activity;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.References;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Activity;

/// <summary>Reads projected activity. Implemented by kernel persistence over its activity projection.</summary>
public interface IActivityStore
{
    /// <summary>
    /// Up to <see cref="ActivityQuery.Limit"/> entries matching <paramref name="query"/>, newest first, unmasked; the
    /// caller masks them for the reader. Returns one extra entry when there is a next page.
    /// </summary>
    Task<IReadOnlyList<ActivityEntry>> QueryAsync(TenantId tenant, ActivityQuery query, int take, CancellationToken cancellationToken);
}

/// <summary>
/// The activity stream for the current actor (ADR-2485): reads a page from the projection and masks every entry whose
/// object the reader may not see through <see cref="INodeVisibility"/>, the same D1 contract as lineage and search.
/// </summary>
public sealed class ActivityStream(IActivityStore store, INodeVisibility visibility, ICurrentActor currentActor) : IActivityStream
{
    public async Task<ActivityPage> ReadAsync(TenantId tenant, ActivityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (tenant.IsEmpty)
        {
            throw new ArgumentException("Activity is read within a tenant.", nameof(tenant));
        }

        if (query.Limit is < 1 or > ActivityQuery.MaxLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Limit, $"Limit must be between 1 and {ActivityQuery.MaxLimit}.");
        }

        var reader = currentActor.Actor
            ?? throw new InvalidOperationException("Activity is read by an accountable actor (K-TX-5).");

        var rows = await store.QueryAsync(tenant, query, query.Limit + 1, cancellationToken).ConfigureAwait(false);
        var page = rows.Take(query.Limit).ToList();
        var next = rows.Count > query.Limit ? new ActivityCursor(page[^1].Recorded, page[^1].EventId) : null;

        var nodesToCheck = page
            .SelectMany(e => new[] { e.Subject, e.AuditEntryRef, e.AttestationRef })
            .Where(n => n is not null)
            .Select(n => n!)
            .Distinct()
            .ToList();
        var masks = nodesToCheck.Count == 0
            ? new Dictionary<NodeRef, MaskReason>()
            : await visibility.MaskAsync(tenant, reader, nodesToCheck, cancellationToken).ConfigureAwait(false);

        var entries = page
            .Select(e =>
            {
                MaskReason? reason = e.Masked;
                if (reason is null && e.Subject is { } subject && masks.TryGetValue(subject, out var subjectMask))
                {
                    reason = subjectMask;
                }
                else if (reason is null && e.AuditEntryRef is { } auditRef && masks.TryGetValue(auditRef, out var auditMask))
                {
                    reason = auditMask;
                }
                else if (reason is null && e.AttestationRef is { } attestationRef && masks.TryGetValue(attestationRef, out var attestationMask))
                {
                    reason = attestationMask;
                }

                return reason is not null
                    ? e with
                    {
                        Subject = null,
                        Masked = reason,
                        ReasonForChange = null,
                        AuditEntryRef = null,
                        AttestationRef = null,
                    }
                    : e;
            })
            .ToList();
        return new ActivityPage(entries, next);
    }
}

/// <summary>Registers the activity stream.</summary>
public static class ActivityStreamServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IActivityStream"/>. The host also registers kernel persistence's projection
    /// (<c>AddKernelActivityProjection()</c>) and an <see cref="INodeVisibility"/>.
    /// </summary>
    public static IServiceCollection AddKernelActivityStream(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IActivityStream, ActivityStream>();
        return services;
    }
}
