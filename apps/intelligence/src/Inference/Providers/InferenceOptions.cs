using System;
using System.Collections.Generic;

namespace Cadence.Modules.Intelligence.Inference.Providers;

/// <summary>How the whole deployment treats inference (ADR-2636 §4.2 step 2).</summary>
public enum InferenceDeploymentMode
{
    /// <summary>Each tenant's own inference policy decides.</summary>
    Standard,

    /// <summary>A sovereign deployment: every tenant is local-only, whatever its policy says.</summary>
    LocalOnly,
}

/// <summary>
/// The <c>Intelligence:Inference</c> configuration section (environment variables <c>Intelligence__Inference__…</c>).
/// A provider whose section is absent is not registered.
/// </summary>
public sealed class InferenceOptions
{
    /// <summary>The configuration section name.</summary>
    public const string Section = "Intelligence:Inference";

    /// <summary>The deployment mode; <see cref="InferenceDeploymentMode.Standard"/> unless set.</summary>
    public InferenceDeploymentMode Mode { get; set; } = InferenceDeploymentMode.Standard;

    /// <summary>The configured providers, one section each.</summary>
    public InferenceProvidersOptions Providers { get; set; } = new();
}

/// <summary>One optional section per provider this stream supplies (the Anthropic and fake providers are the Foundation's).</summary>
public sealed class InferenceProvidersOptions
{
    /// <summary>Claude Platform on AWS (<c>anthropic-aws</c>).</summary>
    public ClaudePlatformAwsOptions? AnthropicAws { get; set; }

    /// <summary>Claude on Amazon Bedrock (<c>bedrock</c>).</summary>
    public BedrockOptions? Bedrock { get; set; }

    /// <summary>Claude on Google Cloud Vertex AI (<c>vertex</c>).</summary>
    public VertexOptions? Vertex { get; set; }

    /// <summary>Claude on Microsoft Foundry (<c>foundry</c>).</summary>
    public FoundryOptions? Foundry { get; set; }

    /// <summary>A model the operator runs behind an OpenAI-compatible endpoint (<c>openai-compatible</c>).</summary>
    public OpenAiCompatibleOptions? OpenAiCompatible { get; set; }
}

/// <summary>Settings every provider section shares.</summary>
public abstract class InferenceProviderOptionsBase
{
    /// <summary>
    /// The residency codes the endpoint may serve, as the operator and the supplier record state them (for example
    /// <c>EU</c>). Empty: the endpoint serves only tenants without a residency.
    /// </summary>
    public IList<string> Residency { get; set; } = [];

    /// <summary>The longest a single model request may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>Claude Platform on AWS: Anthropic-operated, SigV4 through the standard AWS credential chain.</summary>
public sealed class ClaudePlatformAwsOptions : InferenceProviderOptionsBase
{
    /// <summary>The AWS region, such as <c>eu-central-1</c>. Required.</summary>
    public string Region { get; set; } = "";

    /// <summary>The Claude workspace id requests are routed to. Required.</summary>
    public string WorkspaceId { get; set; } = "";
}

/// <summary>Claude on Amazon Bedrock through the Messages-API (Mantle) endpoint and the standard AWS credential chain.</summary>
public sealed class BedrockOptions : InferenceProviderOptionsBase
{
    /// <summary>The AWS region, such as <c>eu-central-1</c>. Required.</summary>
    public string Region { get; set; } = "";
}

/// <summary>Claude on Vertex AI with Google application default credentials.</summary>
public sealed class VertexOptions : InferenceProviderOptionsBase
{
    /// <summary>The Google Cloud project id. Required.</summary>
    public string ProjectId { get; set; } = "";

    /// <summary>
    /// A region (<c>europe-west4</c>), a multi-region (<c>eu</c>, <c>us</c>) or <c>global</c>. Required. A
    /// <c>global</c> endpoint may declare no residency.
    /// </summary>
    public string Region { get; set; } = "";
}

/// <summary>Claude on Microsoft Foundry; Entra ID through the default Azure credential unless an API key is set.</summary>
public sealed class FoundryOptions : InferenceProviderOptionsBase
{
    /// <summary>The Foundry resource name, the subdomain of <c>services.ai.azure.com</c>. Required.</summary>
    public string Resource { get; set; } = "";

    /// <summary>An API key; a secret. Unset means Entra ID.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>An OpenAI-compatible chat endpoint the operator runs inside the deployment (vLLM, llama.cpp, Ollama, TGI).</summary>
public sealed class OpenAiCompatibleOptions : InferenceProviderOptionsBase
{
    /// <summary>
    /// The base address of the server's OpenAI-compatible API: scheme, a cluster-internal host, port and the
    /// <c>/v1</c> path. Required, and it must be in the deployment (<see cref="InDeploymentEndpoint"/>).
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>An API key if the endpoint wants one; a secret.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Maps a model id task configurations name to the id the endpoint serves (for example a local Llama build).
    /// Unmapped ids pass through unchanged.
    /// </summary>
    public IDictionary<string, string> Models { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
