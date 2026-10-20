using System;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Modules.Intelligence.Domain;

namespace Cadence.Modules.Intelligence.Application;

/// <summary>
/// Records an agent run in the interaction ledger before its model call, for the person sending it (K-AI-6). Refused
/// when a kill switch covers the task. A person needs <c>agent_run.record</c> to use AI at all; it is a Propose
/// capability, because nothing an agent produces is more than a suggestion (K-AI-1).
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="RunId">The run's id, chosen by the runtime.</param>
/// <param name="Configuration">The task configuration the run uses.</param>
/// <param name="Model">The model id after any tier was resolved.</param>
/// <param name="RequestJson">The messages sent to the model, as JSON.</param>
/// <param name="IdempotencyKey">The command's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Why the run is recorded.</param>
[KernelCapability("agent_run.record", VerbClass.Propose)]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(typeof(IntelligenceErrors), nameof(IntelligenceErrors.AgentDisabled))]
public sealed record StartAgentRun(
    TenantId TenantId,
    Guid RunId,
    TaskConfiguration Configuration,
    string Model,
    string RequestJson,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<Guid>;

/// <summary>Records how an agent run ended: the model's answer and usage, or why it failed.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="RunId">The run.</param>
/// <param name="Succeeded">True when the model answered.</param>
/// <param name="ResponseText">The answer, when it succeeded.</param>
/// <param name="FailureReason">Why it failed, when it did.</param>
/// <param name="InputTokens">Input tokens the provider reported.</param>
/// <param name="OutputTokens">Output tokens the provider reported.</param>
/// <param name="CostUsd">What the tokens cost, when the model has a configured price.</param>
/// <param name="IdempotencyKey">The command's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Why the run is completed.</param>
[KernelCapability("agent_run.record", VerbClass.Propose)]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.NotFound))]
[RaisesKernelErrors(typeof(IntelligenceErrors), nameof(IntelligenceErrors.RunCompleted), nameof(IntelligenceErrors.RunNotYours))]
public sealed record CompleteAgentRun(
    TenantId TenantId,
    Guid RunId,
    bool Succeeded,
    string? ResponseText,
    string? FailureReason,
    long InputTokens,
    long OutputTokens,
    decimal? CostUsd,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<AgentRunStatus>;

/// <summary>
/// Switches agents on or off for the whole tenant or one task, and sets the monthly token budget (ADR-2630 §4.2). It
/// changes no clinical data, but it governs AI use, so it is an Execute capability a person holds by grant.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="Scope"><see cref="AgentControl.AllTasks"/>, or a task key.</param>
/// <param name="Enabled">False to switch the scope's agents off.</param>
/// <param name="MonthlyTokenLimit">The most tokens the scope may use in a calendar month; null for no limit.</param>
/// <param name="IdempotencyKey">The command's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Why the control changes.</param>
[KernelCapability("agent_control.set", VerbClass.Execute)]
[RaisesKernelErrors(typeof(KernelErrors))]
public sealed record SetAgentControl(
    TenantId TenantId,
    string Scope,
    bool Enabled,
    long? MonthlyTokenLimit,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<AgentControlState>;

/// <summary>A control after a change.</summary>
/// <param name="Scope"><see cref="AgentControl.AllTasks"/>, or a task key.</param>
/// <param name="Enabled">False when the scope's agents are switched off.</param>
/// <param name="MonthlyTokenLimit">The monthly token budget; null for no limit.</param>
/// <param name="VersionIndex">The control's version after the change.</param>
public sealed record AgentControlState(string Scope, bool Enabled, long? MonthlyTokenLimit, long VersionIndex);

/// <summary>Raised when an agent run is completed; the measurement layer counts outcomes and spend from it (#5548).</summary>
/// <param name="RunId">The run.</param>
/// <param name="TaskKey">The task.</param>
/// <param name="TaskVersion">The task configuration's version.</param>
/// <param name="Status">How the run ended.</param>
/// <param name="InputTokens">Input tokens used.</param>
/// <param name="OutputTokens">Output tokens used.</param>
[KernelEvent("intelligence.AgentRunCompleted", 1)]
public sealed record AgentRunCompleted(Guid RunId, string TaskKey, int TaskVersion, AgentRunStatus Status, long InputTokens, long OutputTokens);

/// <summary>Raised when a kill switch or budget changes.</summary>
/// <param name="Scope">The scope.</param>
/// <param name="Enabled">False when switched off.</param>
/// <param name="MonthlyTokenLimit">The monthly token budget; null for no limit.</param>
[KernelEvent("intelligence.AgentControlChanged", 1)]
public sealed record AgentControlChanged(string Scope, bool Enabled, long? MonthlyTokenLimit);
