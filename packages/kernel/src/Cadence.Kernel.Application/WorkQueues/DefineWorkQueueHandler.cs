using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Modules;

namespace Cadence.Kernel.Application.WorkQueues;

/// <summary>
/// Handles <see cref="DefineWorkQueue"/>: the source is registered, a kernel command's capability keeps the verb class
/// the command declares, and the new version follows the code's latest.
/// </summary>
internal sealed class DefineWorkQueueHandler(IEnumerable<IWorkItemSource> sources) : ICommandHandler<DefineWorkQueue, WorkQueueDefinitionState>
{
    public async Task<CommandResult<WorkQueueDefinitionState>> HandleAsync(
        DefineWorkQueue command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<WorkQueueDefinition>(command.WorkQueueDefinitionId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (!sources.Any(s => string.Equals(s.Name, command.Source, StringComparison.Ordinal)))
        {
            return WorkQueueErrors.SourceUnknown.WithMessage($"The work-item source '{command.Source}' is not registered.");
        }

        if (KernelCommandCatalog.Kernel.Entries.FirstOrDefault(e => e.Capability.Token == command.Capability) is { } declared
            && declared.Capability.VerbClass != command.VerbClass)
        {
            return WorkQueueErrors.CapabilityInvalid.WithMessage(
                $"{command.Capability} is a {declared.Capability.VerbClass} capability ({declared.Command}), not {command.VerbClass}.");
        }

        var versions = await context.Work
            .ReadAllAsync<WorkQueueDefinition>(d => d.Code == command.Code, cancellationToken)
            .ConfigureAwait(false);
        var version = versions.Count == 0 ? 1 : versions.Max(d => d.Version) + 1;
        var defined = WorkQueueDefinition.Define(
            command.WorkQueueDefinitionId, command.TenantId, command.Code, version, command.Title, command.Capability, command.VerbClass, command.Source);
        if (defined.Value is not { } definition)
        {
            return defined.Error!;
        }

        context.Work.Add(definition);
        context.Work.Raise(new WorkQueueDefined(
            definition.TenantId, definition.Id, definition.Code, definition.Version, definition.Capability, definition.Source));
        return CommandResult<WorkQueueDefinitionState>.Success(new WorkQueueDefinitionState(
            definition.Id, definition.Code, definition.Version, definition.Title, definition.Capability, definition.VerbClass, definition.Source));
    }
}
