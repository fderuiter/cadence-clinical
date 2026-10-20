using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Modules.Intelligence.Inference.Providers;

/// <summary>Names of the providers' HTTP clients: the model port's only route out of the process (ADR-2636 §4.4).</summary>
public static class InferenceHttpClients
{
    /// <summary>The prefix every provider client name starts with.</summary>
    public const string Prefix = "cadence-inference:";

    /// <summary>The named <see cref="HttpClient"/> of provider <paramref name="providerId"/>.</summary>
    public static string NameFor(string providerId) => Prefix + providerId;
}

/// <summary>
/// Refuses any request from a provider's client to a host outside that provider's fixed endpoint hosts, so a provider
/// cannot be pointed elsewhere at run time (ADR-2636 §4.4).
/// </summary>
/// <param name="providerId">The provider the client belongs to.</param>
/// <param name="hosts">The hosts it may reach.</param>
internal sealed class ProviderEndpointHandler(string providerId, IReadOnlySet<string> hosts) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.IdnHost;
        if (host is null || !hosts.Contains(host))
        {
            throw new InferenceEgressRefusedException(
                $"Provider '{providerId}' may reach only its configured endpoint; '{host}' is not one of them.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>A request to a host the model port or the agent sandbox does not allow. Nothing was sent.</summary>
public sealed class InferenceEgressRefusedException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public InferenceEgressRefusedException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public InferenceEgressRefusedException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and its cause.</summary>
    public InferenceEgressRefusedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
