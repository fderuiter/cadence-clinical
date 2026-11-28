using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

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
    [EnumDataType(typeof(InferenceDeploymentMode))]
    public InferenceDeploymentMode Mode { get; set; } = InferenceDeploymentMode.Standard;

    /// <summary>The configured providers, one section each.</summary>
    [Required]
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
    [Required]
    public IList<string> Residency { get; set; } = [];

    /// <summary>The longest a single model request may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>Claude Platform on AWS: Anthropic-operated, SigV4 through the standard AWS credential chain.</summary>
public sealed class ClaudePlatformAwsOptions : InferenceProviderOptionsBase
{
    /// <summary>The AWS region, such as <c>eu-central-1</c>. Required.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Region { get; set; } = "";

    /// <summary>The Claude workspace id requests are routed to. Required.</summary>
    [Required(AllowEmptyStrings = false)]
    public string WorkspaceId { get; set; } = "";
}

/// <summary>Claude on Amazon Bedrock through the Messages-API (Mantle) endpoint and the standard AWS credential chain.</summary>
public sealed class BedrockOptions : InferenceProviderOptionsBase
{
    /// <summary>The AWS region, such as <c>eu-central-1</c>. Required.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Region { get; set; } = "";
}

/// <summary>Claude on Vertex AI with Google application default credentials.</summary>
public sealed class VertexOptions : InferenceProviderOptionsBase
{
    /// <summary>The Google Cloud project id. Required.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ProjectId { get; set; } = "";

    /// <summary>
    /// A region (<c>europe-west4</c>), a multi-region (<c>eu</c>, <c>us</c>) or <c>global</c>. Required. A
    /// <c>global</c> endpoint may declare no residency.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Region { get; set; } = "";
}

/// <summary>Claude on Microsoft Foundry; Entra ID through the default Azure credential unless an API key is set.</summary>
public sealed class FoundryOptions : InferenceProviderOptionsBase
{
    /// <summary>The Foundry resource name, the subdomain of <c>services.ai.azure.com</c>. Required.</summary>
    [Required(AllowEmptyStrings = false)]
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
    [Required]
    public Uri? Endpoint { get; set; }

    /// <summary>An API key if the endpoint wants one; a secret.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Maps a model id task configurations name to the id the endpoint serves (for example a local Llama build).
    /// Unmapped ids pass through unchanged.
    /// </summary>
    [Required]
    public IDictionary<string, string> Models { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// Validates <see cref="InferenceOptions"/> and nested provider sections at host startup so malformed deployment modes,
/// non-positive timeouts, missing provider fields, invalid residency codes, or out-of-deployment endpoints fail with
/// <see cref="OptionsValidationException"/>.
/// </summary>
public sealed class InferenceOptionsValidator : IValidateOptions<InferenceOptions>
{
    private readonly IConfigurationSection? _section;

    /// <summary>Creates a validator without a backing configuration section.</summary>
    public InferenceOptionsValidator()
    {
    }

    /// <summary>Creates a validator backed by the <c>Intelligence:Inference</c> configuration section.</summary>
    /// <param name="section">The <c>Intelligence:Inference</c> configuration section.</param>
    public InferenceOptionsValidator(IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        _section = section;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, InferenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        string? rawMode = _section?["Mode"];
        if (rawMode is not null && !TryParseDeploymentMode(rawMode, out _))
        {
            failures.Add($"{InferenceOptions.Section}:Mode '{rawMode}' is not valid; expected {nameof(InferenceDeploymentMode.Standard)} or {nameof(InferenceDeploymentMode.LocalOnly)}.");
        }
        else if (!Enum.IsDefined(options.Mode))
        {
            failures.Add($"{InferenceOptions.Section}:Mode '{options.Mode}' is not valid; expected {nameof(InferenceDeploymentMode.Standard)} or {nameof(InferenceDeploymentMode.LocalOnly)}.");
        }

        if (options.Providers is null)
        {
            failures.Add($"{InferenceOptions.Section}:Providers must not be null.");
            return ValidateOptionsResult.Fail(failures);
        }

        var providersSection = _section?.GetSection("Providers");
        ValidateProvider(options.Providers.AnthropicAws, providersSection?.GetSection("AnthropicAws"), InferenceOptions.Section + ":Providers:AnthropicAws", failures);
        ValidateProvider(options.Providers.Bedrock, providersSection?.GetSection("Bedrock"), InferenceOptions.Section + ":Providers:Bedrock", failures);
        ValidateProvider(options.Providers.Vertex, providersSection?.GetSection("Vertex"), InferenceOptions.Section + ":Providers:Vertex", failures);
        ValidateProvider(options.Providers.Foundry, providersSection?.GetSection("Foundry"), InferenceOptions.Section + ":Providers:Foundry", failures);
        ValidateProvider(options.Providers.OpenAiCompatible, providersSection?.GetSection("OpenAiCompatible"), InferenceOptions.Section + ":Providers:OpenAiCompatible", failures);

        if (options.Providers.Vertex is { } vertex
            && string.Equals(vertex.Region?.Trim(), "global", StringComparison.OrdinalIgnoreCase)
            && vertex.Residency is { Count: > 0 })
        {
            failures.Add($"{InferenceOptions.Section}:Providers:Vertex: the global endpoint may serve any region, so it cannot declare a residency.");
        }

        if (options.Providers.OpenAiCompatible is { } openAi
            && InDeploymentEndpoint.Problem(openAi.Endpoint) is { } problem)
        {
            failures.Add($"{InferenceOptions.Section}:Providers:OpenAiCompatible:Endpoint must be inside the deployment: {problem}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    internal static void Bind(IConfigurationSection section, InferenceOptions target)
    {
        if (section["Mode"] is { } rawMode && TryParseDeploymentMode(rawMode, out var parsedMode))
        {
            target.Mode = parsedMode;
        }

        target.Providers = BindProviders(section.GetSection("Providers"));
    }

    internal static InferenceProvidersOptions BindProviders(IConfigurationSection providersSection) => new()
    {
        AnthropicAws = BindAnthropicAws(providersSection.GetSection("AnthropicAws")),
        Bedrock = BindBedrock(providersSection.GetSection("Bedrock")),
        Vertex = BindVertex(providersSection.GetSection("Vertex")),
        Foundry = BindFoundry(providersSection.GetSection("Foundry")),
        OpenAiCompatible = BindOpenAiCompatible(providersSection.GetSection("OpenAiCompatible")),
    };

    private static bool TryParseDeploymentMode(string raw, out InferenceDeploymentMode mode)
    {
        string trimmed = raw.Trim();
        if (trimmed.Length == 0 || int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            mode = default;
            return false;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out mode) && Enum.IsDefined(mode);
    }

    private static bool HasValues(IConfigurationSection section) =>
        section.Value is not null || section.GetChildren().Any();

    private static void BindBase(IConfigurationSection section, InferenceProviderOptionsBase target)
    {
        var residencyChildren = section.GetSection("Residency").GetChildren().ToList();
        if (residencyChildren.Count > 0)
        {
            target.Residency = residencyChildren.Select(c => c.Value ?? "").ToList();
        }

        if (section["Timeout"] is { } rawTimeout
            && TimeSpan.TryParse(rawTimeout, CultureInfo.InvariantCulture, out var parsedTimeout))
        {
            target.Timeout = parsedTimeout;
        }
    }

    private static ClaudePlatformAwsOptions? BindAnthropicAws(IConfigurationSection section)
    {
        if (!HasValues(section))
        {
            return null;
        }

        var options = new ClaudePlatformAwsOptions
        {
            Region = section["Region"] ?? "",
            WorkspaceId = section["WorkspaceId"] ?? "",
        };
        BindBase(section, options);
        return options;
    }

    private static BedrockOptions? BindBedrock(IConfigurationSection section)
    {
        if (!HasValues(section))
        {
            return null;
        }

        var options = new BedrockOptions
        {
            Region = section["Region"] ?? "",
        };
        BindBase(section, options);
        return options;
    }

    private static VertexOptions? BindVertex(IConfigurationSection section)
    {
        if (!HasValues(section))
        {
            return null;
        }

        var options = new VertexOptions
        {
            ProjectId = section["ProjectId"] ?? "",
            Region = section["Region"] ?? "",
        };
        BindBase(section, options);
        return options;
    }

    private static FoundryOptions? BindFoundry(IConfigurationSection section)
    {
        if (!HasValues(section))
        {
            return null;
        }

        var options = new FoundryOptions
        {
            Resource = section["Resource"] ?? "",
            ApiKey = section["ApiKey"],
        };
        BindBase(section, options);
        return options;
    }

    private static OpenAiCompatibleOptions? BindOpenAiCompatible(IConfigurationSection section)
    {
        if (!HasValues(section))
        {
            return null;
        }

        var options = new OpenAiCompatibleOptions
        {
            Endpoint = section["Endpoint"] is { Length: > 0 } rawEndpoint && Uri.TryCreate(rawEndpoint, UriKind.Absolute, out var uri)
                ? uri
                : null,
            ApiKey = section["ApiKey"],
        };
        foreach (var child in section.GetSection("Models").GetChildren())
        {
            if (child.Value is not null)
            {
                options.Models[child.Key] = child.Value;
            }
        }

        BindBase(section, options);
        return options;
    }

    private static void ValidateProvider(
        InferenceProviderOptionsBase? provider,
        IConfigurationSection? providerSection,
        string settingPath,
        List<string> failures)
    {
        if (provider is null)
        {
            return;
        }

        var annotationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(provider, new ValidationContext(provider), annotationResults, validateAllProperties: true))
        {
            foreach (var result in annotationResults)
            {
                failures.Add($"{settingPath}: {result.ErrorMessage}");
            }
        }

        string? rawTimeout = providerSection?["Timeout"];
        if (rawTimeout is not null
            && (string.IsNullOrWhiteSpace(rawTimeout)
                || !TimeSpan.TryParse(rawTimeout, CultureInfo.InvariantCulture, out var parsedTimeout)
                || parsedTimeout <= TimeSpan.Zero))
        {
            failures.Add($"{settingPath}:Timeout must be a positive TimeSpan (was '{rawTimeout}').");
        }
        else if (provider.Timeout <= TimeSpan.Zero)
        {
            failures.Add($"{settingPath}:Timeout must be greater than zero (was '{provider.Timeout}').");
        }

        try
        {
            _ = ResidencyCodes.Parse(provider.Residency, settingPath + ":Residency");
        }
        catch (InvalidOperationException ex)
        {
            failures.Add(ex.Message);
        }
    }
}
