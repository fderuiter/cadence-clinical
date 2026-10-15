using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Execution.Application.Coding;

/// <summary>
/// Result item for medical coding autosuggestions.
/// @req:PRD-SYS-001
/// </summary>
public sealed record CodingAutosuggestResult(
    string Name,
    string Code,
    string Pt,
    string Soc,
    string Dictionary,
    double Confidence,
    string MatchType);

/// <summary>
/// Service contract for Execution medical coding autosuggestion proxy and fallback.
/// @req:PRD-SYS-001
/// </summary>
public interface ICodingAutosuggestService
{
    /// <summary>
    /// Proxies coding autosuggestion queries to AI Gateway with fallback to local synonym rules.
    /// </summary>
    Task<IReadOnlyList<CodingAutosuggestResult>> GetAutosuggestionsAsync(
        string query,
        string? dictionary = null,
        string? studyId = null,
        int limit = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Index active synonym rules into AI Gateway vector store.
    /// </summary>
    Task IndexRulesAsync(
        IReadOnlyList<SynonymRuleResponse> rules,
        CancellationToken cancellationToken = default);
}
