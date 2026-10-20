using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Infrastructure.Models;

using Microsoft.Extensions.AI;

namespace Cadence.Modules.Intelligence.Inference;

/// <summary>
/// The tiers and prices the inference providers share with the module's Anthropic provider: the same Claude models are
/// served on every platform, so <c>Intelligence:Anthropic:Tiers</c> and <c>Intelligence:Prices</c> apply to all of them.
/// </summary>
/// <param name="Tiers">Tier name to first-party model id.</param>
/// <param name="Prices">First-party model id to price.</param>
internal sealed record InferenceModelSettings(IReadOnlyDictionary<string, string> Tiers, IReadOnlyDictionary<string, ModelPrice> Prices);

/// <summary>
/// An <see cref="IInferenceProvider"/> in the module's provider registry (ADR-2630 §4.2), so a task configuration can
/// name it. Model ids stay first-party (<c>claude-opus-5-5</c>) in the ledger and price list; the provider maps them to
/// the platform's id when it builds the client.
/// </summary>
internal sealed class InferenceModelProvider(IInferenceProvider inner, InferenceModelSettings settings) : IModelProvider
{
    public string Name => inner.Id;

    /// <summary>A client that builds the provider's per-call client for the requested model.</summary>
    public IChatClient Client { get; } = new PerCallChatClient(inner, settings);

    public string? ResolveModel(string modelOrTier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelOrTier);
        return modelOrTier.StartsWith(TaskConfiguration.TierPrefix, StringComparison.Ordinal)
            ? settings.Tiers.GetValueOrDefault(modelOrTier[TaskConfiguration.TierPrefix.Length..])
            : modelOrTier;
    }

    public decimal? Cost(string model, long inputTokens, long outputTokens) =>
        settings.Prices.TryGetValue(model, out var price) ? price.Of(inputTokens, outputTokens) : null;

    /// <summary>
    /// Builds the provider's client per call for <see cref="ChatOptions.ModelId"/> (the deep tier when none is given) and
    /// clears the id on the call, so the platform id the client was built for is the one sent.
    /// </summary>
    private sealed class PerCallChatClient(IInferenceProvider provider, InferenceModelSettings settings) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            using var client = provider.CreateClient(Model(options));
            return await client.GetResponseAsync(messages, WithoutModel(options), cancellationToken).ConfigureAwait(false);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var client = provider.CreateClient(Model(options));
            await foreach (var update in client.GetStreamingResponseAsync(messages, WithoutModel(options), cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
            // Each call's client is created and disposed with the call; nothing is held.
        }

        private string Model(ChatOptions? options) =>
            options?.ModelId
            ?? settings.Tiers.GetValueOrDefault("deep")
            ?? throw new InvalidOperationException($"No model was given for {provider.Id} and no deep tier is configured.");

        private static ChatOptions? WithoutModel(ChatOptions? options)
        {
            if (options is null)
            {
                return null;
            }

            var copy = options.Clone();
            copy.ModelId = null;
            return copy;
        }
    }
}
