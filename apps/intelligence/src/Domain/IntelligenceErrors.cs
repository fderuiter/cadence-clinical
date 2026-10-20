using Cadence.Kernel.Commands;

namespace Cadence.Modules.Intelligence.Domain;

/// <summary>The Intelligence module's registered errors (K-ERR-1, ADR-2600 §4.5).</summary>
public static class IntelligenceErrors
{
    /// <summary>A kill switch covers the task.</summary>
    public static readonly KernelError AgentDisabled =
        new("M.Intelligence.Agent.Disabled", 423, "AI agents are switched off for this task. The work can be done without them.");

    /// <summary>The month's token budget for the task is spent.</summary>
    public static readonly KernelError BudgetExhausted =
        new("M.Intelligence.Agent.BudgetExhausted", 429, "This month's AI budget for the task is spent. The work can be done without AI.");

    /// <summary>No task configuration has the key.</summary>
    public static readonly KernelError TaskUnknown =
        new("M.Intelligence.Task.Unknown", 404, "No AI task has this key.");

    /// <summary>The configuration's provider is not configured in this deployment.</summary>
    public static readonly KernelError ProviderUnavailable =
        new("M.Intelligence.Provider.Unavailable", 503, "The AI provider for this task is not configured. The work can be done without AI.");

    /// <summary>The run was completed already.</summary>
    public static readonly KernelError RunCompleted =
        new("M.Intelligence.Run.Completed", 409, "The agent run is already completed.");

    /// <summary>Only the person a run acted for may complete it.</summary>
    public static readonly KernelError RunNotYours =
        new("M.Intelligence.Run.NotYours", 403, "Only the person an agent run acts for may complete it (K-AI-6).");
}
