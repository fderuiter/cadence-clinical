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
using Cadence.Rules.Packs;
using Cadence.Rules.Validation;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Designer.Endpoints;

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
        var api = routes.MapGroup("/api/v1/studies/{id}/rules")
            .WithTags("Cadence.Apps.Designer")
            .RequireStudyScope("id");

        api.MapPost("/batch-validate", BatchValidateRulesAsync)
            .WithName("BatchValidateRules")
            .WithSummary("Executes study-wide parallel batch compilation and validation across pinned edit check rule packs.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(DesignerPermissions.CompileProtocol);

        api.MapGet("/batch-validate/{jobId}", GetBatchValidationJob)
            .WithName("GetBatchValidationJob")
            .WithSummary("Retrieves the aggregated report for an asynchronous batch validation job.")
            .RequirePermission(DesignerPermissions.CompileProtocol);

        api.MapRuleAmendmentRegressionEndpoints();
    }

    public static async Task<Results<Ok<BatchValidationReport>, ProblemHttpResult>> BatchValidateRulesAsync(
        string id,
        BatchValidateApiRequest? request,
        ParallelBatchRuleValidator validator,
        RulePackLoader loader,
        IClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
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
    }

    public static Results<Ok<BatchValidationReport>, NotFound> GetBatchValidationJob(
        string id,
        string jobId,
        BatchJobStore jobStore,
        HttpContext httpContext)
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

    internal static (RulePack? Pack, string ContentHash, ProblemHttpResult? Error) ValidateAndHashInlinePack(RulePack pack)
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

    internal static string NormalizePackKey(StudyId studyId, string? title)
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

    internal static string AllocateUniquePackKey(string baseKey, HashSet<string> usedKeys)
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

    internal static bool TryParsePackVersion(string? versionText, int defaultVersion, out int version)
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

    internal static ProblemHttpResult UnprocessablePackProblem(string detail) =>
        TypedResults.Problem(
            title: "The rule pack request cannot be processed.",
            detail: detail,
            statusCode: StatusCodes.Status422UnprocessableEntity);

    internal static StudyId ResolveStudyId(TenantId tenant, string studyRouteId)
    {
        if (StudyId.TryParse(studyRouteId, out StudyId parsed) && !parsed.IsEmpty)
        {
            return parsed;
        }

        return new StudyId(DeriveDeterministicGuid($"cadence-study:{tenant}:{studyRouteId.Trim()}"));
    }

    internal static LibraryItemId DeriveLibraryItemId(TenantId tenant, StudyId studyId, string packKey, int version, string contentHash) =>
        new(DeriveDeterministicGuid($"cadence-rule-pack:{tenant}:{studyId}:{packKey}:v{version}:{contentHash}"));

    internal static TenantId ResolveVerifiedTenantId(HttpContext httpContext)
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
