using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Core.Domain;
using Cadence.Kernel;

namespace Cadence.Modules.Intelligence.Domain;

/// <summary>Where an agent run stands.</summary>
public enum AgentRunStatus
{
    /// <summary>Recorded before the model was called.</summary>
    Started = 0,

    /// <summary>The model answered.</summary>
    Succeeded = 1,

    /// <summary>The model call failed or was cancelled; nothing it produced is kept.</summary>
    Failed = 2,
}

/// <summary>
/// One entry of the interaction ledger (ADR-2630 §4.2, 21 CFR 11.10(e)): who asked which task configuration what, what
/// the model answered, the tokens it used and what they cost. It is written only by the module's commands, so both its
/// versions (started, completed) sit on the kernel audit chain. <see cref="ContentHash"/> proves the completed entry's
/// integrity; identical regeneration is not promised.
/// </summary>
public sealed class AgentRun : AggregateRoot<Guid>
{
    /// <summary>Decimal places of <see cref="CostUsd"/>, as the ledger stores it.</summary>
    public const int CostDecimals = 6;

    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    private AgentRun()
    {
    }

    private AgentRun(
        Guid id,
        TenantId tenantId,
        TaskConfiguration configuration,
        string model,
        string requestedBy,
        string requestJson,
        DateTimeOffset startedAt)
        : base(id)
    {
        TenantId = tenantId;
        TaskKey = configuration.Key;
        TaskVersion = configuration.Version;
        ConfigurationHash = configuration.ContentHash;
        Provider = configuration.Provider;
        Model = model;
        RequestedBy = requestedBy;
        RequestJson = requestJson;
        StartedAt = ToStoredPrecision(startedAt);
        Status = AgentRunStatus.Started;
    }

    /// <summary>The tenant the run belongs to.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The task configuration's key.</summary>
    public string TaskKey { get; private set; } = string.Empty;

    /// <summary>The task configuration's version (K-AI-5).</summary>
    public int TaskVersion { get; private set; }

    /// <summary>The task configuration's content hash at the time of the run.</summary>
    public string ConfigurationHash { get; private set; } = string.Empty;

    /// <summary>The provider that served the run.</summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>The model id the run asked for, after any tier was resolved.</summary>
    public string Model { get; private set; } = string.Empty;

    /// <summary>The kernel actor id of the person the agent acted for (K-AI-6).</summary>
    public string RequestedBy { get; private set; } = string.Empty;

    /// <summary>The messages sent to the model, as JSON.</summary>
    public string RequestJson { get; private set; } = string.Empty;

    /// <summary>When the run was recorded, from the kernel clock.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Where the run stands.</summary>
    public AgentRunStatus Status { get; private set; }

    /// <summary>The model's answer, when it succeeded.</summary>
    public string? ResponseText { get; private set; }

    /// <summary>Why the run failed, when it did. Never the model's partial output.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Input tokens the provider reported.</summary>
    public long InputTokens { get; private set; }

    /// <summary>Output tokens the provider reported.</summary>
    public long OutputTokens { get; private set; }

    /// <summary>What the tokens cost in US dollars, or null when the model has no configured price.</summary>
    public decimal? CostUsd { get; private set; }

    /// <summary>When the run was completed, from the kernel clock.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>The SHA-256 of the completed entry's canonical JSON, lower-case hex; null while started.</summary>
    public string? ContentHash { get; private set; }

    /// <summary>Records a run before its model call.</summary>
    public static AgentRun Start(
        Guid id,
        TenantId tenantId,
        TaskConfiguration configuration,
        string model,
        string requestedBy,
        string requestJson,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestJson);
        return new AgentRun(id, tenantId, configuration, model, requestedBy, requestJson, startedAt);
    }

    /// <summary>Records the model's answer and usage.</summary>
    /// <exception cref="InvalidOperationException">The run is already completed.</exception>
    public void Succeed(string responseText, long inputTokens, long outputTokens, decimal? costUsd, DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(responseText);
        Complete(AgentRunStatus.Succeeded, responseText, null, inputTokens, outputTokens, costUsd, completedAt);
    }

    /// <summary>Records that the model call failed.</summary>
    /// <exception cref="InvalidOperationException">The run is already completed.</exception>
    public void Fail(string failureReason, long inputTokens, long outputTokens, decimal? costUsd, DateTimeOffset completedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        Complete(AgentRunStatus.Failed, null, failureReason, inputTokens, outputTokens, costUsd, completedAt);
    }

    /// <summary>The hash the completed entry should carry; equal to <see cref="ContentHash"/> unless the row was altered.</summary>
    public string ComputeContentHash() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        Id,
        Tenant = TenantId.Value,
        TaskKey,
        TaskVersion,
        ConfigurationHash,
        Provider,
        Model,
        RequestedBy,
        RequestJson,
        StartedAt = StartedAt.UtcTicks,
        Status = (int)Status,
        ResponseText,
        FailureReason,
        InputTokens,
        OutputTokens,
        CostUsd = CostUsd?.ToString("F6", CultureInfo.InvariantCulture),
        CompletedAt = CompletedAt?.UtcTicks,
    }))));

    /// <summary>
    /// The value as PostgreSQL stores it: UTC, whole microseconds. Stored and hashed values must agree, or a re-read entry
    /// would not match its own hash.
    /// </summary>
    internal static DateTimeOffset ToStoredPrecision(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.UtcTicks - (utc.UtcTicks % TicksPerMicrosecond), TimeSpan.Zero);
    }

    private void Complete(
        AgentRunStatus status,
        string? responseText,
        string? failureReason,
        long inputTokens,
        long outputTokens,
        decimal? costUsd,
        DateTimeOffset completedAt)
    {
        if (Status != AgentRunStatus.Started)
        {
            throw new InvalidOperationException($"Agent run {Id} is already {Status}.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);
        if (costUsd is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(costUsd), costUsd, "A cost is never negative.");
        }

        Status = status;
        ResponseText = responseText;
        FailureReason = failureReason;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CostUsd = costUsd is { } cost ? decimal.Round(cost, CostDecimals, MidpointRounding.AwayFromZero) : null;
        CompletedAt = ToStoredPrecision(completedAt);
        ContentHash = ComputeContentHash();
    }
}
