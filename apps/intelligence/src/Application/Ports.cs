using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Evaluation;

using Microsoft.Extensions.AI;

namespace Cadence.Modules.Intelligence.Application;

/// <summary>
/// The task configuration versions this deployment ships, by key and version (ADR-2631). A version's content never
/// changes once published; the runtime uses, per tenant, the newest version that tenant qualified.
/// </summary>
public interface ITaskConfigurationCatalog
{
    /// <summary>Every configuration version, ordered by key and then version.</summary>
    IReadOnlyList<TaskConfiguration> All { get; }

    /// <summary>The versions of <paramref name="taskKey"/>, newest first; empty when the task is unknown.</summary>
    IReadOnlyList<TaskConfiguration> Versions(string taskKey);

    /// <summary>One version of <paramref name="taskKey"/>, or null.</summary>
    TaskConfiguration? Find(string taskKey, int version);
}

/// <summary>
/// A model provider behind the provider-neutral port (ADR-2630 §4.2): an <see cref="IChatClient"/> plus what the
/// runtime needs to account for it. Providers are registered by name; inference and sovereignty (#5550) add more.
/// </summary>
public interface IModelProvider
{
    /// <summary>The provider's name, lower case, as task configurations name it.</summary>
    string Name { get; }

    /// <summary>The chat client every call to this provider goes through.</summary>
    IChatClient Client { get; }

    /// <summary>
    /// The model id for <paramref name="modelOrTier"/>: a tier (<c>tier:deep</c>) mapped to the provider's model, or the
    /// id itself. Null when the provider has no model for the tier.
    /// </summary>
    string? ResolveModel(string modelOrTier);

    /// <summary>What <paramref name="inputTokens"/> and <paramref name="outputTokens"/> cost on <paramref name="model"/>, or null when no price is configured.</summary>
    decimal? Cost(string model, long inputTokens, long outputTokens);
}

/// <summary>The configured model providers, by name.</summary>
public interface IModelProviderRegistry
{
    /// <summary>The configured providers' names.</summary>
    IReadOnlyCollection<string> Names { get; }

    /// <summary>The provider named <paramref name="name"/>, or null when it is not configured.</summary>
    IModelProvider? Find(string name);
}

/// <summary>One agent run's model call, before the client for it is built.</summary>
/// <param name="Tenant">The tenant the run is for.</param>
/// <param name="RunId">The ledger entry the run will write.</param>
/// <param name="Configuration">The task configuration.</param>
/// <param name="Provider">The provider the configuration names.</param>
/// <param name="Model">The model id after any tier was resolved.</param>
public sealed record AgentRunCall(TenantId Tenant, Guid RunId, TaskConfiguration Configuration, IModelProvider Provider, string Model);

/// <summary>The client a run calls the model through, and where it routes (null when nothing was recorded).</summary>
/// <param name="Client">The chat client for this run; the caller does not dispose it.</param>
/// <param name="Inference">The route for the ledger, without scrub counts, which come with the response.</param>
public sealed record RunChatClient(IChatClient Client, AgentRunInference? Inference);

/// <summary>
/// Builds the chat client one run calls the model through (ADR-2636 §4.2). The default is the provider's own client;
/// the inference layer replaces it with the routed one: tenant policy, residency and supplier checks, failover inside
/// the allowed routes, the outbound guard and the sandbox.
/// </summary>
public interface IRunChatClientFactory
{
    /// <summary>The client for <paramref name="call"/>, or the registered error saying why there is none.</summary>
    Task<CommandResult<RunChatClient>> CreateAsync(AgentRunCall call, CancellationToken cancellationToken);
}

/// <summary>Reads the interaction ledger and the agent controls, within one tenant (Row-Level Security).</summary>
public interface IAgentLedger
{
    /// <summary>The tenant's kill switches and budgets.</summary>
    Task<IReadOnlyList<AgentControl>> ControlsAsync(TenantId tenant, CancellationToken cancellationToken);

    /// <summary>Tokens used by runs started since <paramref name="since"/>, for one task or (null) for all.</summary>
    Task<long> TokensUsedAsync(TenantId tenant, string? taskKey, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>The latest runs <paramref name="requestedBy"/> asked for, newest first.</summary>
    Task<IReadOnlyList<AgentRun>> RunsAsync(TenantId tenant, string requestedBy, int limit, CancellationToken cancellationToken);

    /// <summary>One run, or null.</summary>
    Task<AgentRun?> FindRunAsync(TenantId tenant, Guid runId, CancellationToken cancellationToken);

    /// <summary>The tenant's task configuration qualifications, qualified and suspended.</summary>
    Task<IReadOnlyList<TaskQualification>> QualificationsAsync(TenantId tenant, CancellationToken cancellationToken);
}

/// <summary>Reads the stored evaluation runs (ADR-2634 §4.4), within one tenant (Row-Level Security).</summary>
public interface IEvaluationRuns
{
    /// <summary>The stored runs of one task, newest first, optionally of one version.</summary>
    Task<IReadOnlyList<EvaluationRun>> ForTaskAsync(TenantId tenant, string taskKey, int? version, int limit, CancellationToken cancellationToken);

    /// <summary>One stored run by its harness id, or null.</summary>
    Task<EvaluationRun?> FindAsync(TenantId tenant, Guid runId, CancellationToken cancellationToken);
}

/// <summary>
/// Takes a configuration version out of use in a tenant, for example when monitoring finds its quality has drifted
/// (ADR-2631). Only a new qualification restores it. The evaluation harness (#5548) calls it.
/// </summary>
public interface ITaskConfigurationSuspension
{
    /// <summary>Suspends <paramref name="configuration"/> in <paramref name="tenant"/> for <paramref name="reason"/>.</summary>
    Task<CommandResult<TaskQualificationState>> SuspendAsync(
        TenantId tenant, ConfigurationRef configuration, string reason, CancellationToken cancellationToken);
}
