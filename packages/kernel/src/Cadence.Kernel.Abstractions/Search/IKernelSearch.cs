using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Search;

/// <summary>
/// Finds kernel and module-derived clinical objects by code, identifier, or title within one tenant (ADR-2484, Canvas D9):
/// studies, sites, study sites, enrollments, protocol versions, collection instances, observations, form queries,
/// validation findings, qualifications, artifacts, proposals, organizations, concepts, terminology codes, standards,
/// and library items. Results are NodeRefs; the caller opens them with the navigator. Reads run under the tenant's
/// Row-Level Security, so another tenant's object is never returned.
/// </summary>
public interface IKernelSearch
{
    /// <summary>Searches authorized sources in <paramref name="tenant"/> matching <paramref name="request"/>.</summary>
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
/// <param name="Code">Its human code (study code, site code, subject code, concept code, terminology code), when it has one.</param>
/// <param name="Detail">Context that tells similar results apart (study and site codes, code system and version, country).</param>
/// <param name="ExactCode">True when the code equals the search text.</param>
/// <param name="Rank">Text relevance; higher is better.</param>
public sealed record SearchHit(NodeRef Ref, string Title, string? Code, string? Detail, bool ExactCode, double Rank);

/// <summary>
/// Metadata describing a module-owned derived source searched via <see cref="IKernelSearchContributor"/> (ADR-2484 §4.2).
/// </summary>
/// <param name="Name">Stable source name, for example <c>execution_form_queries</c>.</param>
/// <param name="Kind">The <see cref="NodeKinds"/> value its results refer to.</param>
/// <param name="RequiredCapability">Optional capability the actor must hold before the source is searched (<c>K-IDN-1</c>).</param>
public sealed record SearchContributorSource(string Name, string Kind, Capability? RequiredCapability = null);

/// <summary>
/// A validated search query passed to <see cref="IKernelSearchContributor"/> implementations.
/// </summary>
/// <param name="PrefixQuery">A PostgreSQL <c>tsquery</c> of the search words, each as a prefix, joined with <c>&amp;</c>.</param>
/// <param name="Text">The trimmed search text, compared with codes for exact matches.</param>
/// <param name="Sources">The contributor source names authorized for this search.</param>
/// <param name="Limit">Maximum results.</param>
public sealed record SearchContributorQuery(string PrefixQuery, string Text, IReadOnlyList<string> Sources, int Limit);

/// <summary>
/// Contributes search hits from module-owned derived tables (such as <c>execution.form_queries</c>) under the caller's
/// tenant Row-Level Security (<c>K-ID-2</c>, <c>K-XS-2</c>, ADR-2484).
/// </summary>
public interface IKernelSearchContributor
{
    /// <summary>The module-owned sources this contributor searches.</summary>
    IReadOnlyList<SearchContributorSource> Sources { get; }

    /// <summary>Executes <paramref name="query"/> within <paramref name="tenant"/> under Row-Level Security.</summary>
    Task<IReadOnlyList<SearchHit>> QueryAsync(TenantId tenant, SearchContributorQuery query, CancellationToken cancellationToken);
}
