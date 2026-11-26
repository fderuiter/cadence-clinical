using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Rules.Amendment;
using Cadence.Rules.Packs;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Designer.Endpoints;

/// <summary>
/// Study-scoped protocol amendment rule-pack regression diff endpoints.
/// </summary>
public static class RuleAmendmentRegressionEndpoints
{
    public static RouteGroupBuilder MapRuleAmendmentRegressionEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/amendment-regression", RunAmendmentRegressionAsync)
            .WithName("AmendmentRegression")
            .WithSummary("Compares baseline and target rule pack versions during protocol amendments to highlight rule diffs and broken dependencies.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(DesignerPermissions.CompileProtocol);

        return api;
    }

    public static async Task<Results<Ok<AmendmentRegressionReport>, ProblemHttpResult>> RunAmendmentRegressionAsync(
        string id,
        AmendmentRegressionApiRequest? request,
        AmendmentRegressionEngine regressionEngine,
        RulePackLoader loader,
        IClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var req = request ?? new AmendmentRegressionApiRequest();
        var tenant = RuleBatchEndpoints.ResolveVerifiedTenantId(httpContext);
        var studyId = RuleBatchEndpoints.ResolveStudyId(tenant, id);
        var records = req.Records ?? [];
        var registry = httpContext.RequestServices.GetService<IStandardsRegistry>();

        var (baselineLoaded, targetLoaded, packError) = await ResolveRegressionPacksAsync(
            tenant,
            studyId,
            req,
            loader,
            registry,
            cancellationToken).ConfigureAwait(false);
        if (packError is not null || baselineLoaded is null || targetLoaded is null)
        {
            return packError ?? RuleBatchEndpoints.UnprocessablePackProblem(RuleErrors.NoRulePacks.Message);
        }

        var datasetSchemas = await ResolveStudyDatasetSchemasAsync(tenant, studyId, registry, cancellationToken).ConfigureAwait(false);

        var report = await regressionEngine.CompareAsync(
            baselineLoaded,
            targetLoaded,
            ValidationStage.Collection,
            DateOnly.FromDateTime(clock.UtcNow.DateTime),
            records,
            datasetSchemas,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(report);
    }

    private static async Task<(LoadedRulePack? Baseline, LoadedRulePack? Target, ProblemHttpResult? Error)> ResolveRegressionPacksAsync(
        TenantId tenant,
        StudyId studyId,
        AmendmentRegressionApiRequest request,
        RulePackLoader loader,
        IStandardsRegistry? registry,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LoadedRulePack>? pinnedCache = null;
        async Task<(IReadOnlyList<LoadedRulePack> Packs, ProblemHttpResult? Error)> GetPinnedAsync()
        {
            if (pinnedCache is not null)
            {
                return (pinnedCache, null);
            }

            try
            {
                pinnedCache = await loader.LoadForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
                return (pinnedCache, null);
            }
            catch (InvalidOperationException ex)
            {
                return ([], RuleBatchEndpoints.UnprocessablePackProblem(ex.Message));
            }
        }

        LoadedRulePack? resolvedBaseline = null;
        if (request.BaselinePack is null)
        {
            var (pinned, pinnedError) = await GetPinnedAsync().ConfigureAwait(false);
            if (pinnedError is not null)
            {
                return (null, null, pinnedError);
            }

            var (loaded, loadError) = await ResolveStoredPackVersionAsync(
                tenant,
                request.BaselineVersion,
                pinned,
                loader,
                registry,
                cancellationToken).ConfigureAwait(false);
            if (loadError is not null)
            {
                return (null, null, loadError);
            }

            resolvedBaseline = loaded;
        }

        LoadedRulePack? resolvedTarget = null;
        if (request.TargetPack is null)
        {
            var (pinned, pinnedError) = await GetPinnedAsync().ConfigureAwait(false);
            if (pinnedError is not null)
            {
                return (null, null, pinnedError);
            }

            var (loaded, loadError) = await ResolveStoredPackVersionAsync(
                tenant,
                request.TargetVersion,
                pinned,
                loader,
                registry,
                cancellationToken).ConfigureAwait(false);
            if (loadError is not null)
            {
                return (null, null, loadError);
            }

            resolvedTarget = loaded;
        }

        if ((request.BaselinePack is null && resolvedBaseline is null)
            || (request.TargetPack is null && resolvedTarget is null))
        {
            return (null, null, RuleBatchEndpoints.UnprocessablePackProblem(RuleErrors.NoRulePacks.Message));
        }

        var sharedKey = DeriveSharedRegressionPackKey(studyId, resolvedBaseline, resolvedTarget, request.BaselinePack, request.TargetPack);

        if (resolvedBaseline is null)
        {
            if (!RuleBatchEndpoints.TryParsePackVersion(request.BaselineVersion, defaultVersion: 1, out var baselineVersion))
            {
                return (null, null, RuleBatchEndpoints.UnprocessablePackProblem($"Baseline version '{request.BaselineVersion}' must be a positive integer or library item UUID."));
            }

            var (canonicalBaseline, baselineHash, baselineError) = RuleBatchEndpoints.ValidateAndHashInlinePack(request.BaselinePack!);
            if (baselineError is not null || canonicalBaseline is null)
            {
                return (null, null, baselineError ?? RuleBatchEndpoints.UnprocessablePackProblem(RuleErrors.PackInvalid.Message));
            }

            var baselineId = RuleBatchEndpoints.DeriveLibraryItemId(tenant, studyId, sharedKey, baselineVersion, baselineHash);
            resolvedBaseline = new LoadedRulePack(baselineId, sharedKey, baselineVersion, baselineHash, GovernanceStatus.Approved, canonicalBaseline);
        }
        else if (!string.Equals(resolvedBaseline.Key, sharedKey, StringComparison.Ordinal))
        {
            resolvedBaseline = resolvedBaseline with { Key = sharedKey };
        }

        if (resolvedTarget is null)
        {
            if (!RuleBatchEndpoints.TryParsePackVersion(request.TargetVersion, defaultVersion: 2, out var targetVersion))
            {
                return (null, null, RuleBatchEndpoints.UnprocessablePackProblem($"Target version '{request.TargetVersion}' must be a positive integer or library item UUID."));
            }

            var (canonicalTarget, targetHash, targetError) = RuleBatchEndpoints.ValidateAndHashInlinePack(request.TargetPack!);
            if (targetError is not null || canonicalTarget is null)
            {
                return (null, null, targetError ?? RuleBatchEndpoints.UnprocessablePackProblem(RuleErrors.PackInvalid.Message));
            }

            var targetId = RuleBatchEndpoints.DeriveLibraryItemId(tenant, studyId, sharedKey, targetVersion, targetHash);
            resolvedTarget = new LoadedRulePack(targetId, sharedKey, targetVersion, targetHash, GovernanceStatus.Approved, canonicalTarget);
        }
        else if (!string.Equals(resolvedTarget.Key, sharedKey, StringComparison.Ordinal))
        {
            resolvedTarget = resolvedTarget with { Key = sharedKey };
        }

        return (resolvedBaseline, resolvedTarget, null);
    }

    private static async Task<(LoadedRulePack? Pack, ProblemHttpResult? Error)> ResolveStoredPackVersionAsync(
        TenantId tenant,
        string? versionSpec,
        IReadOnlyList<LoadedRulePack> pinnedPacks,
        RulePackLoader loader,
        IStandardsRegistry? registry,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(versionSpec)
            && LibraryItemId.TryParse(versionSpec.Trim(), out var itemId)
            && !itemId.IsEmpty)
        {
            var result = await loader.LoadAsync(tenant, itemId, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? (result.Pack, null)
                : (null, RuleBatchEndpoints.UnprocessablePackProblem(result.Error!.Message));
        }

        if (pinnedPacks.Count == 0)
        {
            return (null, RuleBatchEndpoints.UnprocessablePackProblem(RuleErrors.NoRulePacks.Message));
        }

        if (string.IsNullOrWhiteSpace(versionSpec))
        {
            return (pinnedPacks[0], null);
        }

        if (!RuleBatchEndpoints.TryParsePackVersion(versionSpec, defaultVersion: 0, out var requestedVersion))
        {
            return (null, RuleBatchEndpoints.UnprocessablePackProblem($"Rule pack version '{versionSpec}' must be a positive integer or library item UUID."));
        }

        var directMatch = pinnedPacks.FirstOrDefault(p => p.Version == requestedVersion);
        if (directMatch is not null)
        {
            return (directMatch, null);
        }

        if (registry is not null)
        {
            var versions = await registry.ListLibraryItemVersionsAsync(
                tenant,
                LibraryItemKind.RulePack,
                pinnedPacks[0].Key,
                cancellationToken).ConfigureAwait(false);
            var matchedItem = versions.FirstOrDefault(v => v.Version == requestedVersion);
            if (matchedItem is not null)
            {
                var loaded = await loader.LoadAsync(tenant, matchedItem.Id, cancellationToken).ConfigureAwait(false);
                return loaded.IsSuccess
                    ? (loaded.Pack, null)
                    : (null, RuleBatchEndpoints.UnprocessablePackProblem(loaded.Error!.Message));
            }
        }

        return (null, RuleBatchEndpoints.UnprocessablePackProblem($"Pinned rule pack '{pinnedPacks[0].Key}' version {requestedVersion} was not found."));
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> ResolveStudyDatasetSchemasAsync(
        TenantId tenant,
        StudyId studyId,
        IStandardsRegistry? registry,
        CancellationToken cancellationToken)
    {
        if (registry is null)
        {
            return null;
        }

        var profile = await registry.FindStudyStandardsAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        if (profile is null || profile.StandardVersions.Count == 0)
        {
            return null;
        }

        var packages = new List<StandardPackage>(profile.StandardVersions.Count);
        foreach (var standardVersionId in profile.StandardVersions)
        {
            if (await registry.GetStandardPackageAsync(tenant, standardVersionId, cancellationToken).ConfigureAwait(false) is { } pkg)
            {
                packages.Add(pkg);
            }
        }

        return packages.Count > 0 ? AmendmentRegressionEngine.BuildDatasetSchemas(packages) : null;
    }

    private static string DeriveSharedRegressionPackKey(
        StudyId studyId,
        LoadedRulePack? resolvedBaseline,
        LoadedRulePack? resolvedTarget,
        RulePack? inlineBaseline,
        RulePack? inlineTarget)
    {
        if (resolvedBaseline is not null)
        {
            return resolvedBaseline.Key;
        }

        if (resolvedTarget is not null)
        {
            return resolvedTarget.Key;
        }

        if (inlineBaseline is not null
            && inlineTarget is not null
            && string.Equals(inlineBaseline.Title.Trim(), inlineTarget.Title.Trim(), StringComparison.Ordinal))
        {
            return RuleBatchEndpoints.NormalizePackKey(studyId, inlineBaseline.Title);
        }

        var authority = inlineBaseline?.Authority ?? inlineTarget?.Authority ?? RuleAuthority.Study;
        return $"study-{studyId.Value:N}-{authority.ToString().ToLowerInvariant()}";
    }
}
