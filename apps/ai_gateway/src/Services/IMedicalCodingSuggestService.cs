using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.AiGateway.Services;

/// <summary>
/// Suggestion result for medical coding.
/// @req:PRD-SYS-001
/// </summary>
public sealed record MedicalCodingSuggestionDto(
    string Name,
    string Code,
    string Pt,
    string Soc,
    string Dictionary,
    double Confidence,
    string MatchType);

/// <summary>
/// Request payload for medical coding suggestions.
/// @req:PRD-SYS-001
/// </summary>
public sealed record SuggestMedicalCodingRequest(
    string Query,
    string? Dictionary = null,
    string? StudyId = null,
    int Limit = 5);

/// <summary>
/// Response payload for medical coding suggestions.
/// @req:PRD-SYS-001
/// </summary>
public sealed record SuggestMedicalCodingResponse(
    string Query,
    IReadOnlyList<MedicalCodingSuggestionDto> Suggestions);

/// <summary>
/// Service contract for medical coding autosuggestions.
/// @req:PRD-SYS-001
/// </summary>
public interface IMedicalCodingSuggestService
{
    /// <summary>
    /// Computes top-K semantically similar and exact match MedDRA/WHODrug terms.
    /// </summary>
    Task<SuggestMedicalCodingResponse> GetSuggestionsAsync(
        SuggestMedicalCodingRequest request,
        CancellationToken cancellationToken = default);
}
