using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Net.Http;

using Microsoft.Extensions.AI;

using OpenAI;
using OpenAI.Chat;

namespace Cadence.Modules.Intelligence.Inference.Providers;

/// <summary>
/// A model the operator runs inside the deployment behind an OpenAI-compatible chat endpoint (vLLM, llama.cpp server,
/// Ollama, TGI). It is the provider local-only tenants use (ADR-2636 §4.1). Its endpoint must pass
/// <see cref="InDeploymentEndpoint"/>, or the host does not start.
/// </summary>
internal sealed class OpenAiCompatibleProvider : IInferenceProvider
{
    private const string Setting = InferenceOptions.Section + ":Providers:OpenAiCompatible";

    // An endpoint without authentication still needs a non-empty credential for the SDK; it is never a secret.
    private const string NoKey = "not-used";

    private readonly IHttpClientFactory _httpClients;
    private readonly OpenAiCompatibleOptions _options;
    private readonly Uri _endpoint;

    public OpenAiCompatibleProvider(IHttpClientFactory httpClients, OpenAiCompatibleOptions options)
    {
        _httpClients = httpClients;
        _options = options;
        if (InDeploymentEndpoint.Problem(options.Endpoint) is { } problem)
        {
            throw new InvalidOperationException($"{Setting}:Endpoint must be inside the deployment: {problem}.");
        }

        _endpoint = options.Endpoint!;
        Residencies = ResidencyCodes.Parse(options.Residency, Setting + ":Residency");
        EndpointHosts = new[] { _endpoint.IdnHost }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    public const string ProviderId = "openai-compatible";

    public string Id => ProviderId;

    public InferenceLocality Locality => InferenceLocality.InDeployment;

    public IReadOnlySet<string> Residencies { get; }

    public string SupplierId => SupplierIds.OperatorHosted;

    public IReadOnlySet<string> EndpointHosts { get; }

    public IChatClient CreateClient(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        var served = _options.Models.TryGetValue(modelId, out var mapped) ? mapped : modelId;
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = _endpoint,
            NetworkTimeout = _options.Timeout,
            Transport = new HttpClientPipelineTransport(_httpClients.CreateClient(InferenceHttpClients.NameFor(Id))),
        };
        var key = new ApiKeyCredential(string.IsNullOrEmpty(_options.ApiKey) ? NoKey : _options.ApiKey);
        return new ChatClient(served, key, clientOptions).AsIChatClient();
    }
}
