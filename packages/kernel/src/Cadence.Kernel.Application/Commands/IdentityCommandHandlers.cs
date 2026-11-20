using System;
using System.Security.Cryptography;
using System.Text;
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

/// <summary>Handles <see cref="RegisterTenantMember"/>.</summary>
internal sealed class RegisterTenantMemberHandler : ICommandHandler<RegisterTenantMember, PersonId>
{
    public async Task<CommandResult<PersonId>> HandleAsync(RegisterTenantMember command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (command.PersonId == default)
        {
            return IdentityErrors.PersonRequired;
        }

        if (!await context.Work.ExistsAsync<Tenant>(command.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return IdentityErrors.TenantUnknown;
        }

        var existing = await context.Work.ReadAllAsync<TenantMembership>(
            m => m.TenantId == command.TenantId && m.PersonId == command.PersonId,
            cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return CommandResult<PersonId>.Success(command.PersonId);
        }

        var membershipId = DeterministicMembershipId(command.TenantId, command.PersonId);
        context.Work.Add(new TenantMembership(membershipId, command.TenantId, command.PersonId, context.Actor.Id, command.ReasonForChange));
        context.Work.Raise(new TenantMemberRegistered(command.TenantId, command.PersonId));
        return CommandResult<PersonId>.Success(command.PersonId);
    }

    private static TenantMembershipId DeterministicMembershipId(TenantId tenantId, PersonId personId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"tenant-membership:{tenantId.Value:D}:{personId.Value:D}"));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return TenantMembershipId.FromGuid(new Guid(guidBytes));
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
