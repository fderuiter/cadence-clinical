using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.AiGateway.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.AiGateway.Endpoints;

/// <summary>
/// Minimal API endpoints for AI Gateway Medical Coding Vector Suggestion Engine.
/// @req:PRD-SYS-001
/// </summary>
public static class MedicalCodingEndpoints
{
    public static RouteGroupBuilder MapMedicalCodingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/ai/medical-coding")
            .WithTags("AI Medical Coding");

        group.MapGet("/suggest", GetMedicalCodingSuggestionsAsync)
            .WithName("GetMedicalCodingSuggestions")
            .WithSummary("Retrieve top-K semantically similar and exact match MedDRA/WHODrug coding suggestions.");

        group.MapPost("/suggest", PostMedicalCodingSuggestionsAsync)
            .WithName("PostMedicalCodingSuggestions")
            .WithSummary("Retrieve top-K semantically similar and exact match MedDRA/WHODrug coding suggestions.");

        group.MapPost("/index", IndexSynonymRulesAsync)
            .WithName("IndexSynonymRules")
            .WithSummary("Index active synonym rules into vector store.");

        return group;
    }

    public static async Task<IResult> GetMedicalCodingSuggestionsAsync(
        IMedicalCodingSuggestService suggestService,
        string? query,
        string? q,
        string? dictionary,
        string? study_id,
        string? studyId,
        int? limit,
        CancellationToken cancellationToken)
    {
        string termQuery = !string.IsNullOrWhiteSpace(query) ? query : (q ?? string.Empty);
        string? effectiveStudyId = !string.IsNullOrWhiteSpace(study_id) ? study_id : studyId;

        var request = new SuggestMedicalCodingRequest(
            Query: termQuery,
            Dictionary: dictionary,
            StudyId: effectiveStudyId,
            Limit: limit ?? 5);

        var response = await suggestService.GetSuggestionsAsync(request, cancellationToken);
        return TypedResults.Ok(response);
    }

    public static async Task<IResult> PostMedicalCodingSuggestionsAsync(
        IMedicalCodingSuggestService suggestService,
        SuggestMedicalCodingRequest request,
        CancellationToken cancellationToken)
    {
        var response = await suggestService.GetSuggestionsAsync(request, cancellationToken);
        return TypedResults.Ok(response);
    }

    public static async Task<IResult> IndexSynonymRulesAsync(
        IVectorEmbeddingService embeddingService,
        IVectorCacheService cacheService,
        List<IndexSynonymRuleRequest> rules,
        CancellationToken cancellationToken)
    {
        if (rules == null || rules.Count == 0)
        {
            return TypedResults.Ok(new { indexedCount = 0 });
        }

        var candidates = new List<VectorCandidateEntry>();
        foreach (var r in rules)
        {
            if (string.IsNullOrWhiteSpace(r.VerbatimText) || string.IsNullOrWhiteSpace(r.TargetCode))
            {
                continue;
            }

            float[] vec = embeddingService.GenerateEmbedding(r.VerbatimText);
            candidates.Add(new VectorCandidateEntry(
                Id: r.Id != Guid.Empty ? r.Id : Guid.NewGuid(),
                Scope: r.Scope ?? "STUDY",
                StudyId: r.StudyId,
                VerbatimText: r.VerbatimText,
                DictionaryType: (r.DictionaryType ?? "MEDDRA").ToUpperInvariant(),
                DictionaryVersion: r.DictionaryVersion ?? "26.1",
                TargetCode: r.TargetCode,
                TargetTerm: r.TargetTerm ?? r.VerbatimText,
                TargetLevel: r.TargetLevel,
                Embedding: vec));
        }

        await cacheService.IndexCandidatesAsync(candidates, cancellationToken);
        return TypedResults.Ok(new { indexedCount = candidates.Count });
    }
}

public record IndexSynonymRuleRequest(
    Guid Id,
    string? Scope,
    string? StudyId,
    string VerbatimText,
    string? DictionaryType,
    string? DictionaryVersion,
    string TargetCode,
    string? TargetTerm,
    string? TargetLevel);
