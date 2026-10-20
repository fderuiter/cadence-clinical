using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Trust;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Evaluation;
using Cadence.Modules.Intelligence.Evaluation.Runs;

using FluentValidation;

namespace Cadence.Modules.Intelligence.Application;

internal sealed class StartAgentRunHandler(ITaskConfigurationCatalog catalog) : ICommandHandler<StartAgentRun, System.Guid>
{
    public async Task<CommandResult<System.Guid>> HandleAsync(StartAgentRun command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<AgentRun>(command.RunId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        // The kill switch is read again inside the command, so a run started after the switch commits is refused even
        // if the runtime's earlier check passed.
        foreach (var scope in new[] { AgentControl.AllTasks, command.Configuration.Key })
        {
            var control = await context.Work.ReadAsync<AgentControl>(AgentControl.IdFor(context.TenantId, scope), cancellationToken).ConfigureAwait(false);
            if (control is { Enabled: false })
            {
                return IntelligenceErrors.AgentDisabled;
            }
        }

        // The configuration must be the one the deployment ships, and qualified in the tenant on its current content
        // (ADR-2631); read inside the command, so a suspension that commits first refuses the run.
        if (catalog.Find(command.Configuration.Key, command.Configuration.Version) is not { } shipped)
        {
            return IntelligenceErrors.TaskUnknown;
        }

        if (shipped.ContentHash != command.Configuration.ContentHash)
        {
            return IntelligenceErrors.ConfigurationChanged;
        }

        var qualification = await context.Work.ReadAsync<TaskQualification>(
            TaskQualification.IdFor(context.TenantId, shipped.Key, shipped.Version), cancellationToken).ConfigureAwait(false);
        if (TaskQualification.WhyNotUsable(qualification, shipped) is { } why)
        {
            return IntelligenceErrors.ConfigurationUnqualified.WithMessage(why);
        }

        context.Work.Add(AgentRun.Start(
            command.RunId, context.TenantId, command.Configuration, command.Model, context.Actor.Id, command.RequestJson, context.RecordedAt));
        return CommandResult<System.Guid>.Success(command.RunId);
    }
}

internal sealed class CompleteAgentRunHandler : ICommandHandler<CompleteAgentRun, AgentRunStatus>
{
    public async Task<CommandResult<AgentRunStatus>> HandleAsync(CompleteAgentRun command, ICommandContext context, CancellationToken cancellationToken)
    {
        var run = await context.Work.FindAsync<AgentRun>(command.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return KernelErrors.NotFound;
        }

        if (run.RequestedBy != context.Actor.Id)
        {
            return IntelligenceErrors.RunNotYours;
        }

        if (run.Status != AgentRunStatus.Started)
        {
            return IntelligenceErrors.RunCompleted;
        }

        if (command.Succeeded)
        {
            run.Succeed(command.ResponseText!, command.InputTokens, command.OutputTokens, command.CostUsd, context.RecordedAt);
        }
        else
        {
            run.Fail(command.FailureReason!, command.InputTokens, command.OutputTokens, command.CostUsd, context.RecordedAt);
        }

        context.Work.Raise(new AgentRunCompleted(run.Id, run.TaskKey, run.TaskVersion, run.Status, run.InputTokens, run.OutputTokens));
        return CommandResult<AgentRunStatus>.Success(run.Status);
    }
}

internal sealed class SetAgentControlHandler : ICommandHandler<SetAgentControl, AgentControlState>
{
    public async Task<CommandResult<AgentControlState>> HandleAsync(SetAgentControl command, ICommandContext context, CancellationToken cancellationToken)
    {
        var id = AgentControl.IdFor(context.TenantId, command.Scope);
        var control = await context.Work.FindAsync<AgentControl>(id, cancellationToken).ConfigureAwait(false);
        var version = 1L;
        if (control is null)
        {
            control = AgentControl.For(context.TenantId, command.Scope);
            context.Work.Add(control);
        }
        else
        {
            version = control.VersionIndex + 1;
        }

        control.Set(command.Enabled, command.MonthlyTokenLimit);
        context.Work.Raise(new AgentControlChanged(command.Scope, command.Enabled, command.MonthlyTokenLimit));
        return CommandResult<AgentControlState>.Success(new AgentControlState(command.Scope, command.Enabled, command.MonthlyTokenLimit, version));
    }
}

internal sealed class QualifyTaskConfigurationHandler(ITaskConfigurationCatalog catalog, IAttestationVerifier verifier)
    : ICommandHandler<QualifyTaskConfiguration, TaskQualificationState>
{
    public async Task<CommandResult<TaskQualificationState>> HandleAsync(
        QualifyTaskConfiguration command, ICommandContext context, CancellationToken cancellationToken)
    {
        var named = command.Configuration;
        if (catalog.Find(named.Key, named.Version) is not { } configuration)
        {
            return IntelligenceErrors.TaskUnknown;
        }

        if (configuration.ContentHash != named.ContentHash)
        {
            return IntelligenceErrors.ConfigurationChanged;
        }

        if (configuration.QualificationGaps() is { Length: > 0 } gaps)
        {
            return IntelligenceErrors.ConfigurationNotQualifiable.WithMessage(string.Join(" ", gaps));
        }

        // The evidence is the stored run, never the caller's word that a run passed (#5574).
        var run = await context.Work.ReadAsync<EvaluationRun>(EvaluationRun.IdFor(context.TenantId, command.EvaluationRunId), cancellationToken)
            .ConfigureAwait(false);
        if (run is null)
        {
            return IntelligenceErrors.EvaluationRunNotFound.WithMessage($"No evaluation run {command.EvaluationRunId} is stored here. Record the run before qualifying on it.");
        }

        if (run.TaskKey != named.Key || run.TaskVersion != named.Version || run.ConfigurationHash != configuration.ContentHash)
        {
            return IntelligenceErrors.EvaluationNotAccepted.WithMessage(
                $"Evaluation run {run.RunId} evaluated {run.TaskKey} v{run.TaskVersion} ({run.ConfigurationHash}), not {named.Key} v{named.Version} ({configuration.ContentHash}).");
        }

        if (run.EvaluationSetHash != configuration.EvaluationSet!.ContentHash)
        {
            return IntelligenceErrors.EvaluationNotAccepted.WithMessage(
                $"Evaluation run {run.RunId} used set {run.EvaluationSetHash}, not the pinned {configuration.EvaluationSet.SetId} v{configuration.EvaluationSet.Version} ({configuration.EvaluationSet.ContentHash}).");
        }

        if (!run.Accepted)
        {
            return IntelligenceErrors.EvaluationNotAccepted.WithMessage(
                $"Evaluation run {run.RunId} did not meet the acceptance criteria.");
        }

        var evidence = run.Evidence();
        var target = new AttestationTarget(TaskConfigurationAttestationCheck.TargetKind, TaskConfiguration.AttestationTargetId(named.Key, named.Version), named.ContentHash);
        var verification = await verifier.VerifyAsync(
            new AttestationRequirement(context.TenantId, command.AttestationId, AttestationMeaning.Approved, target, context.Actor), cancellationToken)
            .ConfigureAwait(false);
        if (!verification.Satisfied)
        {
            return IntelligenceErrors.AttestationRejected.WithMessage(verification.Reason ?? IntelligenceErrors.AttestationRejected.Message);
        }

        var id = TaskQualification.IdFor(context.TenantId, named.Key, named.Version);
        var qualification = await context.Work.FindAsync<TaskQualification>(id, cancellationToken).ConfigureAwait(false);
        var version = 1L;
        if (qualification is null)
        {
            qualification = TaskQualification.Qualify(
                context.TenantId, configuration, evidence, command.AttestationId.Value, context.Actor.Id, context.RecordedAt);
            context.Work.Add(qualification);
        }
        else if (qualification.ConfigurationHash != configuration.ContentHash)
        {
            return IntelligenceErrors.ConfigurationChanged.WithMessage(
                $"{named.Key} v{named.Version} was qualified on other content ({qualification.ConfigurationHash}). Publish the change as a new version.");
        }
        else if (qualification.Status == QualificationStatus.Qualified)
        {
            return IntelligenceErrors.AlreadyQualified;
        }
        else
        {
            qualification.Requalify(configuration, evidence, command.AttestationId.Value, context.Actor.Id, context.RecordedAt);
            version = qualification.VersionIndex + 1;
        }

        context.Work.Raise(new TaskConfigurationQualified(
            named.Key, named.Version, named.ContentHash, qualification.RiskClass, evidence.RunReference, command.AttestationId.Value));
        return CommandResult<TaskQualificationState>.Success(new TaskQualificationState(
            named.Key, named.Version, named.ContentHash, qualification.Status, qualification.RiskClass, version));
    }
}

internal sealed class RecordEvaluationRunHandler(ITaskConfigurationCatalog catalog) : ICommandHandler<RecordEvaluationRun, EvaluationRunState>
{
    public async Task<CommandResult<EvaluationRunState>> HandleAsync(RecordEvaluationRun command, ICommandContext context, CancellationToken cancellationToken)
    {
        EvaluationRunRecord record;
        try
        {
            record = EvaluationRunRecord.FromJson(command.RecordJson);
        }
        catch (JsonException ex)
        {
            return IntelligenceErrors.EvaluationRunInvalid.WithMessage($"The text is not an evaluation run record: {ex.Message}");
        }

        var named = record.Configuration;
        if (catalog.Find(named.Key, named.Version) is not { } configuration)
        {
            return IntelligenceErrors.TaskUnknown;
        }

        if (configuration.ContentHash != named.ContentHash)
        {
            return IntelligenceErrors.ConfigurationChanged;
        }

        if (configuration.QualificationGaps() is { Length: > 0 } gaps)
        {
            return IntelligenceErrors.ConfigurationNotQualifiable.WithMessage(string.Join(" ", gaps));
        }

        if (EvaluationRun.Problems(configuration, record) is { Count: > 0 } problems)
        {
            return IntelligenceErrors.EvaluationRunInvalid.WithMessage(string.Join(" ", problems));
        }

        var id = EvaluationRun.IdFor(context.TenantId, record.RunId);
        if (await context.Work.ExistsAsync<EvaluationRun>(id, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var run = EvaluationRun.Record(context.TenantId, record);
        context.Work.Add(run);
        context.Work.Raise(new EvaluationRunRecorded(run.RunId, run.TaskKey, run.TaskVersion, run.ConfigurationHash, run.RecordHash, run.Accepted));
        return CommandResult<EvaluationRunState>.Success(new EvaluationRunState(
            run.RunId, named, run.EvaluationSetHash, run.RecordHash, run.Accepted, record.Verdict.Reasons));
    }
}

internal sealed class SuspendTaskConfigurationHandler : ICommandHandler<SuspendTaskConfiguration, TaskQualificationState>
{
    public async Task<CommandResult<TaskQualificationState>> HandleAsync(
        SuspendTaskConfiguration command, ICommandContext context, CancellationToken cancellationToken)
    {
        var id = TaskQualification.IdFor(context.TenantId, command.TaskKey, command.TaskVersion);
        if (await context.Work.FindAsync<TaskQualification>(id, cancellationToken).ConfigureAwait(false) is not { } qualification)
        {
            return IntelligenceErrors.NotQualified;
        }

        var version = qualification.VersionIndex;
        if (qualification.Status != QualificationStatus.Suspended)
        {
            qualification.Suspend(command.ReasonForChange, context.RecordedAt);
            context.Work.Raise(new TaskConfigurationSuspended(command.TaskKey, command.TaskVersion, command.ReasonForChange));
            version++;
        }

        return CommandResult<TaskQualificationState>.Success(new TaskQualificationState(
            qualification.TaskKey, qualification.TaskVersion, qualification.ConfigurationHash, qualification.Status, qualification.RiskClass, version));
    }
}

/// <summary>
/// Lets the kernel's <c>Attest</c> sign a task configuration version (ADR-2631): the target names a version this
/// deployment ships, by its derived id, and the content hash it signs is that version's current hash.
/// </summary>
internal sealed class TaskConfigurationAttestationCheck(ITaskConfigurationCatalog catalog) : IAttestationTargetCheck
{
    /// <summary>The attestation target kind of a task configuration version.</summary>
    public const string TargetKind = "task_configuration";

    public string Kind => TargetKind;

    public Task<KernelError?> CheckAsync(AttestationTarget target, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var configuration = catalog.All.FirstOrDefault(c => TaskConfiguration.AttestationTargetId(c.Key, c.Version) == target.Id);
        KernelError? error = configuration switch
        {
            null => TrustErrors.TargetUnknown,
            _ when configuration.ContentHash != target.ContentHash => TrustErrors.TargetContentChanged,
            _ => null,
        };
        return Task.FromResult(error);
    }
}

internal sealed class StartAgentRunValidator : AbstractValidator<StartAgentRun>
{
    public StartAgentRunValidator()
    {
        RuleFor(c => c.RunId).NotEmpty();
        RuleFor(c => c.Configuration).NotNull();
        RuleFor(c => c.Configuration)
            .Must(c => c.Problems().Length == 0)
            .When(c => c.Configuration is not null)
            .WithMessage(c => string.Join(" ", c.Configuration.Problems()));
        RuleFor(c => c.Model).NotEmpty().MaximumLength(200);
        RuleFor(c => c.RequestJson).NotEmpty();
    }
}

internal sealed class CompleteAgentRunValidator : AbstractValidator<CompleteAgentRun>
{
    public CompleteAgentRunValidator()
    {
        RuleFor(c => c.RunId).NotEmpty();
        RuleFor(c => c.ResponseText).NotNull().When(c => c.Succeeded).WithMessage("A successful run carries the model's answer.");
        RuleFor(c => c.FailureReason).NotEmpty().When(c => !c.Succeeded).WithMessage("A failed run says why it failed.");
        RuleFor(c => c.FailureReason).MaximumLength(2000);
        RuleFor(c => c.InputTokens).GreaterThanOrEqualTo(0);
        RuleFor(c => c.OutputTokens).GreaterThanOrEqualTo(0);
        RuleFor(c => c.CostUsd).GreaterThanOrEqualTo(0).When(c => c.CostUsd is not null);
    }
}

internal sealed class SetAgentControlValidator : AbstractValidator<SetAgentControl>
{
    public SetAgentControlValidator()
    {
        RuleFor(c => c.Scope)
            .Must(s => s == AgentControl.AllTasks || TaskConfiguration.KeyPattern().IsMatch(s ?? string.Empty))
            .WithMessage("Name '*' for every task, or one task key.");
        RuleFor(c => c.MonthlyTokenLimit).GreaterThanOrEqualTo(0).When(c => c.MonthlyTokenLimit is not null);
    }
}

internal sealed class QualifyTaskConfigurationValidator : AbstractValidator<QualifyTaskConfiguration>
{
    public QualifyTaskConfigurationValidator()
    {
        RuleFor(c => c.Configuration).NotNull();
        RuleFor(c => c.Configuration.Key).Must(k => TaskConfiguration.KeyPattern().IsMatch(k ?? string.Empty)).When(c => c.Configuration is not null)
            .WithMessage("Name a task key.");
        RuleFor(c => c.Configuration.Version).GreaterThanOrEqualTo(1).When(c => c.Configuration is not null);
        RuleFor(c => c.Configuration.ContentHash).Length(ContentHash.Length).When(c => c.Configuration is not null);
        RuleFor(c => c.EvaluationRunId).NotEmpty().WithMessage("Name the stored evaluation run the qualification rests on.");
        RuleFor(c => c.AttestationId.Value).NotEmpty().WithMessage("Sign the qualification: name the attestation.");
    }
}

internal sealed class RecordEvaluationRunValidator : AbstractValidator<RecordEvaluationRun>
{
    /// <summary>The largest record accepted, in characters; a set's run record is far smaller.</summary>
    internal const int MaximumRecordLength = 8_000_000;

    public RecordEvaluationRunValidator()
    {
        RuleFor(c => c.RecordJson).NotEmpty().MaximumLength(MaximumRecordLength);
    }
}

internal sealed class SuspendTaskConfigurationValidator : AbstractValidator<SuspendTaskConfiguration>
{
    public SuspendTaskConfigurationValidator()
    {
        RuleFor(c => c.TaskKey).Must(k => TaskConfiguration.KeyPattern().IsMatch(k ?? string.Empty)).WithMessage("Name a task key.");
        RuleFor(c => c.TaskVersion).GreaterThanOrEqualTo(1);
        RuleFor(c => c.ReasonForChange).MaximumLength(1000);
    }
}
