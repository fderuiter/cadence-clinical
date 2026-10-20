using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Time;
using Cadence.Modules.Intelligence.Domain;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Cadence.Modules.Intelligence.Application;

/// <summary>What a caller asks an agent task to do.</summary>
/// <param name="Tenant">The tenant, from the caller's signed identity.</param>
/// <param name="TaskKey">The task configuration's key.</param>
/// <param name="InstructionValues">Values for the instructions template's placeholders.</param>
/// <param name="Messages">The conversation to send after the instructions: user and assistant turns.</param>
public sealed record AgentRequest(
    TenantId Tenant,
    string TaskKey,
    IReadOnlyDictionary<string, string> InstructionValues,
    IReadOnlyList<ChatMessage> Messages);

/// <summary>How a run ended.</summary>
/// <param name="RunId">The ledger entry.</param>
/// <param name="Status">Succeeded or Failed.</param>
/// <param name="Text">The model's answer, when it succeeded.</param>
/// <param name="Configuration">The task configuration the run used (K-AI-5).</param>
/// <param name="Model">The model id after any tier was resolved.</param>
/// <param name="InputTokens">Input tokens used.</param>
/// <param name="OutputTokens">Output tokens used.</param>
public sealed record AgentResponse(
    Guid RunId,
    AgentRunStatus Status,
    string? Text,
    TaskConfiguration Configuration,
    string Model,
    long InputTokens,
    long OutputTokens);

/// <summary>Settings of the runtime.</summary>
/// <param name="RequestTimeout">How long one model call may take before the run fails.</param>
public sealed record AgentRunnerOptions(TimeSpan RequestTimeout)
{
    /// <summary>Two minutes.</summary>
    public static AgentRunnerOptions Default { get; } = new(TimeSpan.FromMinutes(2));
}

/// <summary>
/// Runs one agent task for the person sending the request (ADR-2630 §4.2, K-AI-6). In order: the newest configuration
/// version the tenant qualified (ADR-2631), its provider and model, the kill switches and the monthly budget; then the
/// run is recorded in the ledger through a kernel command, the model is called, and the run is completed with the
/// answer or the failure. Every refusal is a registered error and says the work can go on without AI (K-AI-8). The runtime writes nothing else: what an agent
/// suggests reaches clinical data only as a Proposal a person approves (K-AI-1).
/// </summary>
public sealed partial class AgentRunner(
    ITaskConfigurationCatalog catalog,
    IModelProviderRegistry providers,
    IRunChatClientFactory clients,
    IAgentLedger ledger,
    IKernelCommandSender sender,
    IClock clock,
    AgentRunnerOptions options,
    ILogger<AgentRunner> logger)
{
    /// <summary>Runs <paramref name="request"/>; the result is the answer, or why there is none.</summary>
    public async Task<CommandResult<AgentResponse>> RunAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var versions = catalog.Versions(request.TaskKey);
        if (versions.Count == 0)
        {
            return IntelligenceErrors.TaskUnknown;
        }

        // Route to the newest version the tenant qualified on its current content (ADR-2631); when none is usable,
        // say why for the newest version.
        var qualifications = (await ledger.QualificationsAsync(request.Tenant, cancellationToken).ConfigureAwait(false))
            .Where(q => q.TaskKey == request.TaskKey)
            .ToDictionary(q => q.TaskVersion);
        var configuration = versions.FirstOrDefault(c => TaskQualification.WhyNotUsable(qualifications.GetValueOrDefault(c.Version), c) is null);
        if (configuration is null)
        {
            return IntelligenceErrors.ConfigurationUnqualified.WithMessage(
                TaskQualification.WhyNotUsable(qualifications.GetValueOrDefault(versions[0].Version), versions[0])!);
        }

        if (providers.Find(configuration.Provider) is not { } provider
            || provider.ResolveModel(configuration.Model) is not { } model)
        {
            return IntelligenceErrors.ProviderUnavailable;
        }

        if (await RefusalAsync(request.Tenant, configuration.Key, cancellationToken).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        List<ChatMessage> messages =
        [
            new(ChatRole.System, PromptTemplate.Render(configuration.Instructions, request.InstructionValues)),
            .. request.Messages,
        ];
        var runId = Guid.CreateVersion7(clock.UtcNow);
        var routed = await clients.CreateAsync(new AgentRunCall(request.Tenant, runId, configuration, provider, model), cancellationToken)
            .ConfigureAwait(false);
        if (routed is not { IsSuccess: true, Value: { } run })
        {
            return routed.Error!;
        }

        var reason = $"Agent run of {configuration.Key} v{configuration.Version}";
        var started = await sender.SendAsync(
            new StartAgentRun(request.Tenant, runId, configuration, model, RequestJson(messages), $"intelligence:run:{runId:N}:start", reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!started.IsSuccess)
        {
            return started.Error!;
        }

        var (succeeded, text, failure, inputTokens, outputTokens, scrubbed) = await CallModelAsync(
            provider, run.Client, model, configuration, messages, cancellationToken).ConfigureAwait(false);
        var completed = await sender.SendAsync(
            new CompleteAgentRun(
                request.Tenant,
                runId,
                succeeded,
                text,
                failure,
                inputTokens,
                outputTokens,
                provider.Cost(model, inputTokens, outputTokens),
                $"intelligence:run:{runId:N}:complete",
                reason,
                run.Inference is { } inference ? inference with { Scrubbed = scrubbed } : null),
            cancellationToken: CancellationToken.None).ConfigureAwait(false);
        if (!completed.IsSuccess)
        {
            LogCompletionFailed(logger, runId, completed.Error!.Code);
            return completed.Error!;
        }

        LogRun(logger, runId, configuration.Key, configuration.Version, completed.Value, inputTokens, outputTokens);
        return CommandResult<AgentResponse>.Success(new AgentResponse(
            runId, completed.Value, succeeded ? text : null, configuration, model, inputTokens, outputTokens));
    }

    /// <summary>The first month of the calendar (UTC) that contains <paramref name="now"/>.</summary>
    internal static DateTimeOffset MonthStart(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private async Task<KernelError?> RefusalAsync(TenantId tenant, string taskKey, CancellationToken cancellationToken)
    {
        var controls = (await ledger.ControlsAsync(tenant, cancellationToken).ConfigureAwait(false))
            .Where(c => c.Scope == AgentControl.AllTasks || c.Scope == taskKey)
            .ToList();
        if (controls.Any(c => !c.Enabled))
        {
            return IntelligenceErrors.AgentDisabled;
        }

        var since = MonthStart(clock.UtcNow);
        foreach (var control in controls.Where(c => c.MonthlyTokenLimit is not null))
        {
            var used = await ledger.TokensUsedAsync(tenant, control.Scope == AgentControl.AllTasks ? null : taskKey, since, cancellationToken)
                .ConfigureAwait(false);
            if (used >= control.MonthlyTokenLimit)
            {
                return IntelligenceErrors.BudgetExhausted;
            }
        }

        return null;
    }

    private async Task<(bool Succeeded, string? Text, string? Failure, long InputTokens, long OutputTokens, IReadOnlyDictionary<string, int>? Scrubbed)> CallModelAsync(
        IModelProvider provider,
        IChatClient client,
        string model,
        TaskConfiguration configuration,
        List<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RequestTimeout);
        try
        {
            var response = await client.GetResponseAsync(
                messages,
                new ChatOptions { ModelId = model, MaxOutputTokens = configuration.MaxOutputTokens },
                timeout.Token).ConfigureAwait(false);
            object? counts = null;
            response.AdditionalProperties?.TryGetValue(AgentRunInference.ScrubCountsKey, out counts);
            var scrubbed = counts as IReadOnlyDictionary<string, int>;
            return (true, response.Text, null, response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0, scrubbed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (false, null, "The request was cancelled before the model answered.", 0, 0, null);
        }
        catch (OperationCanceledException)
        {
            return (false, null, $"The model did not answer within {options.RequestTimeout.TotalSeconds:0} seconds.", 0, 0, null);
        }
#pragma warning disable CA1031 // Any provider failure fails the run and is recorded; it never escapes as a server error.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogModelCallFailed(logger, ex, provider.Name, model);
            return (false, null, $"The {provider.Name} model call failed ({ex.GetType().Name}).", 0, 0, null);
        }
    }

    private static string RequestJson(IEnumerable<ChatMessage> messages) =>
        JsonSerializer.Serialize(messages.Select(m => new { role = m.Role.Value, text = m.Text }));

    // Prompts and answers can carry participant data, so only ids, counts and codes are logged.
    [LoggerMessage(EventId = 6301, Level = LogLevel.Information, Message = "Agent run {RunId} of {TaskKey} v{TaskVersion} ended {Status} ({InputTokens} in, {OutputTokens} out)")]
    private static partial void LogRun(ILogger logger, Guid runId, string taskKey, int taskVersion, AgentRunStatus status, long inputTokens, long outputTokens);

    [LoggerMessage(EventId = 6302, Level = LogLevel.Warning, Message = "The {Provider} call for model {Model} failed")]
    private static partial void LogModelCallFailed(ILogger logger, Exception exception, string provider, string model);

    [LoggerMessage(EventId = 6303, Level = LogLevel.Error, Message = "Agent run {RunId} could not be completed in the ledger: {Code}")]
    private static partial void LogCompletionFailed(ILogger logger, Guid runId, string code);
}
