using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;

namespace Cadence.Kernel.Application.Commands;

/// <summary>Handles <see cref="ProvisionTenant"/>.</summary>
internal sealed class ProvisionTenantHandler : ICommandHandler<ProvisionTenant, TenantId>
{
    public async Task<CommandResult<TenantId>> HandleAsync(ProvisionTenant command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (IdentityValidation.Check(command.Code, command.Name) is { } invalid)
        {
            return invalid;
        }

        if (command.Residency is { } residency && !IdentityValidation.IsResidency(residency))
        {
            return IdentityErrors.ResidencyInvalid;
        }

        if (await context.Work.ExistsAsync<Tenant>(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        context.Work.Add(new Tenant(command.TenantId, command.Code, command.Name, context.Actor.Id, command.ReasonForChange, command.Residency));
        context.Work.Raise(new TenantProvisioned(command.TenantId, command.Code, command.Name));
        return CommandResult<TenantId>.Success(command.TenantId);
    }
}

/// <summary>Handles <see cref="RegisterOrganization"/>.</summary>
internal sealed class RegisterOrganizationHandler : ICommandHandler<RegisterOrganization, OrganizationId>
{
    public async Task<CommandResult<OrganizationId>> HandleAsync(RegisterOrganization command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (IdentityValidation.Check(command.Code, command.Name) is { } invalid)
        {
            return invalid;
        }

        if (!await context.Work.ExistsAsync<Tenant>(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return IdentityErrors.TenantUnknown;
        }

        if (await context.Work.ExistsAsync<Organization>(command.OrganizationId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        context.Work.Add(new Organization(
            command.OrganizationId, command.TenantId, command.Code, command.Name, command.Kind, context.Actor.Id, command.ReasonForChange));
        context.Work.Raise(new OrganizationRegistered(command.TenantId, command.OrganizationId, command.Code, command.Name, command.Kind));
        return CommandResult<OrganizationId>.Success(command.OrganizationId);
    }
}

internal static partial class IdentityValidation
{
    public static bool IsResidency(string residency) => ResidencyPattern().IsMatch(residency);

    [GeneratedRegex("^[A-Z]{2,8}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ResidencyPattern();

    public static KernelError? Check(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return IdentityErrors.CodeRequired;
        }

        return string.IsNullOrWhiteSpace(name) ? IdentityErrors.NameRequired : null;
    }
}
