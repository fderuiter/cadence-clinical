using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Inference.Guard;
using Cadence.Modules.Intelligence.Inference.Providers;
using Cadence.Modules.Intelligence.Infrastructure.Models;
using Cadence.Modules.Intelligence.Sandbox;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Cadence.Modules.Intelligence.Inference.Routing;

/// <summary>
/// Applies ADR-2636 §4.2 steps 2 to 7 in order and fails closed. The kill switch and budget (step 1) are the
/// runtime's, before it asks. A tenant's residency is cached for <see cref="ResidencyCacheFor"/>, so a deactivated
/// tenant stops routing within that time.
/// </summary>
internal sealed class InferenceRouter(
    IEnumerable<IInferenceProvider> providers,
    IModelProviderRegistry registry,
    ITenantInferencePolicies policies,
    ITenantResidencyReader residencies,
    ISupplierCatalog suppliers,
    OutboundGuardFactory guard,
    IOptions<InferenceOptions> options,
    InferenceHost host,
    IClock clock) : IInferenceRouter
{
    /// <summary>How long a tenant's residency is reused before it is read again.</summary>
    public static readonly TimeSpan ResidencyCacheFor = TimeSpan.FromMinutes(5);

    /// <summary>The name the module registers its Claude API provider under.</summary>
    internal const string AnthropicProviderName = "anthropic";

    private const string InProcessSupplier = "in-process";

    private static readonly IReadOnlySet<string> NoResidency = FrozenSet<string>.Empty;

    private static readonly IReadOnlySet<string> AnthropicHosts = new[] { "api.anthropic.com" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, IInferenceProvider> _providers =
        providers.ToDictionary(p => p.Id, StringComparer.Ordinal);

    private readonly ConcurrentDictionary<TenantId, (TenantResidency Residency, DateTimeOffset ReadAt)> _residencyCache = new();

    public async Task<InferenceRouting> ResolveAsync(InferenceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var decisions = new List<RouteDecision>();
        if (request.QualifiedRoutes.Count == 0)
        {
            return Refused(decisions, InferenceRefusalReason.NoQualifiedRoute);
        }

        var residency = await ResidencyAsync(request.Tenant, cancellationToken).ConfigureAwait(false);
        if (residency is null)
        {
            decisions.AddRange(request.QualifiedRoutes.Select(r => new RouteDecision(r, InferenceRefusalReason.TenantUnknown)));
            return Refused(decisions, InferenceRefusalReason.TenantUnknown);
        }

        var policy = await policies.FindAsync(request.Tenant, cancellationToken).ConfigureAwait(false) ?? TenantInferencePolicy.Default;
        var mode = options.Value.Mode == InferenceDeploymentMode.LocalOnly ? TenantInferenceMode.LocalOnly : policy.Mode;

        var allowed = new List<(InferenceRoute Route, RouteTarget Target)>();
        foreach (var route in request.QualifiedRoutes)
        {
            var refusal = Check(route, mode, policy, residency, out var target, out var supplier);
            decisions.Add(new RouteDecision(route, refusal));
            if (refusal is null)
            {
                allowed.Add((new InferenceRoute(
                    target!.Id, route.ModelId, target.Locality, target.SupplierId, supplier?.Version,
                    target.Residencies, mode, residency.Region), target));
            }
        }

        if (allowed.Count == 0)
        {
            return Refused(decisions, decisions[0].Refusal);
        }

        var failover = new FailoverChatClient([.. allowed.Select(a => new FailoverRoute(a.Route, () => a.Target.CreateClient(a.Route.ModelId), a.Target.ClientCarriesModel))]);
        return new InferenceRouting
        {
            Client = guard.Wrap(new SandboxLimitsChatClient(failover), policy.Scrub),
            Routes = [.. allowed.Select(a => a.Route)],
            Decisions = decisions,
        };
    }

    private InferenceRefusalReason? Check(
        ModelRoute route,
        TenantInferenceMode mode,
        TenantInferencePolicy policy,
        TenantResidency residency,
        out RouteTarget? provider,
        out SupplierRecord? supplier)
    {
        supplier = null;
        provider = Target(route.ProviderId);
        if (provider is null)
        {
            return InferenceRefusalReason.ProviderNotConfigured;
        }

        if (mode == TenantInferenceMode.LocalOnly && provider.Locality != InferenceLocality.InDeployment)
        {
            return InferenceRefusalReason.LocalOnly;
        }

        // An in-process provider (the deterministic fake) sends nothing anywhere: no residency to keep, no supplier to assess.
        if (provider.Locality == InferenceLocality.InDeployment && provider.EndpointHosts.Count == 0)
        {
            return null;
        }

        if (mode != TenantInferenceMode.Open && residency.Region is { } region && !provider.Residencies.Contains(region))
        {
            return InferenceRefusalReason.Residency;
        }

        if (policy.AllowedSuppliers is { } allowedSuppliers && !allowedSuppliers.Contains(provider.SupplierId))
        {
            return InferenceRefusalReason.SupplierNotAllowed;
        }

        supplier = suppliers.Find(provider.SupplierId);
        return supplier?.Status switch
        {
            SupplierStatus.Suspended => InferenceRefusalReason.SupplierSuspended,
            SupplierStatus.Approved => null,
            _ when host.IsDevelopment => null,
            _ => InferenceRefusalReason.SupplierNotApproved,
        };
    }

    /// <summary>
    /// What the router knows about the provider a route names: an <see cref="IInferenceProvider"/> declares it; the
    /// module's built-in providers are described here; any other registered provider counts as an external supplier
    /// with no record, so it is refused outside Development until one is shipped.
    /// </summary>
    private RouteTarget? Target(string providerId)
    {
        if (_providers.TryGetValue(providerId, out var inference))
        {
            return new RouteTarget(
                inference.Id, inference.Locality, inference.Residencies, inference.SupplierId, inference.EndpointHosts, inference.CreateClient, true);
        }

        if (registry.Find(providerId) is not { } builtIn)
        {
            return null;
        }

        IChatClient Shared(string _) => new SharedChatClient(builtIn.Client);
        return providerId switch
        {
            AnthropicProviderName => new RouteTarget(
                providerId, InferenceLocality.External, NoResidency, SupplierIds.Anthropic, AnthropicHosts, Shared, false),
            FakeChatClient.ProviderName => new RouteTarget(
                providerId, InferenceLocality.InDeployment, NoResidency, InProcessSupplier, NoResidency, Shared, false),
            _ => new RouteTarget(
                providerId, InferenceLocality.External, NoResidency, providerId, new HashSet<string>(StringComparer.Ordinal) { providerId }, Shared, false),
        };
    }

    private async ValueTask<TenantResidency?> ResidencyAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (_residencyCache.TryGetValue(tenant, out var cached) && now - cached.ReadAt < ResidencyCacheFor)
        {
            return cached.Residency;
        }

        var found = await residencies.FindAsync(tenant, cancellationToken).ConfigureAwait(false);
        if (found is null)
        {
            _residencyCache.TryRemove(tenant, out _);
        }
        else
        {
            _residencyCache[tenant] = (found, now);
        }

        return found;
    }

    private static InferenceRouting Refused(List<RouteDecision> decisions, InferenceRefusalReason? reason) =>
        new() { Routes = [], Decisions = decisions, Refusal = reason };
}

/// <summary>The policy store until the module's persistence holds tenant policies: every tenant has the default.</summary>
/// <summary>A provider as the router sees it.</summary>
/// <param name="Id">The provider's name.</param>
/// <param name="Locality">Where it runs.</param>
/// <param name="Residencies">The residency codes it declares.</param>
/// <param name="SupplierId">Its supplier record.</param>
/// <param name="EndpointHosts">The hosts it reaches; none for an in-process provider.</param>
/// <param name="CreateClient">Builds the client for one call, for a first-party model id.</param>
/// <param name="ClientCarriesModel">True when <paramref name="CreateClient"/> builds a client for the platform's model id, so the call must not override it.</param>
internal sealed record RouteTarget(
    string Id,
    InferenceLocality Locality,
    IReadOnlySet<string> Residencies,
    string SupplierId,
    IReadOnlySet<string> EndpointHosts,
    Func<string, IChatClient> CreateClient,
    bool ClientCarriesModel);

/// <summary>A provider's long-lived client, handed out per call without being disposed with the call.</summary>
internal sealed class SharedChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    protected override void Dispose(bool disposing)
    {
        // The provider owns the inner client; a call's wrapper must not dispose it.
    }
}

/// <summary>What the router needs to know about the host: whether Draft suppliers may be used (Development only).</summary>
/// <param name="IsDevelopment">True in the Development environment.</param>
internal sealed record InferenceHost(bool IsDevelopment);

internal sealed class DefaultTenantInferencePolicies : ITenantInferencePolicies
{
    public ValueTask<TenantInferencePolicy?> FindAsync(TenantId tenant, CancellationToken cancellationToken) =>
        ValueTask.FromResult<TenantInferencePolicy?>(null);
}
