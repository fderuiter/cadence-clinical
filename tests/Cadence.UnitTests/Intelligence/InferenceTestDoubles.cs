using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Tenancy;
using Cadence.Modules.Intelligence.Inference;
using Cadence.Modules.Intelligence.Inference.Routing;

using Microsoft.Extensions.AI;

namespace Cadence.UnitTests.Intelligence;

/// <summary>A provider whose client records every call and answers with a fixed text, or fails.</summary>
internal sealed class StubProvider(
    string id,
    InferenceLocality locality,
    string supplierId,
    params string[] residencies) : IInferenceProvider
{
    public string Id => id;

    public InferenceLocality Locality => locality;

    public IReadOnlySet<string> Residencies { get; } = residencies.ToFrozenSet(StringComparer.Ordinal);

    public string SupplierId => supplierId;

    public IReadOnlySet<string> EndpointHosts { get; } = new[] { $"{id}.example.test" }.ToFrozenSet();

    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    public Exception? Failure { get; set; }

    public IChatClient CreateClient(string modelId) => new StubChatClient(this, modelId);

    private sealed class StubChatClient(StubProvider provider, string modelId) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            provider.Calls.Add([.. messages]);
            if (provider.Failure is { } failure)
            {
                throw failure;
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"{provider.Id}:{modelId}"))
            {
                Usage = new UsageDetails { TotalTokenCount = 10 },
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release.
        }
    }
}

/// <summary>Residencies keyed by tenant; an absent tenant is unknown.</summary>
internal sealed class StubResidencies : ITenantResidencyReader
{
    public Dictionary<TenantId, TenantResidency> Tenants { get; } = [];

    public int Reads { get; private set; }

    public Task<TenantResidency?> FindAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult(Tenants.GetValueOrDefault(tenant));
    }
}

/// <summary>Policies keyed by tenant.</summary>
internal sealed class StubPolicies : ITenantInferencePolicies
{
    public Dictionary<TenantId, TenantInferencePolicy> Tenants { get; } = [];

    public ValueTask<TenantInferencePolicy?> FindAsync(TenantId tenant, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Tenants.GetValueOrDefault(tenant));
}

/// <summary>A supplier catalogue with chosen statuses.</summary>
internal sealed class StubSuppliers(params (string Id, SupplierStatus Status)[] records) : ISupplierCatalog
{
    private readonly Dictionary<string, SupplierRecord> _records = records.ToDictionary(
        r => r.Id,
        r => new SupplierRecord
        {
            Id = r.Id,
            Version = 3,
            Supplier = r.Id,
            OperatedBy = r.Id,
            Service = r.Id,
            Endpoint = r.Id,
            Status = r.Status,
        });

    public IReadOnlyCollection<SupplierRecord> All => _records.Values;

    public SupplierRecord? Find(string id) => _records.GetValueOrDefault(id);
}
