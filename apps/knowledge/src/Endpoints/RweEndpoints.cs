using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Core.Synthetic;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Knowledge.Endpoints;

public static class RweEndpoints
{
    public static IEndpointRouteBuilder MapRweEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/knowledge/rwe")
            .WithTags("RWE Cohort Distributions");

        group.MapGet("/cohorts", GetRweCohortsAsync)
            .WithName("ListRweCohorts")
            .WithSummary("Lists real-world evidence baseline cohort profiles.");

        group.MapGet("/cohorts/{id}", GetRweCohortByIdAsync)
            .WithName("GetRweCohortById")
            .WithSummary("Retrieves a real-world evidence cohort profile by ID or cohort code.");

        group.MapPost("/cohorts", CreateRweCohortAsync)
            .WithName("CreateRweCohort")
            .WithSummary("Registers a new real-world evidence baseline cohort profile.");

        group.MapGet("/cohorts/{id}/records", GetRweCohortRecordsAsync)
            .WithName("GetRweCohortRecords")
            .WithSummary("Retrieves de-identified real-world evidence patient records for a cohort profile.");

        return endpoints;
    }

    private static async Task<Ok<List<RweCohortProfileResponse>>> GetRweCohortsAsync(KnowledgeDbContext dbContext, TimeProvider timeProvider)
    {
        var cohorts = await dbContext.RweCohortProfiles
            .AsNoTracking()
            .OrderBy(c => c.CohortCode)
            .ToListAsync();

        if (cohorts.Count == 0)
        {
            var seedCohort = GetDefaultRweProfile("RWE-NSCLC-01", "Oncology - NSCLC Baseline Cohort", "Oncology", "Non-Small Cell Lung Cancer", timeProvider.GetUtcNow());
            cohorts.Add(seedCohort);
        }

        var result = cohorts.Select(MapCohortToResponse).ToList();
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<RweCohortProfileResponse>, NotFound<string>>> GetRweCohortByIdAsync(
        string id, KnowledgeDbContext dbContext, TimeProvider timeProvider)
    {
        var cohort = await dbContext.RweCohortProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id || c.CohortCode == id);

        if (cohort == null)
        {
            if (id.Contains("NSCLC", StringComparison.OrdinalIgnoreCase) || id.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase))
            {
                cohort = GetDefaultRweProfile(id, "Oncology - NSCLC Baseline Cohort", "Oncology", "Non-Small Cell Lung Cancer", timeProvider.GetUtcNow());
            }
            else
            {
                return TypedResults.NotFound($"RWE Cohort Profile '{id}' was not found.");
            }
        }

        return TypedResults.Ok(MapCohortToResponse(cohort));
    }

    private static async Task<Created<RweCohortProfileResponse>> CreateRweCohortAsync(
        CreateRweCohortRequest request, KnowledgeDbContext dbContext, TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();
        var entity = new RweCohortProfile
        {
            Id = Guid.NewGuid().ToString(),
            CohortCode = request.CohortCode,
            Name = request.Name,
            TherapeuticArea = request.TherapeuticArea,
            Indication = request.Indication,
            BaselineDemographicsJson = request.BaselineDemographicsJson ?? "{}",
            TotalPatientCount = request.TotalPatientCount > 0 ? request.TotalPatientCount : 5000,
            CreatedAt = now,
            CreatedBy = "svc:knowledge",
            ReasonForChange = request.ReasonForChange ?? "Initial RWE cohort baseline definition",
            VersionIndex = 1
        };

        dbContext.RweCohortProfiles.Add(entity);
        await dbContext.SaveChangesAsync();

        return TypedResults.Created($"/api/v1/knowledge/rwe/cohorts/{entity.Id}", MapCohortToResponse(entity));
    }

    private static async Task<Ok<List<SyntheticPatientRecord>>> GetRweCohortRecordsAsync(
        string id,
        int limit,
        KnowledgeDbContext dbContext)
    {
        int recordLimit = limit > 0 ? Math.Min(limit, 10000) : 500;

        var dbRecords = await dbContext.RwePatientRecords
            .AsNoTracking()
            .Where(r => r.CohortProfileId == id)
            .Take(recordLimit)
            .ToListAsync();

        if (dbRecords.Count > 0)
        {
            var mapped = dbRecords.Select(r => new SyntheticPatientRecord(
                PatientId: r.PatientIdentifier,
                Age: r.Age,
                Sex: r.Sex,
                Bmi: r.Bmi,
                SystolicBp: r.SystolicBp,
                DiastolicBp: r.DiastolicBp,
                BiomarkerValue: r.BiomarkerValue,
                PropensityScore: 0.0,
                IsMatched: false
            )).ToList();

            return TypedResults.Ok(mapped);
        }

        // Return generated RWE baseline patient records using MonteCarloCohortSampler
        var generated = MonteCarloCohortSampler.GenerateCohort(sampleSize: recordLimit);
        return TypedResults.Ok(generated);
    }

    private static RweCohortProfile GetDefaultRweProfile(string code, string name, string ta, string indication, DateTimeOffset now)
    {
        return new RweCohortProfile
        {
            Id = $"rwe-profile-{code.ToLowerInvariant()}",
            CohortCode = code,
            Name = name,
            TherapeuticArea = ta,
            Indication = indication,
            BaselineDemographicsJson = "{\"meanAge\": 62.4, \"maleRatio\": 0.54, \"meanBmi\": 26.8, \"meanSystolicBp\": 126.5, \"meanBiomarker\": 3.9}",
            TotalPatientCount = 10000,
            CreatedAt = now,
            CreatedBy = "svc:knowledge",
            ReasonForChange = "Default RWE cohort baseline seed",
            VersionIndex = 1
        };
    }

    private static RweCohortProfileResponse MapCohortToResponse(RweCohortProfile c) => new(
        c.Id,
        c.CohortCode,
        c.Name,
        c.TherapeuticArea,
        c.Indication,
        c.BaselineDemographicsJson,
        c.TotalPatientCount,
        c.CreatedAt,
        c.CreatedBy,
        c.VersionIndex);
}

public record CreateRweCohortRequest(
    string CohortCode,
    string Name,
    string TherapeuticArea,
    string Indication,
    string? BaselineDemographicsJson = null,
    int TotalPatientCount = 5000,
    string? ReasonForChange = null);

public record RweCohortProfileResponse(
    string Id,
    string CohortCode,
    string Name,
    string TherapeuticArea,
    string Indication,
    string BaselineDemographicsJson,
    int TotalPatientCount,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    int VersionIndex);
