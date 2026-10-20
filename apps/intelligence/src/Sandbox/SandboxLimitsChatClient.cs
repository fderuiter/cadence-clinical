using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.AI;

namespace Cadence.Modules.Intelligence.Sandbox;

/// <summary>
/// Counts each model call and its tokens against the ambient sandbox's limits and ends the call at the run's
/// deadline (ADR-2636 §4.4). Outside a sandbox it passes calls through.
/// </summary>
internal sealed class SandboxLimitsChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (AgentSandbox.Current is not { } sandbox)
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }

        sandbox.BeginModelCall();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sandbox.Deadline);
        try
        {
            var response = await base.GetResponseAsync(messages, options, linked.Token).ConfigureAwait(false);
            sandbox.AddTokens(response.Usage?.TotalTokenCount ?? 0);
            return response;
        }
        catch (OperationCanceledException) when (sandbox.Deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new SandboxLimitExceededException(SandboxStopReason.WallClock, $"The run passed its {sandbox.Limits.WallClock} wall-clock limit.");
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sandbox = AgentSandbox.Current;
        sandbox?.BeginModelCall();
        using var linked = sandbox is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sandbox.Deadline);
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, linked.Token).ConfigureAwait(false))
        {
            foreach (var content in update.Contents)
            {
                if (content is UsageContent usage)
                {
                    sandbox?.AddTokens(usage.Details.TotalTokenCount ?? 0);
                }
            }

            yield return update;
        }
    }
}
