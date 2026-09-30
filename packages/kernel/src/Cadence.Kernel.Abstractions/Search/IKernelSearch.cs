using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.References;

namespace Cadence.Kernel.Search;

/// <summary>
/// Finds kernel objects by code or title within one tenant (ADR-2484, Canvas D9): studies, sites, organizations,
/// concepts, terminology codes, standards and library items. Results are NodeRefs; the caller opens them with the
/// navigator. Reads run under the tenant's Row-Level Security, so another tenant's object is never returned.
/// </summary>
public interface IKernelSearch
{
    Task<IReadOnlyList<SearchHit>> SearchAsync(TenantId tenant, SearchRequest request, CancellationToken cancellationToken);
}

/// <summary>A search: free text, optionally limited to some NodeRef kinds.</summary>
/// <param name="Text">What the user typed. Each word matches as a prefix; a result whose code equals the whole text ranks first.</param>
/// <param name="Kinds">Only return these kinds (for example <c>concept</c>); null or empty for all.</param>
/// <param name="Limit">Maximum results, from 1 to <see cref="MaxLimit"/>.</param>
public sealed record SearchRequest(string Text, IReadOnlyCollection<string>? Kinds = null, int Limit = 20)
{
    /// <summary>The largest page a search returns.</summary>
    public const int MaxLimit = 50;
}

/// <summary>One search result.</summary>
/// <param name="Ref">The object found, pinned to the version read. A terminology code refers to its code-system version.</param>
/// <param name="Title">Its title, name or display text.</param>
/// <param name="Code">Its human code (study code, site code, concept code, terminology code), when it has one.</param>
/// <param name="Detail">Context that tells similar results apart (the code system and version, the country).</param>
/// <param name="ExactCode">True when the code equals the search text.</param>
/// <param name="Rank">Text relevance; higher is better.</param>
public sealed record SearchHit(NodeRef Ref, string Title, string? Code, string? Detail, bool ExactCode, double Rank);
