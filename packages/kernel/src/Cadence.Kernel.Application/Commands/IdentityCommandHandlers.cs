using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Modules;

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

/// <summary>Handles <see cref="RegisterPerson"/>: creates a pseudonymous <see cref="Person"/> record (K-ID-4).</summary>
internal sealed class RegisterPersonHandler : ICommandHandler<RegisterPerson, PersonId>
{
    public async Task<CommandResult<PersonId>> HandleAsync(RegisterPerson command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (command.PersonId.IsEmpty)
        {
            return IdentityErrors.PersonRequired;
        }

        if (!await context.Work.ExistsAsync<Tenant>(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return IdentityErrors.TenantUnknown;
        }

        if (await context.Work.ExistsAsync<Person>(command.PersonId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        context.Work.Add(new Person(command.PersonId, context.Actor.Id, command.ReasonForChange));
        context.Work.Raise(new PersonRegistered(command.TenantId, command.PersonId));
        return CommandResult<PersonId>.Success(command.PersonId);
    }
}

/// <summary>
/// Handles <see cref="RegisterTenantMembership"/>: enrolls a pseudonymous <see cref="Person"/> in a tenant (K-ID-4).
/// Note that under forced RLS on <c>kernel.people</c>, a person is visible to a tenant only once its
/// <c>tenant_memberships</c> row exists, so the database foreign key validates <c>person_id</c> on commit.
/// </summary>
internal sealed class RegisterTenantMembershipHandler : ICommandHandler<RegisterTenantMembership, TenantMembershipId>
{
    public async Task<CommandResult<TenantMembershipId>> HandleAsync(
        RegisterTenantMembership command,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        if (command.TenantMembershipId.IsEmpty)
        {
            return IdentityErrors.MembershipRequired;
        }

        if (command.PersonId.IsEmpty)
        {
            return IdentityErrors.PersonRequired;
        }

        if (!await context.Work.ExistsAsync<Tenant>(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return IdentityErrors.TenantUnknown;
        }

        if (await context.Work.ExistsAsync<TenantMembership>(command.TenantMembershipId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var existing = await context.Work
            .ReadAllAsync<TenantMembership>(m => m.TenantId == command.TenantId && m.PersonId == command.PersonId, cancellationToken)
            .ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return KernelErrors.AlreadyExists;
        }

        context.Work.Add(new TenantMembership(
            command.TenantMembershipId,
            command.TenantId,
            command.PersonId,
            context.Actor.Id,
            command.ReasonForChange));
        context.Work.Raise(new TenantMembershipRegistered(command.TenantId, command.TenantMembershipId, command.PersonId));
        return CommandResult<TenantMembershipId>.Success(command.TenantMembershipId);
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
