using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Rules.Amendment;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Packs;
using Cadence.Rules.Validation;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Designer.Endpoints;

/// <summary>
/// API request parameters for batch rule validation.
/// </summary>
public record BatchValidateApiRequest(
    IReadOnlyList<RuleRecord>? Records = null,
    IReadOnlyList<RulePack>? RulePacks = null,
    bool? IsDryRun = null,
    int? AsyncThreshold = null);

/// <summary>
/// API request parameters for protocol amendment regression testing.
/// </summary>
public record AmendmentRegressionApiRequest(
    string? BaselineVersion = null,
    string? TargetVersion = null,
    RulePack? BaselinePack = null,
    RulePack? TargetPack = null,
    IReadOnlyList<RuleRecord>? Records = null);

/// <summary>
/// REST endpoints for parallel batch rule compilation, validation, and protocol amendment regression diff testing.
/// </summary>
public static class RuleBatchEndpoints
{
    private static readonly ConcurrentDictionary<string, (TenantId Tenant, string StudyRouteId)> JobScopes = new(StringComparer.Ordinal);

    /// <summary>
    /// Maps batch rule validation and amendment regression endpoints.
    /// </summary>
    public static void MapRuleBatchEndpoints(this IEndpointRouteBuilder routes)
    {
        // A caller reaches only the studies it is assigned to (ADR-2516).
        var api = routes.MapGroup("/api/v1/studies/{id}/rules").RequireStudyScope("id");

        api.MapPost("/batch-validate", async Task<Results<Ok<BatchValidationReport>, ProblemHttpResult>> (
            string id,
            BatchValidateApiRequest? request,
            ParallelBatchRuleValidator validator,
            RulePackLoader loader,
            IClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var req = request ?? new BatchValidateApiRequest();
            var tenant = ResolveVerifiedTenantId(httpContext);
            var studyId = ResolveStudyId(tenant, id);
            var records = req.Records ?? [];

            var (loadedPacks, packError) = await ResolveBatchPacksAsync(tenant, studyId, req.RulePacks, loader, cancellationToken).ConfigureAwait(false);
            if (packError is not null)
            {
                return packError;
            }

            var batchReq = new BatchValidationRequest(
                tenant,
                studyId,
                ValidationStage.Collection,
                DateOnly.FromDateTime(clock.UtcNow.DateTime),
                records,
                [.. loadedPacks.Select(p => p.Pack)])
            {
                IsDryRun = req.IsDryRun ?? true,
                AsyncThreshold = req.AsyncThreshold ?? 10000,
                LoadedRulePacks = loadedPacks,
            };

            var report = await validator.ValidateAsync(batchReq, cancellationToken).ConfigureAwait(false);
            JobScopes[report.JobId] = (tenant, id);
            return TypedResults.Ok(report);
        })
        .WithName("BatchValidateRules")
        .WithSummary("Executes study-wide parallel batch compilation and validation across pinned edit check rule packs.")
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequirePermission(DesignerPermissions.CompileProtocol);

        api.MapGet("/batch-validate/{jobId}", Results<Ok<BatchValidationReport>, NotFound> (
            string id,
            string jobId,
            BatchJobStore jobStore,
            HttpContext httpContext) =>
        {
            var tenant = ResolveVerifiedTenantId(httpContext);
            var job = jobStore.GetJob(jobId);
            if (job is null)
            {
                return TypedResults.NotFound();
            }

            if (JobScopes.TryGetValue(jobId, out var owner))
            {
                if (owner.Tenant != tenant || !string.Equals(owner.StudyRouteId, id, StringComparison.Ordinal))
                {
                    return TypedResults.NotFound();
                }
            }
            else if (job.Study != ResolveStudyId(tenant, id))
            {
                return TypedResults.NotFound();
            }

            return TypedResults.Ok(job);
        })
        .WithName("GetBatchValidationJob")
        .WithSummary("Retrieves the aggregated report for an asynchronous batch validation job.")
        .RequirePermission(DesignerPermissions.CompileProtocol);

        api.MapPost("/amendment-regression", async Task<Results<Ok<AmendmentRegressionReport>, ProblemHttpResult>> (
            string id,
            AmendmentRegressionApiRequest? request,
            AmendmentRegressionEngine regressionEngine,
            RulePackLoader loader,
            IClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var req = request ?? new AmendmentRegressionApiRequest();
            var tenant = ResolveVerifiedTenantId(httpContext);
            var studyId = ResolveStudyId(tenant, id);
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
                return packError ?? UnprocessablePackProblem(RuleErrors.NoRulePacks.Message);
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
        })
        .WithName("AmendmentRegression")
        .WithSummary("Compares baseline and target rule pack versions during protocol amendments to highlight rule diffs and broken dependencies.")
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequirePermission(DesignerPermissions.CompileProtocol);
    }

    private static async Task<(IReadOnlyList<LoadedRulePack> Packs, ProblemHttpResult? Error)> ResolveBatchPacksAsync(
        TenantId tenant,
        StudyId studyId,
        IReadOnlyList<RulePack>? inlinePacks,
        RulePackLoader loader,
        CancellationToken cancellationToken)
    {
        if (inlinePacks is null)
        {
            try
            {
                var pinned = await loader.LoadForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
                if (pinned.Count == 0)
                {
                    return ([], UnprocessablePackProblem(RuleErrors.NoRulePacks.Message));
                }

                return (pinned, null);
            }
            catch (InvalidOperationException ex)
            {
                return ([], UnprocessablePackProblem(ex.Message));
            }
        }

        if (inlinePacks.Count == 0)
        {
            return ([], UnprocessablePackProblem(RuleErrors.NoRulePacks.Message));
        }

        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        var loaded = new List<LoadedRulePack>(inlinePacks.Count);
        foreach (var pack in inlinePacks)
        {
            if (pack is null)
            {
                return ([], UnprocessablePackProblem(RuleErrors.PackInvalid.Message));
            }

            var (canonicalPack, contentHash, validationError) = ValidateAndHashInlinePack(pack);
            if (validationError is not null || canonicalPack is null)
            {
                return ([], validationError ?? UnprocessablePackProblem(RuleErrors.PackInvalid.Message));
            }

            var baseKey = NormalizePackKey(studyId, canonicalPack.Title);
            var key = AllocateUniquePackKey(baseKey, usedKeys);
            var itemId = DeriveLibraryItemId(tenant, studyId, key, version: 1, contentHash);
            loaded.Add(new LoadedRulePack(itemId, key, 1, contentHash, GovernanceStatus.Approved, canonicalPack));
        }

        return (loaded, null);
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
                return ([], UnprocessablePackProblem(ex.Message));
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
            return (null, null, UnprocessablePackProblem(RuleErrors.NoRulePacks.Message));
        }

        var sharedKey = DeriveSharedRegressionPackKey(studyId, resolvedBaseline, resolvedTarget, request.BaselinePack, request.TargetPack);

        if (resolvedBaseline is null)
        {
            if (!TryParsePackVersion(request.BaselineVersion, defaultVersion: 1, out var baselineVersion))
            {
                return (null, null, UnprocessablePackProblem($"Baseline version '{request.BaselineVersion}' must be a positive integer or library item UUID."));
            }

            var (canonicalBaseline, baselineHash, baselineError) = ValidateAndHashInlinePack(request.BaselinePack!);
            if (baselineError is not null || canonicalBaseline is null)
            {
                return (null, null, baselineError ?? UnprocessablePackProblem(RuleErrors.PackInvalid.Message));
            }

            var baselineId = DeriveLibraryItemId(tenant, studyId, sharedKey, baselineVersion, baselineHash);
            resolvedBaseline = new LoadedRulePack(baselineId, sharedKey, baselineVersion, baselineHash, GovernanceStatus.Approved, canonicalBaseline);
        }
        else if (!string.Equals(resolvedBaseline.Key, sharedKey, StringComparison.Ordinal))
        {
            resolvedBaseline = resolvedBaseline with { Key = sharedKey };
        }

        if (resolvedTarget is null)
        {
            if (!TryParsePackVersion(request.TargetVersion, defaultVersion: 2, out var targetVersion))
            {
                return (null, null, UnprocessablePackProblem($"Target version '{request.TargetVersion}' must be a positive integer or library item UUID."));
            }

            var (canonicalTarget, targetHash, targetError) = ValidateAndHashInlinePack(request.TargetPack!);
            if (targetError is not null || canonicalTarget is null)
            {
                return (null, null, targetError ?? UnprocessablePackProblem(RuleErrors.PackInvalid.Message));
            }

            var targetId = DeriveLibraryItemId(tenant, studyId, sharedKey, targetVersion, targetHash);
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
                : (null, UnprocessablePackProblem(result.Error!.Message));
        }

        if (pinnedPacks.Count == 0)
        {
            return (null, UnprocessablePackProblem(RuleErrors.NoRulePacks.Message));
        }

        if (string.IsNullOrWhiteSpace(versionSpec))
        {
            return (pinnedPacks[0], null);
        }

        if (!TryParsePackVersion(versionSpec, defaultVersion: 0, out var requestedVersion))
        {
            return (null, UnprocessablePackProblem($"Rule pack version '{versionSpec}' must be a positive integer or library item UUID."));
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
                    : (null, UnprocessablePackProblem(loaded.Error!.Message));
            }
        }

        return (null, UnprocessablePackProblem($"Pinned rule pack '{pinnedPacks[0].Key}' version {requestedVersion} was not found."));
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

    private static (RulePack? Pack, string ContentHash, ProblemHttpResult? Error) ValidateAndHashInlinePack(RulePack pack)
    {
        var json = RulePackJson.ToJson(pack);
        var (parsed, problems) = RulePackJson.Read(json);
        if (parsed is null || problems.Count > 0)
        {
            var detail = problems.Count > 0 ? string.Join(" ", problems) : RuleErrors.PackInvalid.Message;
            return (null, string.Empty, UnprocessablePackProblem(detail));
        }

        var contentHash = ContentHash.Compute(json.GetRawText());
        return (parsed, contentHash, null);
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
            return NormalizePackKey(studyId, inlineBaseline.Title);
        }

        var authority = inlineBaseline?.Authority ?? inlineTarget?.Authority ?? RuleAuthority.Study;
        return $"study-{studyId.Value:N}-{authority.ToString().ToLowerInvariant()}";
    }

    private static string NormalizePackKey(StudyId studyId, string? title)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            var sb = new StringBuilder(title.Length);
            var previousDash = false;
            foreach (var ch in title.Trim())
            {
                if (char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-')
                {
                    sb.Append(ch);
                    previousDash = false;
                }
                else if (!previousDash && sb.Length > 0)
                {
                    sb.Append('-');
                    previousDash = true;
                }
            }

            var normalized = sb.ToString().Trim('-', '.');
            if (normalized.Length > 0)
            {
                return normalized;
            }
        }

        return $"study-{studyId.Value:N}-rules";
    }

    private static string AllocateUniquePackKey(string baseKey, HashSet<string> usedKeys)
    {
        if (usedKeys.Add(baseKey))
        {
            return baseKey;
        }

        var suffix = 2;
        while (true)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{baseKey}-{suffix}");
            if (usedKeys.Add(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static bool TryParsePackVersion(string? versionText, int defaultVersion, out int version)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            version = defaultVersion;
            return version >= 1;
        }

        var trimmed = versionText.Trim();
        if (trimmed.Length > 1 && (trimmed[0] == 'v' || trimmed[0] == 'V'))
        {
            trimmed = trimmed[1..];
        }

        if (int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1)
        {
            version = parsed;
            return true;
        }

        version = 0;
        return false;
    }

    private static ProblemHttpResult UnprocessablePackProblem(string detail) =>
        TypedResults.Problem(
            title: "The rule pack request cannot be processed.",
            detail: detail,
            statusCode: StatusCodes.Status422UnprocessableEntity);

    private static StudyId ResolveStudyId(TenantId tenant, string studyRouteId)
    {
        if (StudyId.TryParse(studyRouteId, out StudyId parsed) && !parsed.IsEmpty)
        {
            return parsed;
        }

        return new StudyId(DeriveDeterministicGuid($"cadence-study:{tenant}:{studyRouteId.Trim()}"));
    }

    private static LibraryItemId DeriveLibraryItemId(TenantId tenant, StudyId studyId, string packKey, int version, string contentHash) =>
        new(DeriveDeterministicGuid($"cadence-rule-pack:{tenant}:{studyId}:{packKey}:v{version}:{contentHash}"));

    private static TenantId ResolveVerifiedTenantId(HttpContext httpContext)
    {
        string? tenantCode = CallerScope.TenantId(httpContext);
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            throw new InvalidOperationException("Verified tenant header is required.");
        }

        if (Guid.TryParse(tenantCode, out Guid parsed) && parsed != Guid.Empty)
        {
            return new TenantId(parsed);
        }

        return new TenantId(DeriveDeterministicGuid("cadence-tenant:" + tenantCode.Trim()));
    }

    private static Guid DeriveDeterministicGuid(string seed)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}
