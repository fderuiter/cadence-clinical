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
