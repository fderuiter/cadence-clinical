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
/// (ADR-2484). The search text never reaches SQL as text: words are reduced to letters and digits and passed as a
/// parameter.
/// </summary>
public sealed partial class KernelSearch(IKernelSearchStore store, IAuthorizationProbe probe) : IKernelSearch
{
    /// <summary>At most this many words are matched; the rest are ignored.</summary>
    public const int MaxTerms = 8;

    /// <summary>Longest search text compared with codes.</summary>
    public const int MaxTextLength = 200;

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

        if (sources.Count == 0)
        {
            return [];
        }

        var plan = new SearchPlan(string.Join(" & ", terms.Select(t => t + ":*")), text, sources, request.Limit);
        return await store.QueryAsync(tenant, plan, cancellationToken).ConfigureAwait(false);
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
