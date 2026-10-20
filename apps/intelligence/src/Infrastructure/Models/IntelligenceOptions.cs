using System;
using System.Collections.Generic;

namespace Cadence.Modules.Intelligence.Infrastructure.Models;

/// <summary>The <c>Intelligence</c> configuration section (docs/ENVIRONMENT_VARIABLES.md).</summary>
public sealed class IntelligenceOptions
{
    /// <summary>The section name.</summary>
    public const string SectionName = "Intelligence";

    /// <summary>The Anthropic provider; configured when it has an API key.</summary>
    public AnthropicProviderOptions Anthropic { get; set; } = new();

    /// <summary>
    /// The deterministic fake provider that tests and local stacks use, so they never call a model. Refused outside the
    /// Development and Testing environments, where its answers could be mistaken for a model's.
    /// </summary>
    public bool FakeProvider { get; set; }

    /// <summary>How long one model call may take, in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 120;

    /// <summary>Prices by model id, for the ledger's cost column; a model without a price records no cost.</summary>
    public Dictionary<string, ModelPrice> Prices { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>The Anthropic provider's settings.</summary>
public sealed class AnthropicProviderOptions
{
    /// <summary>The API key. Never defaulted: without it the provider is not configured (ADR-2256 spirit).</summary>
    public string? ApiKey { get; set; }

    /// <summary>The model each tier maps to.</summary>
    public Dictionary<string, string> Tiers { get; set; } = new(StringComparer.Ordinal)
    {
        ["deep"] = "claude-opus-5-5",
        ["balanced"] = "claude-sonnet-5-5",
        ["fast"] = "claude-haiku-5-5",
    };
}

/// <summary>A model's price in US dollars per million tokens.</summary>
public sealed class ModelPrice
{
    /// <summary>Dollars per million input tokens.</summary>
    public decimal InputPerMillion { get; set; }

    /// <summary>Dollars per million output tokens.</summary>
    public decimal OutputPerMillion { get; set; }

    /// <summary>The price of the given usage.</summary>
    public decimal Of(long inputTokens, long outputTokens) =>
        ((inputTokens * InputPerMillion) + (outputTokens * OutputPerMillion)) / 1_000_000m;
}
