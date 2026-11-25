using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Rules.Amendment;
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

        api.MapPost("/amendment-regression", RunAmendmentRegressionAsync)
            .WithName("AmendmentRegression")
            .WithSummary("Compares baseline and target rule pack versions during protocol amendments to highlight rule diffs and broken dependencies.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(DesignerPermissions.CompileProtocol);
    }

    private static async Task<Results<Ok<BatchValidationReport>, ProblemHttpResult>> BatchValidateRulesAsync(
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

        var (loadedPacks, packError) = await RuleBatchPackResolver.ResolveBatchPacksAsync(
            tenant,
            studyId,
            req.RulePacks,
            loader,
            cancellationToken).ConfigureAwait(false);
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

    private static Results<Ok<BatchValidationReport>, NotFound> GetBatchValidationJob(
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

    private static async Task<Results<Ok<AmendmentRegressionReport>, ProblemHttpResult>> RunAmendmentRegressionAsync(
        string id,
        AmendmentRegressionApiRequest? request,
        AmendmentRegressionEngine regressionEngine,
        RulePackLoader loader,
        IClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var req = request ?? new AmendmentRegressionApiRequest();
        var tenant = ResolveVerifiedTenantId(httpContext);
        var studyId = ResolveStudyId(tenant, id);
        var records = req.Records ?? [];
        var registry = httpContext.RequestServices.GetService<IStandardsRegistry>();

        var (baselineLoaded, targetLoaded, packError) = await RuleBatchPackResolver.ResolveRegressionPacksAsync(
            tenant,
            studyId,
            req,
            loader,
            registry,
            cancellationToken).ConfigureAwait(false);
        if (packError is not null || baselineLoaded is null || targetLoaded is null)
        {
            return packError ?? RuleBatchPackResolver.UnprocessablePackProblem(RuleErrors.NoRulePacks.Message);
        }

        var datasetSchemas = await RuleBatchPackResolver.ResolveStudyDatasetSchemasAsync(
            tenant,
            studyId,
            registry,
            cancellationToken).ConfigureAwait(false);

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

    private static StudyId ResolveStudyId(TenantId tenant, string studyRouteId)
    {
        if (StudyId.TryParse(studyRouteId, out StudyId parsed) && !parsed.IsEmpty)
        {
            return parsed;
        }

        return new StudyId(RuleBatchPackResolver.DeriveDeterministicGuid($"cadence-study:{tenant}:{studyRouteId.Trim()}"));
    }

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

        return new TenantId(RuleBatchPackResolver.DeriveDeterministicGuid("cadence-tenant:" + tenantCode.Trim()));
    }
}
