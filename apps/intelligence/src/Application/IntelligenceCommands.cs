using System;
using System.Collections.Generic;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Evaluation;

namespace Cadence.Modules.Intelligence.Application;

/// <summary>
/// Records an agent run in the interaction ledger before its model call, for the person sending it (K-AI-6). Refused
/// when a kill switch covers the task, and when the configuration is not the deployment's or is not qualified in the
/// tenant on its current content (ADR-2631). A person needs <c>agent_run.record</c> to use AI at all; it is a Propose
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
[RaisesKernelErrors(
    typeof(IntelligenceErrors),
    nameof(IntelligenceErrors.AgentDisabled),
    nameof(IntelligenceErrors.TaskUnknown),
    nameof(IntelligenceErrors.ConfigurationChanged),
    nameof(IntelligenceErrors.ConfigurationUnqualified))]
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
/// <param name="Inference">Where the model call went and what the outbound guard scrubbed (ADR-2636), or null without the inference layer.</param>
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
    string ReasonForChange,
    AgentRunInference? Inference = null) : ICommand<AgentRunStatus>;

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

/// <summary>
/// Stores an evaluation run record (ADR-2634 §4.4, #5574) as the evidence a qualification can rest on. Refused unless
/// the record evaluated a configuration version this deployment ships, on its current content, against exactly its
/// pinned evaluation set, acceptance criteria and provider, and its metrics and verdict are the ones its case results
/// give (<see cref="EvaluationRun.Problems"/>). A rejected run is stored too, as the record that it failed. Execute verb
/// class: it records evidence and qualifies nothing.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="RecordJson">The harness's run record, as JSON (<see cref="Evaluation.Runs.EvaluationRunRecord.ToJson"/>).</param>
/// <param name="IdempotencyKey">The command's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Why the run is recorded.</param>
[KernelCapability("evaluation_run.record", VerbClass.Execute)]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(IntelligenceErrors),
    nameof(IntelligenceErrors.TaskUnknown),
    nameof(IntelligenceErrors.ConfigurationChanged),
    nameof(IntelligenceErrors.ConfigurationNotQualifiable),
    nameof(IntelligenceErrors.EvaluationRunInvalid))]
public sealed record RecordEvaluationRun(
    TenantId TenantId,
    string RecordJson,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<EvaluationRunState>;

/// <summary>A stored evaluation run.</summary>
/// <param name="RunId">The run.</param>
/// <param name="Configuration">The configuration version it evaluated.</param>
/// <param name="EvaluationSetHash">The content hash of the set it used.</param>
/// <param name="RecordHash">The kernel content hash of the stored record, which a qualification binds to.</param>
/// <param name="Accepted">True when it met the configuration's acceptance criteria.</param>
/// <param name="Reasons">Why it was not accepted; empty when it was.</param>
public sealed record EvaluationRunState(
    Guid RunId, ConfigurationRef Configuration, string EvaluationSetHash, string RecordHash, bool Accepted, IReadOnlyList<string> Reasons);

/// <summary>Raised when an evaluation run is stored.</summary>
/// <param name="RunId">The run.</param>
/// <param name="TaskKey">The task.</param>
/// <param name="TaskVersion">The configuration version.</param>
/// <param name="ConfigurationHash">The configuration's content hash.</param>
/// <param name="RecordHash">The stored record's content hash.</param>
/// <param name="Accepted">Whether it met the acceptance criteria.</param>
[KernelEvent("intelligence.EvaluationRunRecorded", 1)]
public sealed record EvaluationRunRecorded(Guid RunId, string TaskKey, int TaskVersion, string ConfigurationHash, string RecordHash, bool Accepted);

/// <summary>
/// Qualifies one task configuration version in the tenant (ADR-2631): a stored evaluation run of exactly this version
/// met its acceptance criteria against the pinned evaluation set (#5574), and the person sending the command signed an
/// <c>Approved</c> attestation over the configuration's content hash (21 CFR 11.50, 11.70). The qualification records
/// the run's id and content hash. Approve verb class: only a person can qualify, never an agent. A suspended version is
/// qualified again the same way, on a stored run and a new signature.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="Configuration">The version and the content hash being qualified.</param>
/// <param name="EvaluationRunId">The stored evaluation run the qualification rests on (<see cref="RecordEvaluationRun"/>).</param>
/// <param name="AttestationId">The signer's attestation over the configuration version.</param>
/// <param name="IdempotencyKey">The command's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Why the version is qualified.</param>
[KernelCapability("task_configuration.qualify", VerbClass.Approve)]
[RaisesKernelErrors(
    typeof(IntelligenceErrors),
    nameof(IntelligenceErrors.TaskUnknown),
    nameof(IntelligenceErrors.ConfigurationChanged),
    nameof(IntelligenceErrors.ConfigurationNotQualifiable),
    nameof(IntelligenceErrors.EvaluationRunNotFound),
    nameof(IntelligenceErrors.EvaluationNotAccepted),
    nameof(IntelligenceErrors.AttestationRejected),
    nameof(IntelligenceErrors.AlreadyQualified))]
public sealed record QualifyTaskConfiguration(
    TenantId TenantId,
    ConfigurationRef Configuration,
    Guid EvaluationRunId,
    AttestationId AttestationId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<TaskQualificationState>;

/// <summary>
/// Takes a qualified configuration version out of use in the tenant until it is qualified again (ADR-2631), for
/// example when monitoring finds its quality has drifted. Execute verb class: it stops AI use and changes no clinical
/// data, so the evaluation harness can send it for a person holding the grant.
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="TaskKey">The task.</param>
/// <param name="TaskVersion">The version to suspend.</param>
/// <param name="IdempotencyKey">The command's idempotency key (K-TX-3).</param>
/// <param name="ReasonForChange">Why the version is suspended; shown to anyone the suspension refuses.</param>
[KernelCapability("task_configuration.suspend", VerbClass.Execute)]
[RaisesKernelErrors(typeof(IntelligenceErrors), nameof(IntelligenceErrors.NotQualified))]
public sealed record SuspendTaskConfiguration(
    TenantId TenantId,
    string TaskKey,
    int TaskVersion,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<TaskQualificationState>;

/// <summary>A configuration version's qualification after a change.</summary>
/// <param name="TaskKey">The task.</param>
/// <param name="TaskVersion">The version.</param>
/// <param name="ConfigurationHash">The content hash the qualification covers.</param>
/// <param name="Status">Whether the version may be used.</param>
/// <param name="RiskClass">The configuration's risk class.</param>
/// <param name="VersionIndex">The qualification record's version after the change.</param>
public sealed record TaskQualificationState(
    string TaskKey, int TaskVersion, string ConfigurationHash, QualificationStatus Status, ModelRiskClass RiskClass, long VersionIndex);

/// <summary>Raised when a configuration version is qualified, or qualified again.</summary>
/// <param name="TaskKey">The task.</param>
/// <param name="TaskVersion">The version.</param>
/// <param name="ConfigurationHash">The content hash qualified.</param>
/// <param name="RiskClass">The risk class.</param>
/// <param name="EvaluationRunReference">The evaluation run it rests on.</param>
/// <param name="AttestationId">The signer's attestation.</param>
[KernelEvent("intelligence.TaskConfigurationQualified", 1)]
public sealed record TaskConfigurationQualified(
    string TaskKey, int TaskVersion, string ConfigurationHash, ModelRiskClass RiskClass, string EvaluationRunReference, Guid AttestationId);

/// <summary>Raised when a configuration version is suspended.</summary>
/// <param name="TaskKey">The task.</param>
/// <param name="TaskVersion">The version.</param>
/// <param name="Reason">Why.</param>
[KernelEvent("intelligence.TaskConfigurationSuspended", 1)]
public sealed record TaskConfigurationSuspended(string TaskKey, int TaskVersion, string Reason);
