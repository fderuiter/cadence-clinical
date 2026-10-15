using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Cadence.Core.Synthetic;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Designer.Endpoints;

public static class CohortFeasibilityEndpoints
{
    public static IEndpointRouteBuilder MapCohortFeasibilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/designer/cohort-feasibility")
            .WithTags("Designer Cohort Feasibility & Synthetic Control Proxy");

        group.MapPost("/synthetic-simulate", SimulateDesignerCohortFeasibilityAsync)
            .WithName("SimulateDesignerCohortFeasibility")
            .WithSummary("Proxies synthetic cohort feasibility simulation requests to AI Gateway and returns structured feasibility summaries.")
            .Produces<CohortFeasibilitySummary>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static Results<Ok<CohortFeasibilitySummary>, ProblemHttpResult> SimulateDesignerCohortFeasibilityAsync(
        DesignerCohortFeasibilityRequest request, TimeProvider timeProvider)
    {
        if (request == null)
        {
            return TypedResults.Problem(
                title: "Invalid cohort feasibility simulation request.",
                detail: "Request payload cannot be null.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var criteriaList = request.EligibilityCriteria ?? GetDefaultCriteria();

        // Validate via Protocol Quality Sentinel
        var sentinelValidation = ProtocolQualitySentinel.ValidateCriteria(criteriaList);
        if (!sentinelValidation.IsValid)
        {
            return TypedResults.Problem(
                title: "Protocol Quality Sentinel eligibility validation failed.",
                detail: string.Join("; ", sentinelValidation.Errors),
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?>
                {
                    ["sentinelErrors"] = sentinelValidation.Errors
                });
        }

        int sampleSize = request.TargetSampleSize > 0 ? request.TargetSampleSize : 500;
        int evaluatedCohortSize = Math.Max(2000, sampleSize * 4);

        var candidateCohort = MonteCarloCohortSampler.GenerateCohort(sampleSize: evaluatedCohortSize);

        // Calculate Attrition Waterfall
        var waterfall = EligibilityWaterfallEngine.EvaluateWaterfall(
            protocolId: request.StudyId ?? request.ProtocolId ?? "STUDY-USDM-001",
            criteria: criteriaList,
            candidates: candidateCohort);

        // Execute Propensity Score Matching for Synthetic Arm
        var propensityRequest = new PropensityMatchingRequest(
            StudyId: request.StudyId ?? request.ProtocolId ?? "STUDY-USDM-001",
            EligibilityCriteria: criteriaList,
            TargetSampleSize: sampleSize,
            Caliper: request.Caliper > 0 ? request.Caliper : 0.25,
            MatchingRatio: request.MatchingRatio ?? "1:1",
            RweCohortProfileId: request.RweCohortProfileId);

        var syntheticResult = PropensityScoreMatchingEngine.MatchSyntheticArm(propensityRequest, candidateCohort);

        var summary = new CohortFeasibilitySummary(
            SimulationId: syntheticResult.SimulationId,
            ProtocolId: request.StudyId ?? request.ProtocolId ?? "STUDY-USDM-001",
            EvaluatedCohortSize: waterfall.InitialPopulationCount,
            EligibleCohortSize: waterfall.FinalEligibleCount,
            OverallYieldPercentage: waterfall.OverallFeasibilityYieldPercentage,
            Waterfall: waterfall,
            SyntheticControlArm: syntheticResult,
            SentinelValidationErrors: sentinelValidation.Errors,
            GeneratedAt: timeProvider.GetUtcNow());

        return TypedResults.Ok(summary);
    }

    private static List<EligibilityCriterion> GetDefaultCriteria() => new()
    {
        new EligibilityCriterion("CRIT-01", "Adults Aged 18 to 75", "INCLUSION", "Age", "BETWEEN", "18", HighValue: "75"),
        new EligibilityCriterion("CRIT-02", "Baseline Systolic BP Control", "INCLUSION", "SystolicBp", "<=", "160"),
        new EligibilityCriterion("CRIT-03", "Exclude Severe Obesity (BMI >= 40)", "EXCLUSION", "Bmi", ">=", "40"),
        new EligibilityCriterion("CRIT-04", "Positive Target Biomarker Threshold", "INCLUSION", "BiomarkerValue", ">=", "1.5")
    };
}

public record DesignerCohortFeasibilityRequest(
    string? StudyId = null,
    string? ProtocolId = null,
    string? UsdmJson = null,
    List<EligibilityCriterion>? EligibilityCriteria = null,
    int TargetSampleSize = 500,
    double Caliper = 0.25,
    string? MatchingRatio = "1:1",
    string? RweCohortProfileId = null);
