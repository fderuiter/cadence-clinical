using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Synthetic;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.AiGateway.Endpoints;

public static class SyntheticControlEndpoints
{
    public static IEndpointRouteBuilder MapSyntheticControlEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai")
            .WithTags("AI Gateway Synthetic Control Arm & Protocol Eligibility Optimization");

        group.MapPost("/synthetic-controls/simulate", SimulateSyntheticControlArmAsync)
            .WithName("SimulateSyntheticControlArm")
            .WithSummary("Executes propensity score matching and Monte Carlo sampling to generate a synthetic control arm cohort.")
            .Produces<PropensityMatchingResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/protocol-optimization/eligibility", SimulateEligibilityOptimizationAsync)
            .WithName("SimulateEligibilityOptimization")
            .WithSummary("Evaluates USDM eligibility criteria feasibility against RWE baseline distributions and reports attrition waterfalls.")
            .Produces<AttritionWaterfallResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static Results<Ok<PropensityMatchingResult>, ProblemHttpResult> SimulateSyntheticControlArmAsync(
        PropensityMatchingRequest request)
    {
        if (request == null)
        {
            return TypedResults.Problem(
                title: "Invalid synthetic control simulation request payload.",
                detail: "Request payload cannot be null.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Sentinel validation over eligibility logic expressions
        var validation = ProtocolQualitySentinel.ValidateCriteria(request.EligibilityCriteria ?? GetDefaultCriteria());
        if (!validation.IsValid)
        {
            return TypedResults.Problem(
                title: "Eligibility logic validation failed.",
                detail: string.Join("; ", validation.Errors),
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?>
                {
                    ["sentinelErrors"] = validation.Errors,
                    ["sentinelWarnings"] = validation.Warnings
                });
        }

        // Generate candidate RWE pool if needed
        int poolSize = Math.Max(200, Math.Min(10000, request.TargetSampleSize * 3));
        var rweCandidates = MonteCarloCohortSampler.GenerateCohort(sampleSize: poolSize);

        // Execute Propensity Score Matching
        var result = PropensityScoreMatchingEngine.MatchSyntheticArm(request, rweCandidates);

        return TypedResults.Ok(result);
    }

    private static Results<Ok<AttritionWaterfallResult>, ProblemHttpResult> SimulateEligibilityOptimizationAsync(
        EligibilityOptimizationRequest request)
    {
        if (request == null)
        {
            return TypedResults.Problem(
                title: "Invalid eligibility optimization request payload.",
                detail: "Request payload cannot be null.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var criteriaList = request.Criteria ?? GetDefaultCriteria();
        var validation = ProtocolQualitySentinel.ValidateCriteria(criteriaList);
        if (!validation.IsValid)
        {
            return TypedResults.Problem(
                title: "Eligibility criteria validation failed.",
                detail: string.Join("; ", validation.Errors),
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?>
                {
                    ["sentinelErrors"] = validation.Errors
                });
        }

        int cohortSize = request.InitialCohortSize > 0 ? Math.Min(request.InitialCohortSize, 50000) : 5000;
        var candidateCohort = MonteCarloCohortSampler.GenerateCohort(sampleSize: cohortSize);

        var result = EligibilityWaterfallEngine.EvaluateWaterfall(
            protocolId: request.ProtocolId ?? "PROT-UNSPECIFIED",
            criteria: criteriaList,
            candidates: candidateCohort);

        return TypedResults.Ok(result);
    }

    private static List<EligibilityCriterion> GetDefaultCriteria() => new()
    {
        new EligibilityCriterion("CRIT-01", "Adults Aged 18 to 75", "INCLUSION", "Age", "BETWEEN", "18", HighValue: "75"),
        new EligibilityCriterion("CRIT-02", "Baseline Systolic BP Control", "INCLUSION", "SystolicBp", "<=", "160"),
        new EligibilityCriterion("CRIT-03", "Exclude Severe Obesity (BMI >= 40)", "EXCLUSION", "Bmi", ">=", "40"),
        new EligibilityCriterion("CRIT-04", "Positive Target Biomarker Threshold", "INCLUSION", "BiomarkerValue", ">=", "1.5")
    };
}

public record EligibilityOptimizationRequest(
    string ProtocolId,
    List<EligibilityCriterion>? Criteria = null,
    int InitialCohortSize = 5000,
    string? RweCohortProfileId = null);
