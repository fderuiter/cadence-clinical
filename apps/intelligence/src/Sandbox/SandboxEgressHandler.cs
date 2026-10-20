using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Modules.Intelligence.Inference.Providers;

using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Cadence.Modules.Intelligence.Sandbox;

/// <summary>Which named HTTP clients a sandboxed run may use besides the model providers' own.</summary>
public sealed class SandboxEgressOptions
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Intelligence:Sandbox";

    /// <summary>
    /// Named clients a run may use, such as the worker role's client for the host's internal read API. Empty by
    /// default: in the composition host the read ports are in-process and need no network.
    /// </summary>
    public IList<string> AllowedClients { get; set; } = [];
}

/// <summary>
/// The outermost handler of every <see cref="IHttpClientFactory"/> client (ADR-2636 §4.4). Outside a sandbox it does
/// nothing. Inside one it refuses every request except from a model provider's client (whose own handler pins its
/// endpoint hosts) or a client listed in <see cref="SandboxEgressOptions.AllowedClients"/>; nothing is sent.
/// </summary>
/// <param name="clientName">The named client this handler belongs to.</param>
/// <param name="allowed">Whether a sandboxed run may use it.</param>
internal sealed class SandboxEgressHandler(string clientName, bool allowed) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!allowed && AgentSandbox.Current is { } sandbox)
        {
            sandbox.RecordEgressRefusal();
            throw new InferenceEgressRefusedException(
                $"An agent run may not reach '{request.RequestUri?.IdnHost}' through client '{clientName}'; only its model endpoint and read ports.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Puts <see cref="SandboxEgressHandler"/> first on every named client the factory builds.</summary>
internal sealed class SandboxEgressConfiguration(IOptions<SandboxEgressOptions> egress) : IConfigureNamedOptions<HttpClientFactoryOptions>
{
    public void Configure(string? name, HttpClientFactoryOptions options)
    {
        var clientName = name ?? Options.DefaultName;
        var allowed = clientName.StartsWith(InferenceHttpClients.Prefix, StringComparison.Ordinal)
            || egress.Value.AllowedClients.Contains(clientName, StringComparer.Ordinal);
        options.HttpMessageHandlerBuilderActions.Add(builder =>
            builder.AdditionalHandlers.Insert(0, new SandboxEgressHandler(clientName, allowed)));
    }

    public void Configure(HttpClientFactoryOptions options) => Configure(Options.DefaultName, options);
}
