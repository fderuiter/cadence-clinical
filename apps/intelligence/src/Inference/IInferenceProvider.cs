using System.Collections.Generic;

using Microsoft.Extensions.AI;

namespace Cadence.Modules.Intelligence.Inference;

/// <summary>Where a provider's model runs, relative to the deployment (ADR-2636 §4.1).</summary>
public enum InferenceLocality
{
    /// <summary>Inside the deployment: a loopback, private or cluster-internal endpoint, or in-process.</summary>
    InDeployment,

    /// <summary>A service outside the deployment, operated by a supplier.</summary>
    External,
}

/// <summary>
/// One way of reaching a model behind the <see cref="IChatClient"/> port (ADR-2636 §4.1). Runs never take a provider's
/// client directly: <see cref="Routing.IInferenceRouter"/> picks the provider the tenant's policy allows and wraps its
/// client in the outbound guard.
/// </summary>
public interface IInferenceProvider
{
    /// <summary>The provider id task configurations name in their routes, such as <c>bedrock</c>.</summary>
    string Id { get; }

    /// <summary>Whether the model runs inside the deployment or at a supplier.</summary>
    InferenceLocality Locality { get; }

    /// <summary>
    /// The residency codes (<c>Tenant.Residency</c>, ADR-2458) the configured endpoint may serve. Empty when it
    /// declares none, so it serves only tenants without a residency.
    /// </summary>
    IReadOnlySet<string> Residencies { get; }

    /// <summary>The supplier record (ADR-2636 §4.5) that covers this provider.</summary>
    string SupplierId { get; }

    /// <summary>The only hosts the provider's HTTP client may reach; empty for an in-process provider.</summary>
    IReadOnlySet<string> EndpointHosts { get; }

    /// <summary>A client for <paramref name="modelId"/>, named by its first-party id (for example <c>claude-opus-5-5</c>).</summary>
    IChatClient CreateClient(string modelId);
}
