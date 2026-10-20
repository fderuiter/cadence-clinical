using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Sandbox;

using Microsoft.Extensions.Logging;

namespace Cadence.Modules.Intelligence.Inference.Routing;

/// <summary>
/// The run client through the inference layer (ADR-2636 §4.2): the router decides whether and where the call may go,
/// the client carries the outbound guard and sandbox limits, and every call runs inside the run's
/// <see cref="AgentSandbox"/>. A refusal is <see cref="IntelligenceErrors.InferenceRefused"/>; nothing was sent.
/// </summary>
internal sealed partial class InferenceRunChatClientFactory(IInferenceRouter router, ILogger<InferenceRunChatClientFactory> logger)
    : IRunChatClientFactory
{
    public async Task<CommandResult<RunChatClient>> CreateAsync(AgentRunCall call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        var routing = await router.ResolveAsync(
            new InferenceRequest(call.Tenant, [new ModelRoute(call.Provider.Name, call.Model)]), cancellationToken).ConfigureAwait(false);
        if (!routing.IsRouted)
        {
            LogRefused(logger, call.RunId, call.Configuration.Key, call.Provider.Name, routing.Refusal);
            return IntelligenceErrors.InferenceRefused;
        }

        var route = routing.Routes[0];
        return CommandResult<RunChatClient>.Success(new RunChatClient(
            new SandboxScopeChatClient(routing.Client!, call.Tenant, call.RunId),
            new AgentRunInference(route.SupplierId, route.Locality.ToString(), route.Mode.ToString(), route.TenantResidency)));
    }

    // Only ids and codes: the request itself can carry participant data.
    [LoggerMessage(EventId = 6311, Level = LogLevel.Information, Message = "Agent run {RunId} of {TaskKey} refused the {Provider} route: {Reason}")]
    private static partial void LogRefused(ILogger logger, Guid runId, string taskKey, string provider, InferenceRefusalReason? reason);
}
