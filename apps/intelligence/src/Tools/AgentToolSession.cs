using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Time;

using Microsoft.Extensions.AI;

namespace Cadence.Modules.Intelligence.Tools;

/// <summary>
/// One run's view of the tools (ADR-2633 §4.4): the allowed tools only, one principal, one <see cref="RunReadSet"/>
/// and a log of every call, refused ones included. Every result comes back wrapped as untrusted data.
/// </summary>
public sealed class AgentToolSession
{
    private readonly Dictionary<string, IAgentTool> tools;
    private readonly IClock clock;
    private readonly Lock gate = new();
    private readonly List<ToolCallRecord> calls = [];

    internal AgentToolSession(AgentPrincipal principal, IReadOnlyList<IAgentTool> allowed, IClock clock)
    {
        Principal = principal;
        Tools = allowed;
        tools = allowed.ToDictionary(t => t.Name, StringComparer.Ordinal);
        this.clock = clock;
        ReadSet = new RunReadSet();
    }

    /// <summary>Who the tools read as.</summary>
    public AgentPrincipal Principal { get; }

    /// <summary>The tools this run may call, by name.</summary>
    public IReadOnlyList<IAgentTool> Tools { get; }

    /// <summary>Everything the run has been shown.</summary>
    public RunReadSet ReadSet { get; }

    /// <summary>Every call so far, in order.</summary>
    public IReadOnlyList<ToolCallRecord> Calls
    {
        get
        {
            lock (gate)
            {
                return [.. calls];
            }
        }
    }

    /// <summary>
    /// Calls <paramref name="tool"/> with <paramref name="arguments"/> and returns the wrapped result. A tool not on the
    /// allow-list is refused and logged without running.
    /// </summary>
    public async ValueTask<JsonElement> InvokeAsync(string tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var started = clock.UtcNow;
        ToolOutcome outcome = tools.TryGetValue(tool, out var found)
            ? await found.InvokeAsync(new AgentToolContext(Principal, ReadSet), arguments, cancellationToken).ConfigureAwait(false)
            : ToolOutcome.Failure(ToolErrors.NotAllowed, $"The tool '{tool}' is not available to this task.");

        lock (gate)
        {
            calls.Add(new ToolCallRecord(tool, outcome.ErrorCode, started, clock.UtcNow));
        }

        return ToolResultEnvelope.WrapAsElement(tool, outcome);
    }

    /// <summary>The allowed tools as <see cref="AIFunction"/>s for an <c>IChatClient</c> (Microsoft.Extensions.AI).</summary>
    public IReadOnlyList<AIFunction> AsAIFunctions() => [.. Tools.Select(t => new SessionAIFunction(this, t))];
}

/// <summary>One tool call: the tool, the error code if it failed (null on success), and when it ran.</summary>
public sealed record ToolCallRecord(string Tool, string? ErrorCode, DateTimeOffset StartedAt, DateTimeOffset FinishedAt);
