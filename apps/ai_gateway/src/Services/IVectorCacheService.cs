using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.AiGateway.Services;

/// <summary>
/// Candidate vector entry stored in vector cache and search index.
/// @req:PRD-SYS-001
/// </summary>
public sealed record VectorCandidateEntry(
    Guid Id,
    string Scope,
    string? StudyId,
    string VerbatimText,
    string DictionaryType,
    string DictionaryVersion,
    string TargetCode,
    string TargetTerm,
    string? TargetLevel,
    float[] Embedding);

/// <summary>
/// Service contract for ephemeral vector embedding caching and similarity index retrieval.
/// @req:PRD-SYS-001
/// </summary>
public interface IVectorCacheService
{
    /// <summary>
    /// Index or update synonym candidates in the vector cache.
    /// </summary>
    Task IndexCandidatesAsync(IEnumerable<VectorCandidateEntry> candidates, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves candidates for similarity search matching scope and dictionary.
    /// </summary>
    Task<IReadOnlyList<VectorCandidateEntry>> GetCandidatesAsync(
        string? dictionaryType = null,
        string? studyId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if vector cache is reachable and online.
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
