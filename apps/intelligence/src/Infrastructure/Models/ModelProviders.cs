using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;

using Microsoft.Extensions.AI;

namespace Cadence.Modules.Intelligence.Infrastructure.Models;

/// <summary>A provider over one <see cref="IChatClient"/>, with tier names mapped to model ids and configured prices.</summary>
internal sealed class ChatClientModelProvider(
    string name,
    IChatClient client,
    IReadOnlyDictionary<string, string> tiers,
    IReadOnlyDictionary<string, ModelPrice> prices) : IModelProvider
{
    public string Name { get; } = name;

    public IChatClient Client { get; } = client;

    public string? ResolveModel(string modelOrTier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelOrTier);
        return modelOrTier.StartsWith(TaskConfiguration.TierPrefix, StringComparison.Ordinal)
            ? tiers.GetValueOrDefault(modelOrTier[TaskConfiguration.TierPrefix.Length..])
            : modelOrTier;
    }

    public decimal? Cost(string model, long inputTokens, long outputTokens) =>
        prices.TryGetValue(model, out var price) ? price.Of(inputTokens, outputTokens) : null;
}

/// <summary>The providers configured in this deployment, by name.</summary>
internal sealed class ModelProviderRegistry : IModelProviderRegistry
{
    private readonly FrozenDictionary<string, IModelProvider> _providers;

    public ModelProviderRegistry(IEnumerable<IModelProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var list = providers.ToList();
        var duplicate = list.GroupBy(p => p.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Model provider '{duplicate.Key}' is registered more than once.");
        }

        _providers = list.ToFrozenDictionary(p => p.Name, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> Names => [.. _providers.Keys.Order(StringComparer.Ordinal)];

    public IModelProvider? Find(string name) => name is null ? null : _providers.GetValueOrDefault(name);
}

/// <summary>The run client without the inference layer: the provider's own client, with no route recorded.</summary>
internal sealed class ProviderRunChatClientFactory : IRunChatClientFactory
{
    public Task<CommandResult<RunChatClient>> CreateAsync(AgentRunCall agentCall, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentCall);
        return Task.FromResult(CommandResult<RunChatClient>.Success(new RunChatClient(agentCall.Provider.Client, null)));
    }
}
