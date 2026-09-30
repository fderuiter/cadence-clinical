using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Trust;

/// <summary>Handles <see cref="DefineRole"/>.</summary>
internal sealed class DefineRoleHandler : ICommandHandler<DefineRole, RoleState>
{
    public async Task<CommandResult<RoleState>> HandleAsync(DefineRole command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<Role>(command.RoleId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var defined = Role.Define(command.RoleId, command.TenantId, command.Code, command.Name, command.Capabilities);
        if (defined.Value is not { } role)
        {
            return defined.Error!;
        }

        if ((await context.Work.ReadAllAsync<Role>(r => r.Code == role.Code, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return AuthorityErrors.RoleCodeTaken;
        }

        context.Work.Add(role);
        context.Work.Raise(new RoleDefined(role.TenantId, role.Id, role.Code, role.Capabilities));
        return CommandResult<RoleState>.Success(new RoleState(role.Id, role.Code, role.Name, role.Capabilities));
    }
}

/// <summary>Handles <see cref="IssueGrant"/>.</summary>
internal sealed class IssueGrantHandler : ICommandHandler<IssueGrant, GrantState>
{
    public async Task<CommandResult<GrantState>> HandleAsync(IssueGrant command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<Grant>(command.GrantId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        var issued = Grant.Issue(
            command.GrantId, command.TenantId, command.Grantee, command.Capability, command.RoleId, command.Scope,
            command.ValidFrom, command.ValidTo, command.WithGrantOption, parentGrantId: null, context.Actor, context.RecordedAt);
        if (issued.Value is not { } grant)
        {
            return issued.Error!;
        }

        if (await AuthorityChecks.TargetProblemAsync(grant, context, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        context.Work.Add(grant);
        var state = grant.State();
        context.Work.Raise(new GrantIssued(
            grant.TenantId, grant.Id, grant.Grantee.ToString(), grant.Capability, grant.RoleId, grant.Scope, state.ValidFrom!.Value, state.ValidTo, grant.WithGrantOption));
        return CommandResult<GrantState>.Success(state);
    }
}

/// <summary>
/// Handles <see cref="DelegateGrant"/>: the actor holds the parent, in effect now and with the grant option, and the
/// delegation's capabilities, scope and validity lie within it.
/// </summary>
internal sealed class DelegateGrantHandler : ICommandHandler<DelegateGrant, GrantState>
{
    public async Task<CommandResult<GrantState>> HandleAsync(DelegateGrant command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<Grant>(command.GrantId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (await context.Work.ReadAsync<Grant>(command.ParentGrantId, cancellationToken).ConfigureAwait(false) is not { } parent)
        {
            return KernelErrors.NotFound;
        }

        if (parent.Grantee != context.Actor || !parent.IsInEffect(context.RecordedAt, context.RecordedAt))
        {
            return AuthorityErrors.ParentInactive;
        }

        if (!parent.WithGrantOption)
        {
            return AuthorityErrors.GrantOptionRequired;
        }

        if (command.Grantee == context.Actor)
        {
            return AuthorityErrors.SelfDelegation;
        }

        var issued = Grant.Issue(
            command.GrantId, command.TenantId, command.Grantee, command.Capability, command.RoleId, command.Scope,
            command.ValidFrom, command.ValidTo, command.WithGrantOption, parent.Id, context.Actor, context.RecordedAt);
        if (issued.Value is not { } grant)
        {
            return issued.Error!;
        }

        if (await AuthorityChecks.TargetProblemAsync(grant, context, cancellationToken).ConfigureAwait(false) is { } problem)
        {
            return problem;
        }

        if (await AuthorityChecks.ExceedsAsync(grant, parent, context, cancellationToken).ConfigureAwait(false) is { } exceeds)
        {
            return AuthorityErrors.DelegationExceedsParent.WithMessage(exceeds);
        }

        context.Work.Add(grant);
        var state = grant.State();
        context.Work.Raise(new DelegationSigned(
            grant.TenantId, grant.Id, parent.Id, grant.Grantee.ToString(), grant.Capability, grant.RoleId, grant.Scope,
            state.ValidFrom!.Value, state.ValidTo, grant.WithGrantOption));
        return CommandResult<GrantState>.Success(state);
    }
}

/// <summary>Handles <see cref="RevokeGrant"/>.</summary>
internal sealed class RevokeGrantHandler : ICommandHandler<RevokeGrant, GrantState>
{
    public async Task<CommandResult<GrantState>> HandleAsync(RevokeGrant command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<Grant>(command.GrantId, cancellationToken).ConfigureAwait(false) is not { } grant)
        {
            return KernelErrors.NotFound;
        }

        if (grant.Revoke(command.EffectiveAt, context.RecordedAt) is { } error)
        {
            return error;
        }

        context.Work.Raise(new GrantRevoked(grant.TenantId, grant.Id, command.EffectiveAt));
        return CommandResult<GrantState>.Success(grant.State());
    }
}

/// <summary>Checks shared by the grant handlers, read in the command's transaction.</summary>
internal static class AuthorityChecks
{
    /// <summary>The role or scope object the grant names does not exist in the tenant.</summary>
    public static async Task<KernelError?> TargetProblemAsync(Grant grant, ICommandContext context, CancellationToken cancellationToken)
    {
        if (grant.RoleId is { } roleId && !await context.Work.ExistsAsync<Role>(roleId, cancellationToken).ConfigureAwait(false))
        {
            return AuthorityErrors.RoleUnknown;
        }

        return await PathOfAsync(grant.Scope, context, cancellationToken).ConfigureAwait(false) is null ? AuthorityErrors.ScopeUnknown : null;
    }

    /// <summary>Why the delegation does not lie within its parent, or null when it does.</summary>
    public static async Task<string?> ExceedsAsync(Grant delegation, Grant parent, ICommandContext context, CancellationToken cancellationToken)
    {
        var granted = await CapabilitiesAsync(delegation, context, cancellationToken).ConfigureAwait(false);
        var held = await CapabilitiesAsync(parent, context, cancellationToken).ConfigureAwait(false);
        if (!granted.All(held.Contains))
        {
            return "It grants capabilities the delegating grant does not hold.";
        }

        if (await PathOfAsync(delegation.Scope, context, cancellationToken).ConfigureAwait(false) is not { } path || !parent.Scope.Covers(path))
        {
            return "Its scope is not within the delegating grant's scope.";
        }

        var within = parent.CurrentValidity is { } bound
            && delegation.CurrentValidity is { } validity
            && validity.Start >= bound.Start
            && (bound.End is null || (validity.End is { } end && end <= bound.End.Value));
        return within ? null : "Its valid period is not within the delegating grant's.";
    }

    private static async Task<IReadOnlySet<string>> CapabilitiesAsync(Grant grant, ICommandContext context, CancellationToken cancellationToken)
    {
        if (grant.Capability is { } capability)
        {
            return new HashSet<string>([capability], StringComparer.Ordinal);
        }

        var role = await context.Work.ReadAsync<Role>(grant.RoleId!.Value, cancellationToken).ConfigureAwait(false);
        return new HashSet<string>(role?.Capabilities ?? [], StringComparer.Ordinal);
    }

    /// <summary>The scope's position on the scope path, or null when the object it names does not exist.</summary>
    public static async Task<ScopePath?> PathOfAsync(AuthorityScope scope, ICommandContext context, CancellationToken cancellationToken)
    {
        switch (scope.Level)
        {
            case ScopeLevel.Tenant:
                return ScopePath.TenantWide;
            case ScopeLevel.Organization:
                var organization = OrganizationId.FromGuid(scope.TargetId!.Value);
                return await context.Work.ExistsAsync<Organization>(organization, cancellationToken).ConfigureAwait(false)
                    ? new ScopePath(organization)
                    : null;
            case ScopeLevel.Study or ScopeLevel.StudyCountry:
                var study = await context.Work.ReadAsync<Study>(StudyId.FromGuid(scope.TargetId!.Value), cancellationToken).ConfigureAwait(false);
                return study is null ? null : new ScopePath(study.SponsorId, study.Id, scope.Country);
            case ScopeLevel.StudySite:
                var studySite = await context.Work.ReadAsync<StudySite>(StudySiteId.FromGuid(scope.TargetId!.Value), cancellationToken).ConfigureAwait(false);
                if (studySite is null)
                {
                    return null;
                }

                var parentStudy = await context.Work.ReadAsync<Study>(studySite.StudyId, cancellationToken).ConfigureAwait(false);
                var site = await context.Work.ReadAsync<Site>(studySite.SiteId, cancellationToken).ConfigureAwait(false);
                return new ScopePath(parentStudy?.SponsorId, studySite.StudyId, site?.Country, studySite.Id);
            default:
                return null;
        }
    }
}

/// <summary>Registers the authority command handlers (spec §9.3).</summary>
public static class AuthorityServiceCollectionExtensions
{
    /// <summary>Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>.</summary>
    public static IServiceCollection AddKernelAuthorityHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<DefineRole, RoleState>, DefineRoleHandler>();
        services.AddTransient<ICommandHandler<IssueGrant, GrantState>, IssueGrantHandler>();
        services.AddTransient<ICommandHandler<DelegateGrant, GrantState>, DelegateGrantHandler>();
        services.AddTransient<ICommandHandler<RevokeGrant, GrantState>, RevokeGrantHandler>();
        services.AddKernelQualificationHandlers();
        return services;
    }
}
