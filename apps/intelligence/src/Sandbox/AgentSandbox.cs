using System;
using System.Threading;

using Cadence.Kernel;

namespace Cadence.Modules.Intelligence.Sandbox;

/// <summary>The bounds of one agent run (ADR-2636 §4.4), taken from its task configuration.</summary>
/// <param name="WallClock">The longest the run may take.</param>
/// <param name="MaxModelCalls">The most model calls (steps) it may make.</param>
/// <param name="MaxTotalTokens">The most input plus output tokens it may use.</param>
public sealed record SandboxLimits(TimeSpan WallClock, int MaxModelCalls, long MaxTotalTokens)
{
    /// <summary>Bounds for a run whose configuration sets none: ten minutes, 50 calls, two million tokens.</summary>
    public static SandboxLimits Default { get; } = new(TimeSpan.FromMinutes(10), 50, 2_000_000);
}

/// <summary>Why a sandboxed run was stopped.</summary>
public enum SandboxStopReason
{
    /// <summary>The run used its wall-clock time.</summary>
    WallClock,

    /// <summary>The run used its model calls.</summary>
    ModelCalls,

    /// <summary>The run used its tokens.</summary>
    Tokens,
}

/// <summary>A sandboxed run went past one of its <see cref="SandboxLimits"/>; the run ends with the reason.</summary>
public sealed class SandboxLimitExceededException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public SandboxLimitExceededException(SandboxStopReason reason, string message) : base(message) => Reason = reason;

    /// <summary>Creates the exception; prefer the reasoned constructor.</summary>
    public SandboxLimitExceededException() : this(SandboxStopReason.WallClock, "The sandbox limit was exceeded.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public SandboxLimitExceededException(string message) : this(SandboxStopReason.WallClock, message)
    {
    }

    /// <summary>Creates the exception with a message and its cause.</summary>
    public SandboxLimitExceededException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>Which limit was passed.</summary>
    public SandboxStopReason Reason { get; }
}

/// <summary>
/// One agent run's sandbox (ADR-2636 §4.4). While it is the <see cref="AgentSandbox.Current"/> scope, the only network
/// a run reaches is its routed model provider's endpoint, and its model calls count against its limits.
/// </summary>
public sealed class AgentSandboxScope : IDisposable
{
    private readonly CancellationTokenSource _deadline;
    private int _modelCalls;
    private long _tokens;
    private int _egressRefusals;

    internal AgentSandboxScope(TenantId tenant, Guid runId, SandboxLimits limits)
    {
        Tenant = tenant;
        RunId = runId;
        Limits = limits;
        _deadline = new CancellationTokenSource(limits.WallClock);
    }

    /// <summary>The tenant the run acts in.</summary>
    public TenantId Tenant { get; }

    /// <summary>The run's id in the interaction ledger.</summary>
    public Guid RunId { get; }

    /// <summary>The run's bounds.</summary>
    public SandboxLimits Limits { get; }

    /// <summary>Cancelled when the run's wall-clock time is up.</summary>
    public CancellationToken Deadline => _deadline.Token;

    /// <summary>Model calls made so far.</summary>
    public int ModelCalls => Volatile.Read(ref _modelCalls);

    /// <summary>Tokens used so far.</summary>
    public long Tokens => Interlocked.Read(ref _tokens);

    /// <summary>Requests the sandbox refused; any is a finding on the run.</summary>
    public int EgressRefusals => Volatile.Read(ref _egressRefusals);

    /// <summary>Counts a model call, refusing it when a limit is already reached.</summary>
    internal void BeginModelCall()
    {
        if (_deadline.IsCancellationRequested)
        {
            throw new SandboxLimitExceededException(SandboxStopReason.WallClock, $"The run passed its {Limits.WallClock} wall-clock limit.");
        }

        if (Tokens >= Limits.MaxTotalTokens)
        {
            throw new SandboxLimitExceededException(SandboxStopReason.Tokens, $"The run used its {Limits.MaxTotalTokens} tokens.");
        }

        if (Interlocked.Increment(ref _modelCalls) > Limits.MaxModelCalls)
        {
            throw new SandboxLimitExceededException(SandboxStopReason.ModelCalls, $"The run used its {Limits.MaxModelCalls} model calls.");
        }
    }

    internal void AddTokens(long tokens) => Interlocked.Add(ref _tokens, Math.Max(0, tokens));

    internal void RecordEgressRefusal() => Interlocked.Increment(ref _egressRefusals);

    /// <summary>Leaves the sandbox.</summary>
    public void Dispose()
    {
        if (AgentSandbox.Current == this)
        {
            AgentSandbox.Leave();
        }

        _deadline.Dispose();
    }
}

/// <summary>Enters and reads the ambient agent sandbox, which flows with the run's async calls.</summary>
public static class AgentSandbox
{
    private static readonly AsyncLocal<AgentSandboxScope?> Ambient = new();

    /// <summary>The sandbox the current code runs in, or null outside an agent run.</summary>
    public static AgentSandboxScope? Current => Ambient.Value;

    /// <summary>Starts a run's sandbox. Runs do not nest: entering inside another run's sandbox throws.</summary>
    public static AgentSandboxScope Enter(TenantId tenant, Guid runId, SandboxLimits? limits = null)
    {
        if (Ambient.Value is not null)
        {
            throw new InvalidOperationException("An agent run is already sandboxed here; runs do not nest.");
        }

        var scope = new AgentSandboxScope(tenant, runId, limits ?? SandboxLimits.Default);
        Ambient.Value = scope;
        return scope;
    }

    internal static void Leave() => Ambient.Value = null;
}
