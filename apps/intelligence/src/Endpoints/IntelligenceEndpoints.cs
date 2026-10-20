using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;
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

        group.MapGet("/tasks", ListTasks)
            .WithName("AiListTasks")
            .WithSummary("The agent task configurations in this deployment, with their content hashes.");

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

    internal static Ok<IReadOnlyList<AgentTaskResponse>> ListTasks(ITaskConfigurationCatalog catalog) =>
        TypedResults.Ok<IReadOnlyList<AgentTaskResponse>>([.. catalog.All.Select(AgentTaskResponse.From)]);

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

/// <summary>A task configuration as the deployment runs it.</summary>
/// <param name="Key">The task key.</param>
/// <param name="Version">The configuration version.</param>
/// <param name="Provider">The provider.</param>
/// <param name="Model">The model id or tier.</param>
/// <param name="AutonomyLevel">The highest autonomy level the task may use.</param>
/// <param name="ContentHash">The configuration's content hash (K-AI-5).</param>
public sealed record AgentTaskResponse(string Key, int Version, string Provider, string Model, string AutonomyLevel, string ContentHash)
{
    internal static AgentTaskResponse From(TaskConfiguration c) =>
        new(c.Key, c.Version, c.Provider, c.Model, c.Level.ToString(), c.ContentHash);
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
