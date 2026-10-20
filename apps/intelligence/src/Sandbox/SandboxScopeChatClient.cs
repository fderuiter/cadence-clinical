using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;

using Microsoft.Extensions.AI;

namespace Cadence.Modules.Intelligence.Sandbox;

/// <summary>
/// Runs each call inside the run's <see cref="AgentSandbox"/> when no sandbox is open yet. A caller that drives a
/// multi-step run (the tool layer, #5547) opens the sandbox itself, so its limits span every step.
/// </summary>
internal sealed class SandboxScopeChatClient(IChatClient innerClient, TenantId tenant, Guid runId) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var scope = AgentSandbox.Current is null ? AgentSandbox.Enter(tenant, runId) : null;
        return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var scope = AgentSandbox.Current is null ? AgentSandbox.Enter(tenant, runId) : null;
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }
}
