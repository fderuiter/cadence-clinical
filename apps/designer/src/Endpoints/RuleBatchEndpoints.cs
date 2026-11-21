using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Rules.Amendment;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
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

        api.MapPost("/batch-validate", async Task<Ok<BatchValidationReport>> (
            string id,
            BatchValidateApiRequest? request,
            ParallelBatchRuleValidator validator,
            IClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var req = request ?? new BatchValidateApiRequest();
            var studyId = StudyId.TryParse(id, out var parsed) ? parsed : StudyId.New(clock.UtcNow);
            var tenant = ResolveVerifiedTenantId(httpContext);

            var records = req.Records ?? GetDefaultSampleRecords();
            var packs = req.RulePacks ?? [GetDefaultSampleRulePack()];

            var batchReq = new BatchValidationRequest(
                tenant,
                studyId,
                ValidationStage.Collection,
                DateOnly.FromDateTime(clock.UtcNow.DateTime),
                records,
                packs)
            {
                IsDryRun = req.IsDryRun ?? true,
                AsyncThreshold = req.AsyncThreshold ?? 10000,
            };

            var report = await validator.ValidateAsync(batchReq, cancellationToken).ConfigureAwait(false);
            JobScopes[report.JobId] = (tenant, id);
            return TypedResults.Ok(report);
        })
        .WithName("BatchValidateRules")
        .WithSummary("Executes study-wide parallel batch compilation and validation across pinned edit check rule packs.")
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
            else if (StudyId.TryParse(id, out var routeStudyId) && job.Study != routeStudyId)
            {
                return TypedResults.NotFound();
            }

            return TypedResults.Ok(job);
        })
        .WithName("GetBatchValidationJob")
        .WithSummary("Retrieves the aggregated report for an asynchronous batch validation job.")
        .RequirePermission(DesignerPermissions.CompileProtocol);

        api.MapPost("/amendment-regression", async Task<Ok<AmendmentRegressionReport>> (
            string id,
            AmendmentRegressionApiRequest? request,
            AmendmentRegressionEngine regressionEngine,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            var req = request ?? new AmendmentRegressionApiRequest();
            var baselineVersion = req.BaselineVersion ?? "1";
            var targetVersion = req.TargetVersion ?? "2";

            var baselinePack = req.BaselinePack ?? GetBaselineSampleRulePack();
            var targetPack = req.TargetPack ?? GetTargetSampleRulePack();
            var records = req.Records ?? GetDefaultSampleRecords();

            var baselineLoaded = new LoadedRulePack(
                LibraryItemId.New(clock.UtcNow),
                "pack_baseline",
                int.TryParse(baselineVersion, out var bv) ? bv : 1,
                "hash_v1",
                GovernanceStatus.Approved,
                baselinePack);

            var targetLoaded = new LoadedRulePack(
                LibraryItemId.New(clock.UtcNow),
                "pack_target",
                int.TryParse(targetVersion, out var tv) ? tv : 2,
                "hash_v2",
                GovernanceStatus.Approved,
                targetPack);

            var report = await regressionEngine.CompareAsync(
                baselineLoaded,
                targetLoaded,
                ValidationStage.Collection,
                DateOnly.FromDateTime(clock.UtcNow.DateTime),
                records,
                cancellationToken).ConfigureAwait(false);

            return TypedResults.Ok(report);
        })
        .WithName("AmendmentRegression")
        .WithSummary("Compares baseline and target rule pack versions during protocol amendments to highlight rule diffs and broken dependencies.")
        .RequirePermission(DesignerPermissions.CompileProtocol);
    }

    private static IReadOnlyList<RuleRecord> GetDefaultSampleRecords() =>
    [
        new("VS", "rec_001", new Dictionary<string, RuleValue>
        {
            ["pulse"] = RuleValue.FromNumber(110m),
            ["vssbp"] = RuleValue.FromNumber(140m),
            ["vsdpb"] = RuleValue.FromNumber(90m),
        }),
        new("VS", "rec_002", new Dictionary<string, RuleValue>
        {
            ["pulse"] = RuleValue.FromNumber(80m),
            ["vssbp"] = RuleValue.FromNumber(120m),
            ["vsdpb"] = RuleValue.FromNumber(80m),
        }),
    ];

    private static RulePack GetDefaultSampleRulePack() => new(
        RuleAuthority.Study,
        "Study Edit Checks Rule Pack",
        [
            new Rule(
                "RULE_001",
                1,
                RuleAuthority.Study,
                new SourceCitation("Protocol Edit Check Spec", "Section 4.1", null),
                new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                ValidationSeverity.Error,
                new ComparisonExpression(
                    ComparisonOperator.Greater,
                    new VariableExpression("pulse"),
                    new LiteralExpression(RuleValue.FromNumber(100m))),
                "Pulse exceeds upper threshold.",
                "Verify subject pulse value.",
                EffectivePeriod.Always),
        ]);

    private static RulePack GetBaselineSampleRulePack() => new(
        RuleAuthority.Study,
        "Baseline Edit Checks v1",
        [
            new Rule(
                "RULE_001",
                1,
                RuleAuthority.Study,
                new SourceCitation("Protocol Edit Check Spec", "Section 4.1", null),
                new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                ValidationSeverity.Error,
                new ComparisonExpression(
                    ComparisonOperator.Greater,
                    new VariableExpression("pulse"),
                    new LiteralExpression(RuleValue.FromNumber(100m))),
                "Pulse exceeds upper threshold.",
                "Verify pulse.",
                EffectivePeriod.Always),
            new Rule(
                "RULE_002",
                1,
                RuleAuthority.Study,
                new SourceCitation("Protocol Edit Check Spec", "Section 4.2", null),
                new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                ValidationSeverity.Warning,
                new ComparisonExpression(
                    ComparisonOperator.Greater,
                    new VariableExpression("vssbp"),
                    new LiteralExpression(RuleValue.FromNumber(160m))),
                "Systolic BP exceeds warning limit.",
                "Re-measure BP.",
                EffectivePeriod.Always),
        ]);

    private static RulePack GetTargetSampleRulePack() => new(
        RuleAuthority.Study,
        "Amended Edit Checks v2",
        [
            new Rule(
                "RULE_001",
                2,
                RuleAuthority.Study,
                new SourceCitation("Protocol Edit Check Spec", "Section 4.1", null),
                new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                ValidationSeverity.Error,
                new ComparisonExpression(
                    ComparisonOperator.Greater,
                    new VariableExpression("pulse"),
                    new LiteralExpression(RuleValue.FromNumber(105m))),
                "Pulse exceeds amended upper threshold.",
                "Verify pulse.",
                EffectivePeriod.Always),
            new Rule(
                "RULE_003",
                1,
                RuleAuthority.Study,
                new SourceCitation("Protocol Edit Check Spec", "Section 4.3", null),
                new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                ValidationSeverity.Error,
                new ComparisonExpression(
                    ComparisonOperator.Less,
                    new VariableExpression("vsdpb"),
                    new LiteralExpression(RuleValue.FromNumber(50m))),
                "Diastolic BP below safety threshold.",
                "Check subject vitals.",
                EffectivePeriod.Always),
        ]);

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

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("cadence-tenant:" + tenantCode.Trim()));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new TenantId(new Guid(guidBytes));
    }
}
