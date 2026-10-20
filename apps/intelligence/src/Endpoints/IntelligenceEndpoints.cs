using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Evaluation;
using Cadence.Modules.Intelligence.Evaluation.Runs;
using Cadence.Modules.Intelligence.Evaluation.Sets;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Modules.Intelligence.Endpoints;

/// <summary>
/// The Intelligence module's routes under <c>/api/v1/ai</c> (ADR-2630 §4.2): the caller's own ledger entries, the task
/// configurations, and the kill switches and budgets. Writes are kernel commands, authorized by the kernel against the
/// caller's grants; the tenant comes from the signed identity (<see cref="KernelTenantFilter"/>).
/// </summary>
public static class IntelligenceEndpoints
{
    /// <summary>The module's route prefix.</summary>
    public const string RoutePrefix = "/api/v1/ai";

    /// <summary>The header carrying a command's idempotency key (K-TX-3).</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Maps the ledger, task and control routes.</summary>
    public static IEndpointRouteBuilder MapIntelligenceEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup(RoutePrefix)
            .RequireSignedInCaller("Reads are limited to the caller's own runs and the tenant's settings; every write is a kernel command authorized against the caller's grants.")
            .AddEndpointFilter<KernelTenantFilter>()
            .WithTags("AI agents");

        group.MapGet("/runs", ListRunsAsync)
            .WithName("AiListMyRuns")
            .WithSummary("The caller's latest agent runs from the interaction ledger, newest first.");

        group.MapGet("/runs/{runId:guid}", GetRunAsync)
            .WithName("AiGetMyRun")
            .WithSummary("One of the caller's agent runs with its request, answer, usage and integrity hash.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/tasks", ListTasksAsync)
            .WithName("AiListTasks")
            .WithSummary("The agent task configuration versions in this deployment, with their content hashes and the tenant's qualification of each.");

        group.MapPost("/evaluation-runs", RecordEvaluationRunAsync)
            .WithName("AiRecordEvaluationRun")
            .WithSummary("Stores an evaluation run record after checking it against its task configuration version, set, criteria and case results (evaluation_run.record, ADR-2634).")
            .ProducesKernelCommand<RecordEvaluationRun>();

        group.MapGet("/evaluation-runs/{runId:guid}", GetEvaluationRunAsync)
            .WithName("AiGetEvaluationRun")
            .WithSummary("One stored evaluation run with its record, exactly as hashed.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/tasks/{key}/evaluation-runs", ListEvaluationRunsAsync)
            .WithName("AiListEvaluationRuns")
            .WithSummary("The stored evaluation runs of a task, newest first, optionally of one version.");

        group.MapPost("/tasks/{key}/versions/{version:int}/qualification", QualifyAsync)
            .WithName("AiQualifyTask")
            .WithSummary("Qualifies a task configuration version on a stored, accepted evaluation run and the caller's signed approval (task_configuration.qualify, ADR-2631).")
            .ProducesKernelCommand<QualifyTaskConfiguration>();

        group.MapPost("/tasks/{key}/versions/{version:int}/suspension", SuspendAsync)
            .WithName("AiSuspendTask")
            .WithSummary("Takes a qualified task configuration version out of use until it is qualified again (task_configuration.suspend).")
            .ProducesKernelCommand<SuspendTaskConfiguration>();

        group.MapGet("/controls", ListControlsAsync)
            .WithName("AiListControls")
            .WithSummary("The tenant's AI kill switches and monthly token budgets.");

        group.MapPut("/controls/{scope}", SetControlAsync)
            .WithName("AiSetControl")
            .WithSummary("Switches agents on or off for every task ('*') or one task, and sets the monthly token budget (agent_control.set).")
            .ProducesKernelCommand<SetAgentControl>();

        return app;
    }

    internal static async Task<Ok<IReadOnlyList<AgentRunSummaryResponse>>> ListRunsAsync(
        HttpContext context, IAgentLedger ledger, int? limit, CancellationToken cancellationToken)
    {
        var runs = Caller(context.User) is { } caller
            ? await ledger.RunsAsync(KernelRequestTenant.Get(context), caller, limit ?? 50, cancellationToken).ConfigureAwait(false)
            : [];
        return TypedResults.Ok<IReadOnlyList<AgentRunSummaryResponse>>([.. runs.Select(AgentRunSummaryResponse.From)]);
    }

    internal static async Task<Results<Ok<AgentRunResponse>, ProblemHttpResult>> GetRunAsync(
        Guid runId, HttpContext context, IAgentLedger ledger, CancellationToken cancellationToken)
    {
        var run = await ledger.FindRunAsync(KernelRequestTenant.Get(context), runId, cancellationToken).ConfigureAwait(false);

        // Another person's run answers as missing, so the route does not reveal who used AI on what.
        return run is not null && run.RequestedBy == Caller(context.User)
            ? TypedResults.Ok(AgentRunResponse.From(run))
            : KernelErrorEndpoints.Problem(KernelErrors.NotFound.WithMessage($"You have no agent run {runId}."));
    }

    internal static async Task<Ok<IReadOnlyList<AgentTaskResponse>>> ListTasksAsync(
        HttpContext context, ITaskConfigurationCatalog catalog, IAgentLedger ledger, CancellationToken cancellationToken)
    {
        var qualifications = (await ledger.QualificationsAsync(KernelRequestTenant.Get(context), cancellationToken).ConfigureAwait(false))
            .ToDictionary(q => (q.TaskKey, q.TaskVersion));
        return TypedResults.Ok<IReadOnlyList<AgentTaskResponse>>(
            [.. catalog.All.Select(c => AgentTaskResponse.From(c, qualifications.GetValueOrDefault((c.Key, c.Version))))]);
    }

    internal static async Task<Results<Ok<EvaluationRunStateResponse>, ProblemHttpResult>> RecordEvaluationRunAsync(
        JsonElement record,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        var result = await sender.SendAsync(
            new RecordEvaluationRun(
                KernelRequestTenant.Get(context),
                record.GetRawText(),
                context.Request.Headers[IdempotencyKeyHeader].ToString(),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state ? TypedResults.Ok(EvaluationRunStateResponse.From(state)) : KernelErrorEndpoints.Problem(result.Error!);
    }

    internal static async Task<Results<Ok<EvaluationRunResponse>, ProblemHttpResult>> GetEvaluationRunAsync(
        Guid runId, HttpContext context, IEvaluationRuns runs, CancellationToken cancellationToken)
    {
        var run = await runs.FindAsync(KernelRequestTenant.Get(context), runId, cancellationToken).ConfigureAwait(false);
        return run is not null
            ? TypedResults.Ok(EvaluationRunResponse.From(run))
            : KernelErrorEndpoints.Problem(IntelligenceErrors.EvaluationRunNotFound.WithMessage($"No evaluation run {runId} is stored here."));
    }

    internal static async Task<Ok<IReadOnlyList<EvaluationRunSummaryResponse>>> ListEvaluationRunsAsync(
        string key, int? version, int? limit, HttpContext context, IEvaluationRuns runs, CancellationToken cancellationToken)
    {
        var stored = await runs.ForTaskAsync(KernelRequestTenant.Get(context), key, version, limit ?? 50, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<EvaluationRunSummaryResponse>>([.. stored.Select(EvaluationRunSummaryResponse.From)]);
    }

    internal static async Task<Results<Ok<QualificationResponse>, ProblemHttpResult>> QualifyAsync(
        string key,
        int version,
        QualificationRequest request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await sender.SendAsync(
            new QualifyTaskConfiguration(
                KernelRequestTenant.Get(context),
                new ConfigurationRef(key, version, request.ConfigurationHash),
                request.EvaluationRunId,
                new AttestationId(request.AttestationId),
                context.Request.Headers[IdempotencyKeyHeader].ToString(),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state ? TypedResults.Ok(QualificationResponse.From(state)) : KernelErrorEndpoints.Problem(result.Error!);
    }

    internal static async Task<Results<Ok<QualificationResponse>, ProblemHttpResult>> SuspendAsync(
        string key,
        int version,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        var result = await sender.SendAsync(
            new SuspendTaskConfiguration(
                KernelRequestTenant.Get(context),
                key,
                version,
                context.Request.Headers[IdempotencyKeyHeader].ToString(),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state ? TypedResults.Ok(QualificationResponse.From(state)) : KernelErrorEndpoints.Problem(result.Error!);
    }

    internal static async Task<Ok<IReadOnlyList<AgentControlResponse>>> ListControlsAsync(
        HttpContext context, IAgentLedger ledger, CancellationToken cancellationToken)
    {
        var controls = await ledger.ControlsAsync(KernelRequestTenant.Get(context), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<AgentControlResponse>>(
            [.. controls.Select(c => new AgentControlResponse(c.Scope, c.Enabled, c.MonthlyTokenLimit, c.VersionIndex))]);
    }

    internal static async Task<Results<Ok<AgentControlResponse>, ProblemHttpResult>> SetControlAsync(
        string scope,
        AgentControlRequest request,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        var result = await sender.SendAsync(
            new SetAgentControl(
                KernelRequestTenant.Get(context),
                scope,
                request?.Enabled ?? true,
                request?.MonthlyTokenLimit,
                context.Request.Headers[IdempotencyKeyHeader].ToString(),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state
            ? TypedResults.Ok(new AgentControlResponse(state.Scope, state.Enabled, state.MonthlyTokenLimit, state.VersionIndex))
            : KernelErrorEndpoints.Problem(result.Error!);
    }

    /// <summary>The caller's kernel actor id: the signed subject, as the kernel's current actor reads it.</summary>
    internal static string? Caller(ClaimsPrincipal user) =>
        user.FindFirst(CadenceClaimTypes.Subject)?.Value is { Length: > 0 } subject ? subject : null;
}

/// <summary>A kill switch and budget to set.</summary>
/// <param name="Enabled">False to switch the scope's agents off.</param>
/// <param name="MonthlyTokenLimit">The most tokens the scope may use in a calendar month (UTC); null for no limit.</param>
public sealed record AgentControlRequest(bool Enabled, long? MonthlyTokenLimit);

/// <summary>A kill switch and budget.</summary>
/// <param name="Scope">'*' for every task, or a task key.</param>
/// <param name="Enabled">False when the scope's agents are switched off.</param>
/// <param name="MonthlyTokenLimit">The monthly token budget; null for no limit.</param>
/// <param name="VersionIndex">The control's version.</param>
public sealed record AgentControlResponse(string Scope, bool Enabled, long? MonthlyTokenLimit, long VersionIndex);

/// <summary>A task configuration version as the deployment ships it, and the tenant's qualification of it.</summary>
/// <param name="Key">The task key.</param>
/// <param name="Version">The configuration version.</param>
/// <param name="Provider">The provider.</param>
/// <param name="Model">The model id or tier.</param>
/// <param name="AutonomyLevel">The highest autonomy level the task may use.</param>
/// <param name="ContentHash">The configuration's kernel content hash (K-AI-5), which a qualification signs.</param>
/// <param name="Tools">The tools the agent may call.</param>
/// <param name="RetrievalSources">The sources its grounding may read.</param>
/// <param name="EvaluationSet">The pinned evaluation set, when there is one.</param>
/// <param name="AcceptanceCriteria">What an evaluation run must show.</param>
/// <param name="RiskClass">The credibility assessment's risk class, when it is complete.</param>
/// <param name="QualificationGaps">What the configuration lacks before it can be qualified; empty when it can be.</param>
/// <param name="Qualification">The tenant's qualification of this version, or null when it has none.</param>
public sealed record AgentTaskResponse(
    string Key,
    int Version,
    string Provider,
    string Model,
    string AutonomyLevel,
    string ContentHash,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> RetrievalSources,
    EvaluationSetRef? EvaluationSet,
    AcceptanceCriteria? AcceptanceCriteria,
    string? RiskClass,
    IReadOnlyList<string> QualificationGaps,
    TaskQualificationResponse? Qualification)
{
    internal static AgentTaskResponse From(TaskConfiguration c, TaskQualification? q) =>
        new(
            c.Key,
            c.Version,
            c.Provider,
            c.Model,
            c.Level.ToString(),
            c.ContentHash,
            c.Tools,
            c.RetrievalSources,
            c.EvaluationSet,
            c.AcceptanceCriteria,
            c.Credibility?.RiskClass.ToString(),
            c.QualificationGaps(),
            q is null ? null : TaskQualificationResponse.From(q, c));
}

/// <summary>The tenant's qualification of one configuration version.</summary>
/// <param name="Status">Qualified or Suspended.</param>
/// <param name="Usable">True when agents may use the version now: qualified on its current content.</param>
/// <param name="ConfigurationHash">The content hash qualified.</param>
/// <param name="RiskClass">The risk class when it was qualified.</param>
/// <param name="EvaluationRunReference">The evaluation run it rests on.</param>
/// <param name="EvaluationRunHash">The content hash of the stored run record.</param>
/// <param name="QualifiedAt">When it was qualified.</param>
/// <param name="SuspendedAt">When it was suspended, while it is.</param>
/// <param name="SuspensionReason">Why it was suspended, while it is.</param>
public sealed record TaskQualificationResponse(
    string Status,
    bool Usable,
    string ConfigurationHash,
    string RiskClass,
    string EvaluationRunReference,
    string EvaluationRunHash,
    DateTimeOffset QualifiedAt,
    DateTimeOffset? SuspendedAt,
    string? SuspensionReason)
{
    internal static TaskQualificationResponse From(TaskQualification q, TaskConfiguration c) =>
        new(
            q.Status.ToString(),
            TaskQualification.WhyNotUsable(q, c) is null,
            q.ConfigurationHash,
            q.RiskClass.ToString(),
            q.EvaluationRunReference,
            q.EvaluationRunHash,
            q.QualifiedAt,
            q.SuspendedAt,
            q.SuspensionReason);
}

/// <summary>A qualification to record: the content hash qualified, the stored evaluation run and the signer's attestation.</summary>
/// <param name="ConfigurationHash">The content hash of the version being qualified.</param>
/// <param name="EvaluationRunId">The stored evaluation run it rests on (<c>POST /api/v1/ai/evaluation-runs</c>).</param>
/// <param name="AttestationId">The caller's <c>Approved</c> attestation over the version (kind <c>task_configuration</c>).</param>
public sealed record QualificationRequest(string ConfigurationHash, Guid EvaluationRunId, Guid AttestationId);

/// <summary>A stored evaluation run after it was recorded.</summary>
/// <param name="RunId">The run.</param>
/// <param name="TaskKey">The task.</param>
/// <param name="TaskVersion">The configuration version.</param>
/// <param name="ConfigurationHash">The configuration's content hash.</param>
/// <param name="EvaluationSetHash">The content hash of the set it used.</param>
/// <param name="RecordHash">The stored record's content hash, which a qualification binds to.</param>
/// <param name="Accepted">True when it met the acceptance criteria.</param>
/// <param name="Reasons">Why it was not accepted; empty when it was.</param>
public sealed record EvaluationRunStateResponse(
    Guid RunId, string TaskKey, int TaskVersion, string ConfigurationHash, string EvaluationSetHash, string RecordHash, bool Accepted, IReadOnlyList<string> Reasons)
{
    internal static EvaluationRunStateResponse From(EvaluationRunState s) =>
        new(s.RunId, s.Configuration.Key, s.Configuration.Version, s.Configuration.ContentHash, s.EvaluationSetHash, s.RecordHash, s.Accepted, s.Reasons);
}

/// <summary>A stored evaluation run in a list.</summary>
/// <param name="RunId">The run.</param>
/// <param name="TaskKey">The task.</param>
/// <param name="TaskVersion">The configuration version.</param>
/// <param name="ConfigurationHash">The configuration's content hash.</param>
/// <param name="EvaluationSetId">The set's id.</param>
/// <param name="EvaluationSetVersion">The set's version.</param>
/// <param name="EvaluationSetHash">The set's content hash.</param>
/// <param name="Provider">The provider.</param>
/// <param name="Model">The model.</param>
/// <param name="Accepted">True when it met the acceptance criteria.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="CompletedAt">When it ended.</param>
/// <param name="RecordHash">The stored record's content hash.</param>
public sealed record EvaluationRunSummaryResponse(
    Guid RunId,
    string TaskKey,
    int TaskVersion,
    string ConfigurationHash,
    string EvaluationSetId,
    int EvaluationSetVersion,
    string EvaluationSetHash,
    string Provider,
    string Model,
    bool Accepted,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string RecordHash)
{
    internal static EvaluationRunSummaryResponse From(EvaluationRun r) =>
        new(r.RunId, r.TaskKey, r.TaskVersion, r.ConfigurationHash, r.EvaluationSetId, r.EvaluationSetVersion, r.EvaluationSetHash, r.Provider, r.Model, r.Accepted, r.StartedAt, r.CompletedAt, r.RecordHash);
}

/// <summary>A stored evaluation run with its record.</summary>
/// <param name="Run">The run's identity and outcome.</param>
/// <param name="RecordJson">The record exactly as hashed; recomputing its kernel content hash gives <c>RecordHash</c>.</param>
public sealed record EvaluationRunResponse(EvaluationRunSummaryResponse Run, string RecordJson)
{
    internal static EvaluationRunResponse From(EvaluationRun r) => new(EvaluationRunSummaryResponse.From(r), r.RecordJson);
}

/// <summary>A configuration version's qualification after a change.</summary>
/// <param name="TaskKey">The task.</param>
/// <param name="TaskVersion">The version.</param>
/// <param name="ConfigurationHash">The content hash covered.</param>
/// <param name="Status">Qualified or Suspended.</param>
/// <param name="RiskClass">The risk class.</param>
/// <param name="VersionIndex">The record's version.</param>
public sealed record QualificationResponse(string TaskKey, int TaskVersion, string ConfigurationHash, string Status, string RiskClass, long VersionIndex)
{
    internal static QualificationResponse From(TaskQualificationState s) =>
        new(s.TaskKey, s.TaskVersion, s.ConfigurationHash, s.Status.ToString(), s.RiskClass.ToString(), s.VersionIndex);
}

/// <summary>An agent run in a list.</summary>
/// <param name="RunId">The run.</param>
/// <param name="TaskKey">The task.</param>
/// <param name="TaskVersion">The configuration version.</param>
/// <param name="Model">The model id.</param>
/// <param name="Status">Started, Succeeded or Failed.</param>
/// <param name="StartedAt">When the run was recorded.</param>
/// <param name="CompletedAt">When it ended.</param>
/// <param name="InputTokens">Input tokens used.</param>
/// <param name="OutputTokens">Output tokens used.</param>
/// <param name="CostUsd">What the tokens cost, when priced.</param>
public sealed record AgentRunSummaryResponse(
    Guid RunId,
    string TaskKey,
    int TaskVersion,
    string Model,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long InputTokens,
    long OutputTokens,
    decimal? CostUsd)
{
    internal static AgentRunSummaryResponse From(AgentRun r) =>
        new(r.Id, r.TaskKey, r.TaskVersion, r.Model, r.Status.ToString(), r.StartedAt, r.CompletedAt, r.InputTokens, r.OutputTokens, r.CostUsd);
}

/// <summary>One agent run in full.</summary>
/// <param name="Summary">The run's summary.</param>
/// <param name="Provider">The provider.</param>
/// <param name="ConfigurationHash">The configuration's content hash at the time of the run.</param>
/// <param name="RequestJson">The messages sent to the model.</param>
/// <param name="ResponseText">The model's answer, when it succeeded.</param>
/// <param name="FailureReason">Why it failed, when it did.</param>
/// <param name="ContentHash">The completed entry's integrity hash.</param>
/// <param name="ContentHashVerified">True when the stored hash matches the entry as read.</param>
public sealed record AgentRunResponse(
    AgentRunSummaryResponse Summary,
    string Provider,
    string ConfigurationHash,
    string RequestJson,
    string? ResponseText,
    string? FailureReason,
    string? ContentHash,
    bool ContentHashVerified)
{
    internal static AgentRunResponse From(AgentRun r) =>
        new(
            AgentRunSummaryResponse.From(r),
            r.Provider,
            r.ConfigurationHash,
            r.RequestJson,
            r.ResponseText,
            r.FailureReason,
            r.ContentHash,
            r.ContentHash is not null && string.Equals(r.ContentHash, r.ComputeContentHash(), StringComparison.Ordinal));
}
