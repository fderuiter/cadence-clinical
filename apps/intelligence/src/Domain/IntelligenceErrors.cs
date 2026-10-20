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

    /// <summary>The configuration version is not qualified in the tenant, is suspended, or changed after it was qualified.</summary>
    public static readonly KernelError ConfigurationUnqualified =
        new("M.Intelligence.Configuration.Unqualified", 409, "This AI task configuration is not qualified for use here. The work can be done without AI.");

    /// <summary>The named configuration is not the one the deployment runs: its content hash differs.</summary>
    public static readonly KernelError ConfigurationChanged =
        new("M.Intelligence.Configuration.Changed", 409, "The task configuration changed: its content hash is not the one named. Publish the change as a new version.");

    /// <summary>The configuration lacks what qualification needs.</summary>
    public static readonly KernelError ConfigurationNotQualifiable =
        new("M.Intelligence.Configuration.NotQualifiable", 422, "The task configuration cannot be qualified yet.");

    /// <summary>The stored evaluation run did not meet the acceptance criteria, or evaluated another configuration or set.</summary>
    public static readonly KernelError EvaluationNotAccepted =
        new("M.Intelligence.Qualification.EvaluationNotAccepted", 422, "The evaluation does not support qualification.");

    /// <summary>The evaluation run record cannot be evidence for its configuration.</summary>
    public static readonly KernelError EvaluationRunInvalid =
        new("M.Intelligence.Evaluation.RunInvalid", 422, "The evaluation run record cannot be evidence for this task configuration.");

    /// <summary>No stored evaluation run has the id in the tenant.</summary>
    public static readonly KernelError EvaluationRunNotFound =
        new("M.Intelligence.Evaluation.RunNotFound", 404, "No evaluation run with this id is stored here. Record the run before qualifying on it.");

    /// <summary>The attestation does not sign this configuration version as approved by the person qualifying it.</summary>
    public static readonly KernelError AttestationRejected =
        new("M.Intelligence.Qualification.AttestationRejected", 422, "The signature does not approve this task configuration version.");

    /// <summary>The version is already qualified on this content.</summary>
    public static readonly KernelError AlreadyQualified =
        new("M.Intelligence.Qualification.AlreadyQualified", 409, "This task configuration version is already qualified.");

    /// <summary>The version has no qualification in the tenant to suspend.</summary>
    public static readonly KernelError NotQualified =
        new("M.Intelligence.Qualification.NotQualified", 404, "This task configuration version has no qualification here.");
}
