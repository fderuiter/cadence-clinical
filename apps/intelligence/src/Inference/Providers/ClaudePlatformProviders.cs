using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Net.Http;

using Anthropic;
using Anthropic.Aws;
using Anthropic.Bedrock;
using Anthropic.Foundry;
using Anthropic.Vertex;

using Azure.Identity;

using Microsoft.Extensions.AI;

namespace Cadence.Modules.Intelligence.Inference.Providers;

/// <summary>
/// Claude through a cloud platform's own Anthropic client (ADR-2636 §4.1). The SDK client is built per call around the
/// provider's named <see cref="HttpClient"/>, which carries the endpoint allow-list and the sandbox egress handler.
/// </summary>
internal abstract class ClaudePlatformProvider : IInferenceProvider
{
    private readonly IHttpClientFactory _httpClients;
    private readonly TimeSpan _timeout;

    protected ClaudePlatformProvider(IHttpClientFactory httpClients, InferenceProviderOptionsBase options, string setting)
    {
        _httpClients = httpClients;
        _timeout = options.Timeout;
        Residencies = ResidencyCodes.Parse(options.Residency, setting + ":Residency");
    }

    public abstract string Id { get; }

    public InferenceLocality Locality => InferenceLocality.External;

    public IReadOnlySet<string> Residencies { get; }

    public abstract string SupplierId { get; }

    public abstract IReadOnlySet<string> EndpointHosts { get; }

    public IChatClient CreateClient(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        var http = _httpClients.CreateClient(InferenceHttpClients.NameFor(Id));
        var client = CreatePlatformClient().WithOptions(o => o with { HttpClient = http, Timeout = _timeout });
        return client.AsIChatClient(PlatformModelId(modelId));
    }

    /// <summary>The platform's SDK client, configured for this deployment.</summary>
    protected abstract IAnthropicClient CreatePlatformClient();

    /// <summary>The id the platform serves <paramref name="modelId"/> under; the first-party id unless overridden.</summary>
    protected virtual string PlatformModelId(string modelId) => modelId;

    protected static FrozenSet<string> Hosts(params string[] hosts) => hosts.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    protected static string Required(string? value, string setting) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{setting} is required.") : value.Trim();
}

/// <summary>Claude Platform on AWS: Anthropic-operated, first-party model ids, SigV4 with the AWS credential chain.</summary>
internal sealed class ClaudePlatformAwsProvider : ClaudePlatformProvider
{
    private const string Setting = InferenceOptions.Section + ":Providers:AnthropicAws";
    private readonly string _region;
    private readonly string _workspaceId;

    public ClaudePlatformAwsProvider(IHttpClientFactory httpClients, ClaudePlatformAwsOptions options)
        : base(httpClients, options, Setting)
    {
        _region = Required(options.Region, Setting + ":Region");
        _workspaceId = Required(options.WorkspaceId, Setting + ":WorkspaceId");
        EndpointHosts = Hosts($"aws-external-anthropic.{_region}.api.aws");
    }

    public const string ProviderId = "anthropic-aws";

    public override string Id => ProviderId;

    public override string SupplierId => SupplierIds.AnthropicOnAws;

    public override IReadOnlySet<string> EndpointHosts { get; }

    protected override IAnthropicClient CreatePlatformClient() =>
        new AnthropicAwsClient(new AwsClientOptions { AwsRegion = _region, WorkspaceId = _workspaceId });
}

/// <summary>Claude on Amazon Bedrock through the Messages-API (Mantle) endpoint; Bedrock ids take the <c>anthropic.</c> prefix.</summary>
internal sealed class BedrockProvider : ClaudePlatformProvider
{
    private const string Setting = InferenceOptions.Section + ":Providers:Bedrock";
    private const string ModelPrefix = "anthropic.";
    private readonly string _region;

    public BedrockProvider(IHttpClientFactory httpClients, BedrockOptions options)
        : base(httpClients, options, Setting)
    {
        _region = Required(options.Region, Setting + ":Region");
        EndpointHosts = Hosts($"bedrock-mantle.{_region}.api.aws");
    }

    public const string ProviderId = "bedrock";

    public override string Id => ProviderId;

    public override string SupplierId => SupplierIds.AwsBedrock;

    public override IReadOnlySet<string> EndpointHosts { get; }

    protected override IAnthropicClient CreatePlatformClient() =>
        new AnthropicBedrockMantleClient(new MantleAwsClientOptions { AwsRegion = _region });

    protected override string PlatformModelId(string modelId) =>
        modelId.StartsWith(ModelPrefix, StringComparison.Ordinal) ? modelId : ModelPrefix + modelId;
}

/// <summary>Claude on Google Cloud Vertex AI with application default credentials; first-party model ids.</summary>
internal sealed class VertexProvider : ClaudePlatformProvider
{
    private const string Setting = InferenceOptions.Section + ":Providers:Vertex";
    private readonly string _project;
    private readonly string? _region;

    public VertexProvider(IHttpClientFactory httpClients, VertexOptions options)
        : base(httpClients, options, Setting)
    {
        _project = Required(options.ProjectId, Setting + ":ProjectId");
        var region = Required(options.Region, Setting + ":Region").ToLowerInvariant();
        _region = region == "global" ? null : region;
        if (_region is null && Residencies.Count > 0)
        {
            throw new InvalidOperationException($"{Setting}: the global endpoint may serve any region, so it cannot declare a residency.");
        }

        EndpointHosts = Hosts(_region switch
        {
            null => "aiplatform.googleapis.com",
            "us" => "aiplatform.us.rep.googleapis.com",
            "eu" => "aiplatform.eu.rep.googleapis.com",
            _ => $"{_region}-aiplatform.googleapis.com",
        });
    }

    public const string ProviderId = "vertex";

    public override string Id => ProviderId;

    public override string SupplierId => SupplierIds.GoogleVertex;

    public override IReadOnlySet<string> EndpointHosts { get; }

    protected override IAnthropicClient CreatePlatformClient() =>
        new AnthropicVertexClient(new AnthropicVertexCredentials(_region, _project));
}

/// <summary>Claude on Microsoft Foundry; Entra ID through the default Azure credential unless an API key is configured.</summary>
internal sealed class FoundryProvider : ClaudePlatformProvider
{
    private const string Setting = InferenceOptions.Section + ":Providers:Foundry";
    private readonly string _resource;
    private readonly string? _apiKey;

    public FoundryProvider(IHttpClientFactory httpClients, FoundryOptions options)
        : base(httpClients, options, Setting)
    {
        _resource = Required(options.Resource, Setting + ":Resource");
        _apiKey = string.IsNullOrWhiteSpace(options.ApiKey) ? null : options.ApiKey;
        EndpointHosts = Hosts($"{_resource}.services.ai.azure.com");
    }

    public const string ProviderId = "foundry";

    public override string Id => ProviderId;

    public override string SupplierId => SupplierIds.MicrosoftFoundry;

    public override IReadOnlySet<string> EndpointHosts { get; }

    protected override IAnthropicClient CreatePlatformClient() => new AnthropicFoundryClient(
        _apiKey is null
            ? new AnthropicFoundryIdentityTokenCredentials(new DefaultAzureCredential(), _resource, null)
            : new AnthropicFoundryApiKeyCredentials(_apiKey, _resource));
}
