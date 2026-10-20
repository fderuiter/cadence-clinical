using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Modules.Intelligence.Domain;

using FluentValidation;

namespace Cadence.Modules.Intelligence.Application;

internal sealed class StartAgentRunHandler : ICommandHandler<StartAgentRun, System.Guid>
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
