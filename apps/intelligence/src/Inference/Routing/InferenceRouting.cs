using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Modules.Intelligence.Inference.Guard;

using Microsoft.Extensions.AI;

namespace Cadence.Modules.Intelligence.Inference.Routing;

/// <summary>How a tenant's runs may use inference (ADR-2636 §4.2 step 3).</summary>
public enum TenantInferenceMode
{
    /// <summary>The default: only providers that declare the tenant's residency, or any provider for an unrestricted tenant.</summary>
    ResidencyBound,

    /// <summary>Only providers inside the deployment.</summary>
    LocalOnly,

    /// <summary>Any configured provider whose supplier is allowed.</summary>
    Open,
}

/// <summary>A tenant's inference policy; <see cref="Default"/> when the tenant has none.</summary>
/// <param name="Mode">How runs may use inference.</param>
/// <param name="AllowedSuppliers">The only supplier records the tenant accepts, or null for any approved supplier.</param>
/// <param name="Scrub">What the outbound guard scrubs; direct identifiers unless the tenant widens it.</param>
public sealed record TenantInferencePolicy(
    TenantInferenceMode Mode,
    IReadOnlySet<string>? AllowedSuppliers = null,
    ScrubProfile Scrub = ScrubProfile.DirectIdentifiers)
{
    /// <summary>The policy of a tenant without a record: residency-bound, any approved supplier.</summary>
    public static TenantInferencePolicy Default { get; } = new(TenantInferenceMode.ResidencyBound);
}

/// <summary>
/// Reads tenants' inference policies. The record changes only through the audited
/// <c>intelligence.inference_policy.set</c> command (Approve verb, so never by an agent).
/// </summary>
public interface ITenantInferencePolicies
{
    /// <summary>The tenant's policy, or null when it has none.</summary>
    ValueTask<TenantInferencePolicy?> FindAsync(TenantId tenant, CancellationToken cancellationToken);
}

/// <summary>A provider and model a task configuration was qualified on (ADR-2631), in the configuration's order.</summary>
/// <param name="ProviderId">The provider id.</param>
/// <param name="ModelId">The first-party model id.</param>
public sealed record ModelRoute(string ProviderId, string ModelId);

/// <summary>What a run asks the router for.</summary>
/// <param name="Tenant">The tenant the run reads and writes in.</param>
/// <param name="QualifiedRoutes">The task configuration's qualified routes, preferred first.</param>
public sealed record InferenceRequest(TenantId Tenant, IReadOnlyList<ModelRoute> QualifiedRoutes);

/// <summary>Why the router or the outbound guard refused (ADR-2636 §4.2, §4.3).</summary>
public enum InferenceRefusalReason
{
    /// <summary>The tenant does not exist or is inactive.</summary>
    TenantUnknown,

    /// <summary>The task configuration names no route.</summary>
    NoQualifiedRoute,

    /// <summary>The route's provider is not configured in this deployment.</summary>
    ProviderNotConfigured,

    /// <summary>The tenant or the deployment is local-only and the provider is external.</summary>
    LocalOnly,

    /// <summary>The provider does not declare the tenant's residency.</summary>
    Residency,

    /// <summary>The tenant's policy does not accept the provider's supplier.</summary>
    SupplierNotAllowed,

    /// <summary>The provider's supplier record is missing or not approved, outside Development.</summary>
    SupplierNotApproved,

    /// <summary>The provider's supplier record is suspended.</summary>
    SupplierSuspended,

    /// <summary>A message carries content from the identity layer; nothing was sent.</summary>
    IdentityLayerContent,

    /// <summary>A message carries binary content the guard cannot inspect; nothing was sent.</summary>
    UninspectableContent,
}

/// <summary>One qualified route and why it was refused, if it was.</summary>
/// <param name="Route">The route.</param>
/// <param name="Refusal">The reason it was refused, or null when it is allowed.</param>
public sealed record RouteDecision(ModelRoute Route, InferenceRefusalReason? Refusal);

/// <summary>
/// Where a model call ran: recorded on the run's interaction-ledger entry (21 CFR 11.10(e)) and on each response as
/// <see cref="InferenceRoute.ResponsePropertyKey"/>.
/// </summary>
public sealed record InferenceRoute(
    string ProviderId,
    string ModelId,
    InferenceLocality Locality,
    string SupplierId,
    int? SupplierVersion,
    IReadOnlySet<string> ProviderResidencies,
    TenantInferenceMode Mode,
    string? TenantResidency)
{
    /// <summary>The <see cref="ChatResponse.AdditionalProperties"/> key carrying the route that served the response.</summary>
    public const string ResponsePropertyKey = "cadence.inference_route";
}

/// <summary>The router's answer: a guarded client over the allowed routes, or a refusal with every route's reason.</summary>
public sealed record InferenceRouting
{
    /// <summary>The client to use, or null when no route is allowed.</summary>
    public IChatClient? Client { get; init; }

    /// <summary>The allowed routes in failover order.</summary>
    public required IReadOnlyList<InferenceRoute> Routes { get; init; }

    /// <summary>Every route the router considered, with its refusal if any.</summary>
    public required IReadOnlyList<RouteDecision> Decisions { get; init; }

    /// <summary>The refusal when nothing is routed: the tenant's, or the first route's.</summary>
    public InferenceRefusalReason? Refusal { get; init; }

    /// <summary>True when a client is available.</summary>
    public bool IsRouted => Client is not null;
}

/// <summary>The one way a run gets a model client (ADR-2636 §4.2).</summary>
public interface IInferenceRouter
{
    /// <summary>
    /// Applies the deployment mode, the tenant's policy and residency, supplier status and the qualified routes, and
    /// returns a guarded client that fails over only among the allowed routes. A refusal is a normal outcome (K-AI-8).
    /// </summary>
    Task<InferenceRouting> ResolveAsync(InferenceRequest request, CancellationToken cancellationToken);
}

/// <summary>A model call the guard or the router refused before anything was sent.</summary>
public sealed class InferenceRefusedException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public InferenceRefusedException(InferenceRefusalReason reason, string message) : base(message) => Reason = reason;

    /// <summary>Creates the exception with no reason; prefer the reasoned constructor.</summary>
    public InferenceRefusedException() : this(InferenceRefusalReason.NoQualifiedRoute, "Inference was refused.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public InferenceRefusedException(string message) : this(InferenceRefusalReason.NoQualifiedRoute, message)
    {
    }

    /// <summary>Creates the exception with a message and its cause.</summary>
    public InferenceRefusedException(string message, Exception innerException) : base(message, innerException) =>
        Reason = InferenceRefusalReason.NoQualifiedRoute;

    /// <summary>Why the call was refused.</summary>
    public InferenceRefusalReason Reason { get; }
}

/// <summary>Every allowed route failed; the run ends with "AI unavailable" (K-AI-8).</summary>
public sealed class InferenceUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    public InferenceUnavailableException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public InferenceUnavailableException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the last route's failure.</summary>
    public InferenceUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
