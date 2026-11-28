using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Search;

/// <summary>
/// A table the kernel searches (ADR-2484 §4.2). Sources are an explicit allow-list; a source that holds identifying
/// content names the capability that reveals it, and is searched only when the actor holds it (K-IDN-1).
/// </summary>
/// <param name="Name">Stable source name, for example <c>concepts</c>.</param>
/// <param name="Kind">The NodeRef kind its results refer to.</param>
/// <param name="RequiredCapability">The capability the actor needs before the source is searched, or null.</param>
public sealed record SearchSourceInfo(string Name, string Kind, Capability? RequiredCapability = null);

/// <summary>A validated search, ready for the store.</summary>
/// <param name="PrefixQuery">A PostgreSQL <c>tsquery</c> of the search words, each as a prefix, joined with AND.</param>
/// <param name="Text">The trimmed search text, compared with codes for exact matches.</param>
/// <param name="Sources">The sources to search.</param>
/// <param name="Limit">Maximum results.</param>
public sealed record SearchPlan(string PrefixQuery, string Text, IReadOnlyList<string> Sources, int Limit);

/// <summary>Runs a search plan against the kernel tables. Implemented by kernel persistence.</summary>
public interface IKernelSearchStore
{
    IReadOnlyList<SearchSourceInfo> Sources { get; }

    Task<IReadOnlyList<SearchHit>> QueryAsync(TenantId tenant, SearchPlan plan, CancellationToken cancellationToken);
}

/// <summary>
/// Turns what the user typed into a safe prefix query, picks the sources the actor may search, and asks the store
/// and any registered <see cref="IKernelSearchContributor"/> modules (ADR-2484). The search text never reaches SQL
/// as text: words are reduced to letters and digits and passed as a parameter.
/// </summary>
public sealed partial class KernelSearch(
    IKernelSearchStore store,
    IAuthorizationProbe probe,
    IEnumerable<IKernelSearchContributor>? contributors = null) : IKernelSearch
{
    /// <summary>At most this many words are matched; the rest are ignored.</summary>
    public const int MaxTerms = 8;

    /// <summary>Longest search text compared with codes.</summary>
    public const int MaxTextLength = 200;

    private readonly IReadOnlyList<IKernelSearchContributor> _contributors = contributors is null ? [] : [.. contributors];

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(TenantId tenant, SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (tenant.IsEmpty)
        {
            throw new ArgumentException("Search runs within a tenant.", nameof(tenant));
        }

        if (request.Limit is < 1 or > SearchRequest.MaxLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Limit, $"Limit must be between 1 and {SearchRequest.MaxLimit}.");
        }

        var text = (request.Text ?? string.Empty).Trim();
        if (text.Length > MaxTextLength)
        {
            text = text[..MaxTextLength];
        }

        var terms = Terms(text);
        if (terms.Count == 0)
        {
            return [];
        }

        var sources = new List<string>();
        foreach (var source in store.Sources)
        {
            if (request.Kinds is { Count: > 0 } kinds && !kinds.Contains(source.Kind))
            {
                continue;
            }

            if (source.RequiredCapability is { } capability
                && !(await probe.CanIAsync(tenant, capability, NodeRef.For(tenant), cancellationToken).ConfigureAwait(false)).Permitted)
            {
                continue;
            }

            sources.Add(source.Name);
        }

        var activeContributors = new List<(IKernelSearchContributor Contributor, List<string> Sources)>();
        foreach (var contributor in _contributors)
        {
            var contributorSources = new List<string>();
            foreach (var source in contributor.Sources)
            {
                if (request.Kinds is { Count: > 0 } kinds && !kinds.Contains(source.Kind))
                {
                    continue;
                }

                if (source.RequiredCapability is { } capability
                    && !(await probe.CanIAsync(tenant, capability, NodeRef.For(tenant), cancellationToken).ConfigureAwait(false)).Permitted)
                {
                    continue;
                }

                contributorSources.Add(source.Name);
            }

            if (contributorSources.Count > 0)
            {
                activeContributors.Add((contributor, contributorSources));
            }
        }

        if (sources.Count == 0 && activeContributors.Count == 0)
        {
            return [];
        }

        var prefixQuery = string.Join(" & ", terms.Select(t => t + ":*"));
        if (activeContributors.Count == 0)
        {
            var storeOnlyPlan = new SearchPlan(prefixQuery, text, sources, request.Limit);
            return await store.QueryAsync(tenant, storeOnlyPlan, cancellationToken).ConfigureAwait(false);
        }

        var merged = new List<SearchHit>();
        if (sources.Count > 0)
        {
            var plan = new SearchPlan(prefixQuery, text, sources, request.Limit);
            merged.AddRange(await store.QueryAsync(tenant, plan, cancellationToken).ConfigureAwait(false));
        }

        foreach (var (contributor, contributorSources) in activeContributors)
        {
            var contributorQuery = new SearchContributorQuery(prefixQuery, text, contributorSources, request.Limit);
            merged.AddRange(await contributor.QueryAsync(tenant, contributorQuery, cancellationToken).ConfigureAwait(false));
        }

        return
        [
            .. merged
                .GroupBy(h => (h.Ref.Kind, h.Ref.Id))
                .Select(g => g
                    .OrderByDescending(h => h.ExactCode)
                    .ThenByDescending(h => h.Rank)
                    .ThenByDescending(h => h.Ref.Version ?? 0L)
                    .First())
                .OrderByDescending(h => h.ExactCode)
                .ThenByDescending(h => h.Rank)
                .ThenBy(h => h.Title, StringComparer.Ordinal)
                .ThenBy(h => h.Ref.Id)
                .Take(request.Limit),
        ];
    }

    /// <summary>The distinct lower-case words of <paramref name="text"/> (letters and digits only), at most <see cref="MaxTerms"/>.</summary>
    public static IReadOnlyList<string> Terms(string text) =>
        [.. Word().Matches(text ?? string.Empty).Select(m => m.Value.ToLowerInvariant()).Distinct(StringComparer.Ordinal).Take(MaxTerms)];

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();
}

/// <summary>Registers kernel search.</summary>
public static class KernelSearchServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IKernelSearch"/>. The host also registers kernel persistence's store
    /// (<c>AddKernelSearchStore()</c>) and <see cref="IAuthorizationProbe"/> (<c>AddKernelAuthorization()</c>).
    /// </summary>
    public static IServiceCollection AddKernelSearch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IKernelSearch, KernelSearch>();
        return services;
    }
}
