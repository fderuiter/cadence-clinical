using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Modules.Intelligence.Inference.Providers;

using Microsoft.Extensions.AI;

namespace Cadence.Modules.Intelligence.Inference.Routing;

/// <summary>
/// Tries the allowed routes in the task configuration's order (ADR-2636 §4.2 step 7). Only routes the router already
/// allowed are here, so failover cannot leave the tenant's policy. Refusals and cancellation are not failures to fail
/// over from; a stream fails over only before its first update. Every response names the route that served it.
/// </summary>
internal sealed class FailoverChatClient(IReadOnlyList<FailoverRoute> routes) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var history = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        Exception? last = null;
        foreach (var (route, create, carriesModel) in routes)
        {
            using var client = create();
            try
            {
                var response = await client.GetResponseAsync(history, For(route, carriesModel, options), cancellationToken).ConfigureAwait(false);
                response.AdditionalProperties = PropertyBag.With(response.AdditionalProperties, InferenceRoute.ResponsePropertyKey, route);
                return response;
            }
            catch (Exception e) when (CanFailOver(e, cancellationToken))
            {
                last = e;
            }
        }

        throw Unavailable(last);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var history = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        Exception? last = null;
        foreach (var (route, create, carriesModel) in routes)
        {
            using var client = create();
            var updates = client.GetStreamingResponseAsync(history, For(route, carriesModel, options), cancellationToken).GetAsyncEnumerator(cancellationToken);
            try
            {
                bool first;
                try
                {
                    first = await updates.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception e) when (CanFailOver(e, cancellationToken))
                {
                    last = e;
                    continue;
                }

                if (!first)
                {
                    yield break;
                }

                do
                {
                    var update = updates.Current;
                    update.AdditionalProperties = PropertyBag.With(update.AdditionalProperties, InferenceRoute.ResponsePropertyKey, route);
                    yield return update;
                }
                while (await updates.MoveNextAsync().ConfigureAwait(false));

                yield break;
            }
            finally
            {
                await updates.DisposeAsync().ConfigureAwait(false);
            }
        }

        throw Unavailable(last);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        // Each route's client is created and disposed per call; nothing is held.
    }

    /// <summary>
    /// The options for one route: its own model id, or none when its client was built for the platform's id (a Bedrock
    /// id differs from the first-party one), so a caller's model id never sends one route's id to another provider.
    /// </summary>
    private static ChatOptions For(InferenceRoute route, bool clientCarriesModel, ChatOptions? options)
    {
        var routed = options?.Clone() ?? new ChatOptions();
        routed.ModelId = clientCarriesModel ? null : route.ModelId;
        return routed;
    }

    private static bool CanFailOver(Exception e, CancellationToken cancellationToken) =>
        e is not (InferenceRefusedException or InferenceEgressRefusedException)
        && !(e is OperationCanceledException && cancellationToken.IsCancellationRequested);

    private InferenceUnavailableException Unavailable(Exception? last) =>
        new($"No allowed inference route answered ({string.Join(", ", routes.Select(r => r.Route.ProviderId))}).", last!);
}

/// <summary>One allowed route, the factory for its per-call client, and whether that client fixes the model id.</summary>
internal sealed record FailoverRoute(InferenceRoute Route, Func<IChatClient> Create, bool ClientCarriesModel);
