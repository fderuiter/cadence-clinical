using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Standards;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Persistence.Standards;

/// <summary>
/// Reads the Standards Registry and Metadata Registry through a context bound to the caller's tenant, so Row-Level
/// Security scopes every query (K-ID-2). Read-only: nothing here is tracked or saved.
/// </summary>
internal sealed class KernelStandardsRegistry(KernelTenantDbContextFactory contexts) : IStandardsRegistry
{
    public async Task<IReadOnlyList<StandardVersionSummary>> ListStandardVersionsAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var versions = await db.Set<StandardVersion>().AsNoTracking()
            .Include(v => v.Terminology)
            .OrderBy(v => v.StandardCode).ThenBy(v => v.Version)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. versions.Select(ToSummary)];
    }

    public async Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var version = await db.Set<StandardVersion>().AsNoTracking()
            .Include(v => v.Terminology)
            .SingleOrDefaultAsync(v => v.Id == id, cancellationToken).ConfigureAwait(false);
        return version is null ? null : ToSummary(version);
    }

    public async Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, string standardCode, string version, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var found = await db.Set<StandardVersion>().AsNoTracking()
            .Include(v => v.Terminology)
            .SingleOrDefaultAsync(v => v.StandardCode == standardCode && v.Version == version, cancellationToken).ConfigureAwait(false);
        return found is null ? null : ToSummary(found);
    }

    public async Task<StandardPackage?> GetStandardPackageAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var version = await db.Set<StandardVersion>().AsNoTracking().AsSplitQuery()
            .Include(v => v.Datasets).ThenInclude(d => d.Variables)
            .Include(v => v.ValueLevelDefinitions)
            .Include(v => v.Terminology)
            .SingleOrDefaultAsync(v => v.Id == id, cancellationToken).ConfigureAwait(false);
        return version?.ToPackage();
    }

    public async Task<LibraryItemView?> FindLibraryItemAsync(TenantId tenant, LibraryItemId id, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var item = await db.Set<LibraryItem>().AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false);
        return item is null ? null : ToView(item);
    }

    public async Task<IReadOnlyList<LibraryItemView>> ListLibraryItemVersionsAsync(TenantId tenant, LibraryItemKind kind, string key, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var items = await db.Set<LibraryItem>().AsNoTracking()
            .Where(i => i.Kind == kind && i.Key == key)
            .OrderByDescending(i => i.ItemVersion)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. items.Select(ToView)];
    }

    public async Task<StudyStandardsProfileView?> FindStudyStandardsAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var profile = await db.Set<StudyStandardsProfile>().AsNoTracking()
            .SingleOrDefaultAsync(p => p.StudyId == study, cancellationToken).ConfigureAwait(false);
        return profile is null
            ? null
            : new StudyStandardsProfileView(
                profile.Id, profile.StudyId, profile.VersionIndex, profile.StandardVersionIds, profile.ControlledTerminologyRefs, profile.LibraryPins);
    }

    private static StandardVersionSummary ToSummary(StandardVersion v) => new(
        v.Id,
        v.StandardCode,
        v.Version,
        v.Name,
        v.Publisher,
        v.Source,
        v.EffectiveDate,
        v.ContentHash,
        v.Status,
        v.ApprovalAttestationId,
        v.DeprecationAttestationId,
        [.. v.Terminology.Select(t => new CodeSystemVersionRef(t.CodeSystem, t.Version))
            .OrderBy(t => t.CodeSystem, StringComparer.Ordinal).ThenBy(t => t.Version, StringComparer.Ordinal)]);

    private static LibraryItemView ToView(LibraryItem i)
    {
        using var content = JsonDocument.Parse(i.Content);
        return new LibraryItemView(
            i.Id,
            i.Kind,
            i.Key,
            i.ItemVersion,
            i.Title,
            content.RootElement.Clone(),
            i.ContentHash,
            i.Status,
            i.StandardVersionIds,
            i.ApprovalAttestationId,
            i.DeprecationAttestationId)
        {
            VersionIndex = i.VersionIndex,
        };
    }
}

/// <summary>Registers kernel persistence for the Standards Registry and Metadata Registry.</summary>
public static class KernelStandardsPersistenceServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IStandardsRegistry"/>. Call after <c>AddKernelPersistence</c>.</summary>
    public static IServiceCollection AddKernelStandardsPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IStandardsRegistry, KernelStandardsRegistry>();
        return services;
    }
}
