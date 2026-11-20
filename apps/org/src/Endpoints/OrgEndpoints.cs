using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Identity;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Time;
using Cadence.Modules.Org.Contracts;
using Cadence.Modules.Org.Provisioning;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Modules.Org.Endpoints;

/// <summary>
/// HTTP endpoints of the Organization Administration module (<c>/api/v1/org</c>, ADR-2610, ADR-2819):
/// tenant team directory, OIDC seat invitations, bounded email delegations, and grant revocation.
/// </summary>
public static class OrgEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>Maps <c>/api/v1/org</c> endpoints on <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapOrgEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup("/api/v1/org")
            .RequireSignedInCaller("Team directory reads are bounded by the caller's active grants; invitations, delegations, and revocations execute through the kernel command pipeline.")
            .AddEndpointFilter<KernelTenantFilter>()
            .WithTags("Organization Administration");

        group.MapGet("/team", GetTeamAsync)
            .WithName("GetOrgTeam")
            .Produces<OrgTeamViewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/invitations", InviteMemberAsync)
            .WithName("InviteOrgMember")
            .Produces<InviteMemberResponse>(StatusCodes.Status201Created)
            .Produces<InviteMemberResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/delegations", DelegateDutiesAsync)
            .WithName("DelegateOrgDuties")
            .Produces<DelegateDutiesResponse>(StatusCodes.Status201Created)
            .Produces<DelegateDutiesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/grants/{grantId:guid}/revoke", RevokeGrantAsync)
            .WithName("RevokeOrgGrant")
            .Produces<RevokeOrgGrantResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> GetTeamAsync(
        HttpContext httpContext,
        ITeamDirectoryReader directoryReader,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (ResolveCallerActor(httpContext.User) is not { } actor)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ActorUnresolved);
        }

        var tenant = KernelRequestTenant.Get(httpContext);
        var now = clock.UtcNow;
        var snapshot = await directoryReader.ReadSnapshotAsync(tenant, now, cancellationToken).ConfigureAwait(false);
        var rolesById = snapshot.Roles.ToDictionary(r => r.RoleId);
        var scopeByScope = snapshot.ScopeOptions.ToDictionary(s => s.Scope);
        var grantsById = snapshot.Grants.ToDictionary(g => g.GrantId);

        var actorActiveGrants = snapshot.Grants
            .Where(g => g.InEffect && g.Grantee == actor)
            .ToList();

        var issueGrants = actorActiveGrants
            .Where(g => GrantCarries(g, "grant.issue", rolesById))
            .ToList();
        var revokeGrants = actorActiveGrants
            .Where(g => GrantCarries(g, "grant.revoke", rolesById))
            .ToList();
        bool canInvite = issueGrants.Count > 0;
        bool canAttemptReveal = actorActiveGrants.Any(g => GrantCarries(g, "identity.reveal.person", rolesById));

        var delegableGrants = actorActiveGrants
            .Where(g => g.WithGrantOption)
            .Select(g =>
            {
                var caps = EffectiveCapabilities(g, rolesById);
                var scopeOpt = ResolveScopeOption(g.Scope, scopeByScope);
                var summary = GrantSummary(g, rolesById);
                return new OrgDelegableGrantDto(
                    g.GrantId.Value,
                    $"{summary} — {scopeOpt.Label}",
                    ScopeKey(g.Scope),
                    g.Scope.Level.ToString(),
                    g.Scope.TargetId,
                    g.Scope.Country,
                    scopeOpt.Label,
                    caps,
                    g.ValidFrom,
                    g.ValidTo);
            })
            .ToList();

        var visibleScopes = snapshot.ScopeOptions
            .Where(s => actorActiveGrants.Any(g => g.Scope.Covers(s.Path)))
            .Select(s => new OrgScopeOptionDto(
                ScopeKey(s.Scope),
                s.Scope.Level.ToString(),
                s.Scope.TargetId,
                s.Scope.Country,
                s.Code,
                s.Label,
                s.ParentLabel,
                CanIssueGrants: issueGrants.Any(g => g.Scope.Covers(s.Path))))
            .ToList();

        var visibleGrants = snapshot.Grants
            .Where(g =>
            {
                if (g.Grantee == actor || string.Equals(g.IssuedBy, actor.ToString(), StringComparison.Ordinal))
                {
                    return true;
                }

                return scopeByScope.TryGetValue(g.Scope, out var opt)
                    && actorActiveGrants.Any(held => held.Scope.Covers(opt.Path));
            })
            .ToList();

        var canRevokeByGrantId = new Dictionary<GrantId, bool>(visibleGrants.Count);
        foreach (var grant in visibleGrants.Where(g => g.InEffect))
        {
            bool signedByActor = grant.ParentGrantId is { } parentId
                && grantsById.TryGetValue(parentId, out var parent)
                && parent.Grantee == actor;
            bool coveredByRevokeGrant = scopeByScope.TryGetValue(grant.Scope, out var opt)
                && revokeGrants.Any(r => r.Scope.Covers(opt.Path));
            canRevokeByGrantId[grant.GrantId] = signedByActor || coveredByRevokeGrant;
        }

        var grantsByPerson = new Dictionary<PersonId, List<TeamGrantRecord>>();
        foreach (var grant in visibleGrants.Where(g => g.Grantee.Kind == ActorKind.Person && Guid.TryParse(g.Grantee.Id, out _)))
        {
            var pid = PersonId.FromGuid(Guid.Parse(grant.Grantee.Id));
            if (!grantsByPerson.TryGetValue(pid, out var list))
            {
                list = [];
                grantsByPerson[pid] = list;
            }

            list.Add(grant);
        }

        bool seesAllMembers = actorActiveGrants.Any(g => g.Scope.Level == ScopeLevel.Tenant);
        var memberMap = snapshot.Members.ToDictionary(m => m.PersonId);
        foreach (var pid in grantsByPerson.Keys.Where(pid => !memberMap.ContainsKey(pid)))
        {
            var earliest = grantsByPerson[pid].Min(g => g.CreatedAt);
            memberMap[pid] = new TeamMemberRecord(pid, earliest, IsActive: true);
        }

        var visibleMembers = memberMap.Values
            .Where(m => seesAllMembers
                || grantsByPerson.ContainsKey(m.PersonId)
                || (actor.Kind == ActorKind.Person && Guid.TryParse(actor.Id, out var selfId) && m.PersonId.Value == selfId))
            .OrderBy(m => m.JoinedAt)
            .ToList();

        var vault = httpContext.RequestServices.GetService<IIdentityVault>();
        var memberDtos = new List<OrgTeamMemberDto>(visibleMembers.Count);
        foreach (var member in visibleMembers)
        {
            var (displayName, email, revealed) = await ResolveMemberIdentityAsync(
                vault, canAttemptReveal, tenant, member.PersonId, cancellationToken).ConfigureAwait(false);

            var memberGrants = grantsByPerson.GetValueOrDefault(member.PersonId) ?? [];
            var mappedGrants = memberGrants
                .Select(g => MapMemberGrant(g, rolesById, scopeByScope, canRevokeByGrantId.GetValueOrDefault(g.GrantId)))
                .ToList();

            memberDtos.Add(new OrgTeamMemberDto(
                member.PersonId.Value,
                displayName,
                email,
                revealed,
                member.JoinedAt,
                mappedGrants.Where(g => g.IsActive).ToList(),
                mappedGrants.Where(g => !g.IsActive).ToList()));
        }

        var roleDtos = snapshot.Roles
            .Select(r => new OrgRoleDto(r.RoleId.Value, r.Code, r.Name, r.Capabilities))
            .ToList();

        var dutyDtos = CtmsDutyCapabilityMap.Catalog
            .Select(d => new OrgDutyOptionDto(d.Duty, d.Label, d.Capabilities))
            .ToList();

        return Results.Ok(new OrgTeamViewResponse(
            snapshot.TenantCode,
            snapshot.TenantName,
            actor.ToString(),
            canInvite,
            delegableGrants.Count > 0,
            roleDtos,
            visibleScopes,
            delegableGrants,
            dutyDtos,
            memberDtos));
    }

    private static async Task<IResult> InviteMemberAsync(
        InviteMemberRequest request,
        HttpContext httpContext,
        IKernelChangeReason changeReason,
        ITeamDirectoryReader directoryReader,
        IKernelCommandSender commandSender,
        IOidcUserProvisioner provisioner,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (ResolveCallerActor(httpContext.User) is not { } actor)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ActorUnresolved);
        }

        if (ReadIdempotencyKey(httpContext) is not { } idempotencyKey)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        if (changeReason.Read(httpContext) is not { } reason)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        var vault = httpContext.RequestServices.GetService<IIdentityVault>();
        if (vault is null)
        {
            return KernelErrorEndpoints.Problem(OrgErrors.IdentityVaultUnavailable);
        }

        if (NormalizeAndValidateEmail(request.Email) is not { } normalizedEmail)
        {
            return KernelErrorEndpoints.Problem(OrgErrors.InvitationEmailInvalid);
        }

        var tenant = KernelRequestTenant.Get(httpContext);
        var now = clock.UtcNow;
        var snapshot = await directoryReader.ReadSnapshotAsync(tenant, now, cancellationToken).ConfigureAwait(false);
        var rolesById = snapshot.Roles.ToDictionary(r => r.RoleId);

        var targetResult = ResolveInviteTarget(request, snapshot);
        if (targetResult.Error is { } targetError)
        {
            return KernelErrorEndpoints.Problem(targetError);
        }

        var (roleId, roleCode, capability, effectiveCaps) = targetResult.Value;

        var scopeResult = ResolveTargetScope(request.ScopeLevel, request.ScopeTargetId, request.ScopeCountry, null, null, snapshot);
        if (scopeResult.Error is { } scopeError)
        {
            return KernelErrorEndpoints.Problem(scopeError);
        }

        var scopeOption = scopeResult.Value!;
        var validFrom = request.ValidFrom ?? StartOfUtcDay(now);
        var validTo = NormalizeEndOfDayIfMidnight(request.ValidTo);
        if (validTo is { } end && end <= validFrom)
        {
            return KernelErrorEndpoints.Problem(AuthorityErrors.ValidityInvalid);
        }

        bool holdsIssueOverScope = snapshot.Grants.Any(g =>
            g.InEffect
            && g.Grantee == actor
            && GrantCarries(g, "grant.issue", rolesById)
            && g.Scope.Covers(scopeOption.Path));

        if (!holdsIssueOverScope)
        {
            var probeResult = await commandSender.SendAsync(
                new IssueGrant(
                    tenant,
                    GrantId.New(),
                    ActorPerson(Guid.NewGuid()),
                    capability,
                    roleId,
                    scopeOption.Scope,
                    validFrom,
                    validTo,
                    request.WithGrantOption,
                    $"preflight:{Guid.NewGuid():N}",
                    reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!probeResult.IsSuccess)
            {
                return KernelErrorEndpoints.Problem(probeResult.Error!);
            }
        }

        var existingInVault = await vault.FindPersonByEmailAsync(tenant, normalizedEmail, cancellationToken).ConfigureAwait(false);
        var (givenName, familyName, hasExplicitName) = SplitPersonName(request.GivenName, request.FamilyName, request.FullName);
        if (existingInVault is null && !hasExplicitName)
        {
            return KernelErrorEndpoints.Problem(OrgErrors.InvitationNameRequired);
        }

        var realmRoles = CtmsDutyCapabilityMap.RealmRolesFor(roleCode, effectiveCaps, scopeOption.Scope.Level);
        var (studyCodes, siteCode) = DeriveOidcScopeAttributes(scopeOption);

        OidcProvisioningResult provisioned;
        try
        {
            provisioned = await provisioner.ProvisionOrReuseAsync(
                new OidcProvisioningRequest(
                    snapshot.TenantCode,
                    normalizedEmail,
                    hasExplicitName ? givenName : "Colleague",
                    hasExplicitName ? familyName : "Member",
                    realmRoles,
                    studyCodes,
                    siteCode,
                    existingInVault),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OidcProvisioningException ex)
        {
            return KernelErrorEndpoints.Problem(ex.Error);
        }

        var memberResult = await commandSender.SendAsync(
            new RegisterTenantMember(tenant, provisioned.PersonId, $"{idempotencyKey}:member", reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!memberResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(memberResult.Error!);
        }

        if (existingInVault is null || hasExplicitName)
        {
            var details = new IdentityDetails
            {
                GivenName = hasExplicitName ? givenName : null,
                FamilyName = hasExplicitName ? familyName : null,
                Email = normalizedEmail,
            };
            var recordResult = await commandSender.SendAsync(
                new RecordPersonIdentity(tenant, provisioned.PersonId, details, $"{idempotencyKey}:identity", reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!recordResult.IsSuccess)
            {
                return KernelErrorEndpoints.Problem(recordResult.Error!);
            }
        }

        var grantee = ActorPerson(provisioned.PersonId);
        var duplicateGrant = snapshot.Grants.FirstOrDefault(g =>
            g.InEffect
            && g.Grantee == grantee
            && g.ParentGrantId is null
            && g.RoleId == roleId
            && string.Equals(g.Capability, capability, StringComparison.Ordinal)
            && g.Scope == scopeOption.Scope
            && g.ValidTo == validTo);

        if (duplicateGrant is not null)
        {
            return Results.Ok(new InviteMemberResponse(
                provisioned.PersonId.Value,
                duplicateGrant.GrantId.Value,
                ReusedExistingAccount: true,
                ReusedExistingGrant: true,
                WelcomeEmailSent: false,
                scopeOption.Label,
                duplicateGrant.ValidFrom ?? validFrom,
                duplicateGrant.ValidTo));
        }

        var grantId = DeterministicGrantId(tenant, idempotencyKey, "invite-grant");
        var grantResult = await commandSender.SendAsync(
            new IssueGrant(
                tenant,
                grantId,
                grantee,
                capability,
                roleId,
                scopeOption.Scope,
                validFrom,
                validTo,
                request.WithGrantOption,
                $"{idempotencyKey}:grant",
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!grantResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(grantResult.Error!);
        }

        var state = grantResult.Value!;
        var response = new InviteMemberResponse(
            provisioned.PersonId.Value,
            state.GrantId.Value,
            ReusedExistingAccount: provisioned.Outcome == OidcProvisioningOutcome.ReusedExisting,
            ReusedExistingGrant: grantResult.Replayed,
            WelcomeEmailSent: provisioned.WelcomeEmailSent && !grantResult.Replayed,
            scopeOption.Label,
            state.ValidFrom ?? validFrom,
            state.ValidTo);

        return grantResult.Replayed
            ? Results.Ok(response)
            : Results.Created("/api/v1/org/team", response);
    }

    private static async Task<IResult> DelegateDutiesAsync(
        DelegateDutiesRequest request,
        HttpContext httpContext,
        IKernelChangeReason changeReason,
        ITeamDirectoryReader directoryReader,
        IKernelCommandSender commandSender,
        IOidcUserProvisioner provisioner,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (ResolveCallerActor(httpContext.User) is not { } actor)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ActorUnresolved);
        }

        if (ReadIdempotencyKey(httpContext) is not { } idempotencyKey)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        if (changeReason.Read(httpContext) is not { } reason)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        var vault = httpContext.RequestServices.GetService<IIdentityVault>();
        if (vault is null)
        {
            return KernelErrorEndpoints.Problem(OrgErrors.IdentityVaultUnavailable);
        }

        if (NormalizeAndValidateEmail(request.Email) is not { } normalizedEmail)
        {
            return KernelErrorEndpoints.Problem(OrgErrors.InvitationEmailInvalid);
        }

        var tenant = KernelRequestTenant.Get(httpContext);
        var now = clock.UtcNow;
        var snapshot = await directoryReader.ReadSnapshotAsync(tenant, now, cancellationToken).ConfigureAwait(false);
        var rolesById = snapshot.Roles.ToDictionary(r => r.RoleId);

        var scopeResult = ResolveTargetScope(
            request.ScopeLevel,
            request.ScopeTargetId,
            request.ScopeCountry,
            request.StudyCode,
            request.SiteCode,
            snapshot);
        if (scopeResult.Error is { } scopeError)
        {
            return KernelErrorEndpoints.Problem(scopeError);
        }

        var scopeOption = scopeResult.Value!;

        var delegationTargets = ResolveDelegationTargets(request, snapshot);
        if (delegationTargets.Error is { } targetError)
        {
            return KernelErrorEndpoints.Problem(targetError);
        }

        var (roleId, roleCode, capabilities) = delegationTargets.Value;

        var parentResult = SelectParentGrant(request.ParentGrantId, actor, scopeOption, capabilities, rolesById, snapshot);
        if (parentResult.Error is { } parentError)
        {
            return KernelErrorEndpoints.Problem(parentError);
        }

        var parentGrant = parentResult.Value!;
        var defaultStart = StartOfUtcDay(now);
        if (parentGrant.ValidFrom is { } parentStart && parentStart > defaultStart)
        {
            defaultStart = parentStart;
        }

        var validFrom = request.ValidFrom ?? defaultStart;
        var validTo = NormalizeEndOfDayIfMidnight(request.ValidTo) ?? parentGrant.ValidTo;
        if (validTo is { } end && end <= validFrom)
        {
            return KernelErrorEndpoints.Problem(AuthorityErrors.ValidityInvalid);
        }

        if (await ProbeDelegationFailureAsync(
                tenant,
                parentGrant,
                roleId,
                capabilities,
                scopeOption,
                validFrom,
                validTo,
                request.WithGrantOption,
                rolesById,
                commandSender,
                reason,
                cancellationToken).ConfigureAwait(false) is { } preflightError)
        {
            return KernelErrorEndpoints.Problem(preflightError);
        }

        var existingInVault = await vault.FindPersonByEmailAsync(tenant, normalizedEmail, cancellationToken).ConfigureAwait(false);
        if (existingInVault is { } selfVault && ActorPerson(selfVault) == actor)
        {
            return KernelErrorEndpoints.Problem(AuthorityErrors.SelfDelegation);
        }

        var (givenName, familyName, hasExplicitName) = SplitPersonName(request.GivenName, request.FamilyName, request.FullName);
        if (existingInVault is null && !hasExplicitName)
        {
            givenName = DeriveDisplayFallbackFromEmail(normalizedEmail);
            familyName = "Staff";
        }

        var realmRoles = CtmsDutyCapabilityMap.RealmRolesFor(roleCode, capabilities, scopeOption.Scope.Level);
        var (studyCodes, siteCode) = DeriveOidcScopeAttributes(scopeOption);

        OidcProvisioningResult provisioned;
        try
        {
            provisioned = await provisioner.ProvisionOrReuseAsync(
                new OidcProvisioningRequest(
                    snapshot.TenantCode,
                    normalizedEmail,
                    givenName,
                    familyName,
                    realmRoles,
                    studyCodes,
                    siteCode,
                    existingInVault),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OidcProvisioningException ex)
        {
            return KernelErrorEndpoints.Problem(ex.Error);
        }

        var grantee = ActorPerson(provisioned.PersonId);
        if (grantee == actor)
        {
            return KernelErrorEndpoints.Problem(AuthorityErrors.SelfDelegation);
        }

        var memberResult = await commandSender.SendAsync(
            new RegisterTenantMember(tenant, provisioned.PersonId, $"{idempotencyKey}:member", reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!memberResult.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(memberResult.Error!);
        }

        if (existingInVault is null || hasExplicitName)
        {
            var details = new IdentityDetails
            {
                GivenName = givenName,
                FamilyName = familyName,
                Email = normalizedEmail,
            };
            var recordResult = await commandSender.SendAsync(
                new RecordPersonIdentity(tenant, provisioned.PersonId, details, $"{idempotencyKey}:identity", reason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!recordResult.IsSuccess)
            {
                return KernelErrorEndpoints.Problem(recordResult.Error!);
            }
        }

        var issuedIds = new List<Guid>();
        bool anyNewlyCreated = false;

        if (roleId is { } targetRoleId)
        {
            var existingChild = snapshot.Grants.FirstOrDefault(g =>
                g.InEffect
                && g.Grantee == grantee
                && g.ParentGrantId == parentGrant.GrantId
                && g.RoleId == targetRoleId
                && g.Scope == scopeOption.Scope
                && g.ValidTo == validTo);

            if (existingChild is not null)
            {
                issuedIds.Add(existingChild.GrantId.Value);
            }
            else
            {
                var childId = DeterministicGrantId(tenant, idempotencyKey, "delegate-role");
                var delResult = await commandSender.SendAsync(
                    new DelegateGrant(
                        tenant,
                        childId,
                        parentGrant.GrantId,
                        grantee,
                        null,
                        targetRoleId,
                        scopeOption.Scope,
                        validFrom,
                        validTo,
                        request.WithGrantOption,
                        $"{idempotencyKey}:delegate:role",
                        reason),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!delResult.IsSuccess)
                {
                    return KernelErrorEndpoints.Problem(delResult.Error!);
                }

                issuedIds.Add(delResult.Value!.GrantId.Value);
                anyNewlyCreated |= !delResult.Replayed;
            }
        }
        else
        {
            foreach (var cap in capabilities)
            {
                var existingChild = snapshot.Grants.FirstOrDefault(g =>
                    g.InEffect
                    && g.Grantee == grantee
                    && g.ParentGrantId == parentGrant.GrantId
                    && string.Equals(g.Capability, cap, StringComparison.Ordinal)
                    && g.Scope == scopeOption.Scope
                    && g.ValidTo == validTo);

                if (existingChild is not null)
                {
                    issuedIds.Add(existingChild.GrantId.Value);
                    continue;
                }

                var childId = DeterministicGrantId(tenant, idempotencyKey, $"delegate:{cap}");
                var delResult = await commandSender.SendAsync(
                    new DelegateGrant(
                        tenant,
                        childId,
                        parentGrant.GrantId,
                        grantee,
                        cap,
                        null,
                        scopeOption.Scope,
                        validFrom,
                        validTo,
                        request.WithGrantOption,
                        $"{idempotencyKey}:delegate:{cap}",
                        reason),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!delResult.IsSuccess)
                {
                    return KernelErrorEndpoints.Problem(delResult.Error!);
                }

                issuedIds.Add(delResult.Value!.GrantId.Value);
                anyNewlyCreated |= !delResult.Replayed;
            }
        }

        var response = new DelegateDutiesResponse(
            provisioned.PersonId.Value,
            parentGrant.GrantId.Value,
            issuedIds[0],
            issuedIds,
            capabilities,
            ReusedExistingAccount: provisioned.Outcome == OidcProvisioningOutcome.ReusedExisting,
            WelcomeEmailSent: provisioned.WelcomeEmailSent && anyNewlyCreated,
            scopeOption.Label,
            validFrom,
            validTo);

        return anyNewlyCreated
            ? Results.Created("/api/v1/org/team", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> RevokeGrantAsync(
        Guid grantId,
        RevokeOrgGrantRequest? request,
        HttpContext httpContext,
        IKernelChangeReason changeReason,
        IKernelCommandSender commandSender,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (ResolveCallerActor(httpContext.User) is null)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ActorUnresolved);
        }

        if (ReadIdempotencyKey(httpContext) is not { } idempotencyKey)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.IdempotencyKeyRequired);
        }

        if (changeReason.Read(httpContext) is not { } reason)
        {
            return KernelErrorEndpoints.Problem(KernelErrors.ReasonForChangeRequired);
        }

        var tenant = KernelRequestTenant.Get(httpContext);
        var effectiveAt = request?.EffectiveAt ?? StartOfUtcDay(clock.UtcNow);

        var result = await commandSender.SendAsync(
            new RevokeGrant(tenant, GrantId.FromGuid(grantId), effectiveAt, idempotencyKey, reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return KernelErrorEndpoints.Problem(result.Error!);
        }

        var state = result.Value!;
        return Results.Ok(new RevokeOrgGrantResponse(
            state.GrantId.Value,
            Revoked: true,
            state.ValidFrom,
            state.ValidTo));
    }

    private static Actor ActorPerson(PersonId personId) => Actor.Person(personId.Value.ToString("D"));

    private static Actor ActorPerson(Guid personGuid) => Actor.Person(personGuid.ToString("D"));

    private static Actor? ResolveCallerActor(ClaimsPrincipal user)
    {
        if (user.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        var subject = user.FindFirst(CadenceClaimTypes.Subject)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        try
        {
            return Actor.Person(subject);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static async Task<(string DisplayName, string? Email, bool Revealed)> ResolveMemberIdentityAsync(
        IIdentityVault? vault,
        bool canAttemptReveal,
        TenantId tenant,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        var shortId = personId.Value.ToString("D")[..8];
        if (vault is null || !canAttemptReveal)
        {
            return ($"Member {shortId}", null, false);
        }

        var revealed = await vault.RevealAsync(
            new RevealPersonIdentity(tenant, personId, $"org-team-reveal:{Guid.NewGuid():N}", "View tenant team directory member identity"),
            cancellationToken).ConfigureAwait(false);
        if (!revealed.IsSuccess || revealed.Value is not { } details)
        {
            return ($"Member {shortId}", null, false);
        }

        var fullName = string.Join(" ", new[] { details.GivenName, details.FamilyName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var displayName = !string.IsNullOrWhiteSpace(fullName)
            ? fullName
            : details.Email ?? $"Member {shortId}";
        return (displayName, details.Email, true);
    }

    private static OrgMemberGrantDto MapMemberGrant(
        TeamGrantRecord grant,
        IReadOnlyDictionary<RoleId, TeamRoleRecord> rolesById,
        IReadOnlyDictionary<AuthorityScope, TeamScopeOption> scopeByScope,
        bool canRevoke)
    {
        var capabilities = EffectiveCapabilities(grant, rolesById);
        var scopeOpt = ResolveScopeOption(grant.Scope, scopeByScope);
        string kind = DetermineGrantKind(grant);
        string? roleCode = grant.RoleId is { } rid && rolesById.TryGetValue(rid, out var role) ? role.Code : null;

        return new OrgMemberGrantDto(
            grant.GrantId.Value,
            kind,
            GrantSummary(grant, rolesById),
            grant.RoleId?.Value,
            roleCode,
            grant.Capability,
            capabilities,
            grant.Scope.Level.ToString(),
            grant.Scope.TargetId,
            grant.Scope.Country,
            scopeOpt.Code,
            scopeOpt.Label,
            grant.ParentGrantId?.Value,
            grant.WithGrantOption,
            grant.IssuedBy,
            grant.CreatedAt,
            grant.ReasonForChange,
            grant.ValidFrom,
            grant.ValidTo,
            grant.InEffect,
            grant.Revoked,
            canRevoke);
    }

    private static string DetermineGrantKind(TeamGrantRecord grant)
    {
        if (grant.ParentGrantId is not null)
        {
            return "delegation";
        }

        return grant.RoleId is not null ? "role" : "capability";
    }

    private static CommandResult<(RoleId? RoleId, string? RoleCode, string? Capability, IReadOnlyList<string> Capabilities)> ResolveInviteTarget(
        InviteMemberRequest request,
        TeamDirectorySnapshot snapshot)
    {
        bool hasRole = request.RoleId is not null || !string.IsNullOrWhiteSpace(request.RoleCode);
        bool hasCap = !string.IsNullOrWhiteSpace(request.Capability);
        if (hasRole == hasCap)
        {
            return OrgErrors.InvitationTargetInvalid;
        }

        if (hasCap)
        {
            var cap = request.Capability!.Trim();
            if (!Capability.IsWellFormed(cap))
            {
                return AuthorityErrors.CapabilityInvalid;
            }

            if (Capability.IsUnblinding(cap))
            {
                return BuildUnblindingError(cap);
            }

            return CommandResult<(RoleId?, string?, string?, IReadOnlyList<string>)>.Success((null, null, cap, [cap]));
        }

        var matched = request.RoleId is { } rid
            ? snapshot.Roles.FirstOrDefault(r => r.RoleId.Value == rid)
            : snapshot.Roles.FirstOrDefault(r => string.Equals(r.Code, request.RoleCode!.Trim(), StringComparison.OrdinalIgnoreCase));

        if (matched is null)
        {
            return AuthorityErrors.RoleUnknown;
        }

        return CommandResult<(RoleId?, string?, string?, IReadOnlyList<string>)>.Success((matched.RoleId, matched.Code, null, matched.Capabilities));
    }

    private static CommandResult<(RoleId? RoleId, string? RoleCode, IReadOnlyList<string> Capabilities)> ResolveDelegationTargets(
        DelegateDutiesRequest request,
        TeamDirectorySnapshot snapshot)
    {
        if (request.RoleId is not null || !string.IsNullOrWhiteSpace(request.RoleCode))
        {
            var matched = request.RoleId is { } rid
                ? snapshot.Roles.FirstOrDefault(r => r.RoleId.Value == rid)
                : snapshot.Roles.FirstOrDefault(r => string.Equals(r.Code, request.RoleCode!.Trim(), StringComparison.OrdinalIgnoreCase));
            if (matched is null)
            {
                return AuthorityErrors.RoleUnknown;
            }

            return CommandResult<(RoleId?, string?, IReadOnlyList<string>)>.Success((matched.RoleId, matched.Code, matched.Capabilities));
        }

        var (resolved, hasUnblinding, invalid) = CtmsDutyCapabilityMap.ResolveDutiesAndCapabilities(request.Duties, request.Capabilities);
        if (invalid is not null)
        {
            return OrgErrors.InvitationTargetInvalid.WithMessage($"Unknown duty or malformed capability '{invalid}'.");
        }

        if (hasUnblinding)
        {
            return BuildUnblindingError(resolved.First(Capability.IsUnblinding));
        }

        if (resolved.Count == 0)
        {
            return OrgErrors.InvitationTargetInvalid;
        }

        return CommandResult<(RoleId?, string?, IReadOnlyList<string>)>.Success((null, null, resolved));
    }

    private static CommandResult<TeamScopeOption> ResolveTargetScope(
        string? scopeLevel,
        Guid? scopeTargetId,
        string? scopeCountry,
        string? studyCode,
        string? siteCode,
        TeamDirectorySnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(scopeLevel))
        {
            if (!Enum.TryParse<ScopeLevel>(scopeLevel.Trim(), ignoreCase: true, out var parsedLevel))
            {
                return AuthorityErrors.ScopeInvalid;
            }

            var scope = new AuthorityScope(parsedLevel, scopeTargetId, scopeCountry);
            if (scope.Problem() is { } problem)
            {
                return AuthorityErrors.ScopeInvalid.WithMessage(problem);
            }

            var matched = snapshot.ScopeOptions.FirstOrDefault(s => s.Scope == scope);
            return matched is not null ? CommandResult<TeamScopeOption>.Success(matched) : AuthorityErrors.ScopeUnknown;
        }

        if (!string.IsNullOrWhiteSpace(siteCode))
        {
            var trimmedSite = siteCode.Trim();
            var siteMatches = snapshot.ScopeOptions
                .Where(s => s.Scope.Level == ScopeLevel.StudySite && string.Equals(s.Code, trimmedSite, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!string.IsNullOrWhiteSpace(studyCode))
            {
                var trimmedStudy = studyCode.Trim();
                var filtered = siteMatches
                    .Where(s => string.Equals(s.ParentLabel, trimmedStudy, StringComparison.OrdinalIgnoreCase)
                        || (Guid.TryParse(trimmedStudy, out var studyGuid) && s.Path.Study?.Value == studyGuid))
                    .ToList();
                if (filtered.Count > 0)
                {
                    siteMatches = filtered;
                }
            }

            if (siteMatches.Count == 1)
            {
                return CommandResult<TeamScopeOption>.Success(siteMatches[0]);
            }

            return siteMatches.Count > 1 ? OrgErrors.InvitationTargetInvalid : AuthorityErrors.ScopeUnknown;
        }

        if (!string.IsNullOrWhiteSpace(studyCode))
        {
            var trimmedStudy = studyCode.Trim();
            var studyMatch = snapshot.ScopeOptions.FirstOrDefault(s =>
                s.Scope.Level == ScopeLevel.Study
                && (string.Equals(s.Code, trimmedStudy, StringComparison.OrdinalIgnoreCase)
                    || (Guid.TryParse(trimmedStudy, out var studyGuid) && s.Scope.TargetId == studyGuid)));
            return studyMatch is not null ? CommandResult<TeamScopeOption>.Success(studyMatch) : AuthorityErrors.ScopeUnknown;
        }

        return OrgErrors.InvitationTargetInvalid;
    }

    private static CommandResult<TeamGrantRecord> SelectParentGrant(
        Guid? explicitParentId,
        Actor actor,
        TeamScopeOption targetScope,
        IReadOnlyList<string> requestedCapabilities,
        IReadOnlyDictionary<RoleId, TeamRoleRecord> rolesById,
        TeamDirectorySnapshot snapshot)
    {
        if (explicitParentId is { } parentGuid)
        {
            var pid = GrantId.FromGuid(parentGuid);
            var explicitGrant = snapshot.Grants.FirstOrDefault(g => g.GrantId == pid);
            return explicitGrant is not null ? CommandResult<TeamGrantRecord>.Success(explicitGrant) : KernelErrors.NotFound;
        }

        var actorGrants = snapshot.Grants.Where(g => g.Grantee == actor).ToList();
        var activeDelegable = actorGrants.Where(g => g.InEffect && g.WithGrantOption).ToList();
        if (activeDelegable.Count > 0)
        {
            var fullMatch = activeDelegable.FirstOrDefault(g =>
                g.Scope.Covers(targetScope.Path)
                && requestedCapabilities.All(c => GrantCarries(g, c, rolesById)));
            if (fullMatch is not null)
            {
                return CommandResult<TeamGrantRecord>.Success(fullMatch);
            }

            var scopeMatch = activeDelegable.FirstOrDefault(g => g.Scope.Covers(targetScope.Path));
            return CommandResult<TeamGrantRecord>.Success(scopeMatch ?? activeDelegable[0]);
        }

        var nonDelegableActive = actorGrants.FirstOrDefault(g => g.InEffect);
        if (nonDelegableActive is not null)
        {
            return CommandResult<TeamGrantRecord>.Success(nonDelegableActive);
        }

        return OrgErrors.NoDelegableGrant.WithExplanation(new Explanation(
            Subject: null,
            Claim: $"Delegation by {actor.Id} is not permitted because no active delegable grant is held.",
            Producer: "kernel.authority",
            Steps: [new ExplanationStep($"{actor.Id} holds no active grant with WithGrantOption in this tenant.", [])],
            Conditions:
            [
                EvaluatedCondition.Unsatisfied(
                    "grant.delegable",
                    "Hold an active grant issued with WithGrantOption covering the target site or study"),
            ],
            NextActions:
            [
                new NextAction("Ask a tenant or study administrator to issue a delegable role grant (WithGrantOption).", "grant.issue"),
            ]));
    }

    private static async Task<KernelError?> ProbeDelegationFailureAsync(
        TenantId tenant,
        TeamGrantRecord parentGrant,
        RoleId? roleId,
        IReadOnlyList<string> capabilities,
        TeamScopeOption targetScope,
        DateTimeOffset validFrom,
        DateTimeOffset? validTo,
        bool withGrantOption,
        IReadOnlyDictionary<RoleId, TeamRoleRecord> rolesById,
        IKernelCommandSender commandSender,
        string reason,
        CancellationToken cancellationToken)
    {
        var heldCaps = EffectiveCapabilities(parentGrant, rolesById).ToHashSet(StringComparer.Ordinal);
        string? failingCap = capabilities.FirstOrDefault(c => !heldCaps.Contains(c));

        bool needsProbe = !parentGrant.InEffect
            || !parentGrant.WithGrantOption
            || failingCap is not null
            || !parentGrant.Scope.Covers(targetScope.Path)
            || (parentGrant.ValidFrom is { } pStart && validFrom < pStart)
            || (parentGrant.ValidTo is { } pEnd && (validTo is null || validTo.Value > pEnd));

        if (!needsProbe)
        {
            return null;
        }

        var probeGrantee = ActorPerson(Guid.NewGuid());
        var probeCap = roleId is null ? (failingCap ?? capabilities[0]) : null;
        var probeResult = await commandSender.SendAsync(
            new DelegateGrant(
                tenant,
                GrantId.New(),
                parentGrant.GrantId,
                probeGrantee,
                probeCap,
                roleId,
                targetScope.Scope,
                validFrom,
                validTo,
                withGrantOption,
                $"preflight:{Guid.NewGuid():N}",
                reason),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return probeResult.Error;
    }

    private static KernelError BuildUnblindingError(string capability) =>
        OrgErrors.UnblindingForbidden.WithExplanation(new Explanation(
            Subject: null,
            Claim: $"Unblinding capability '{capability}' cannot be bundled into team invitations or site delegations (K-AUTH-4).",
            Producer: "org.authority",
            Steps: [new ExplanationStep("Clinical kernel rule K-AUTH-4 forbids bundling unblinding capabilities in general role or delegation workflows.", [])],
            Conditions:
            [
                EvaluatedCondition.Unsatisfied(
                    "delegation.unblinding_excluded",
                    $"Exclude unblinding capability '{capability}' from the invitation or delegation"),
            ],
            NextActions:
            [
                new NextAction("Select non-unblinding clinical duties or issue dedicated emergency unblinding authority separately."),
            ]));

    private static (IReadOnlyList<string> StudyCodes, string? SiteCode) DeriveOidcScopeAttributes(TeamScopeOption scopeOption) =>
        scopeOption.Scope.Level switch
        {
            ScopeLevel.Tenant or ScopeLevel.Organization => (["*"], null),
            ScopeLevel.Study or ScopeLevel.StudyCountry => ([scopeOption.Code], null),
            ScopeLevel.StudySite => (
                scopeOption.ParentLabel is { Length: > 0 } parent ? [parent] : ["*"],
                scopeOption.Code),
            _ => ([], null),
        };

    private static string? NormalizeAndValidateEmail(string? rawEmail)
    {
        if (string.IsNullOrWhiteSpace(rawEmail))
        {
            return null;
        }

        var normalized = rawEmail.Trim().ToLowerInvariant();
        var probe = new IdentityDetails { Email = normalized };
        return probe.Validate(IdentityLayer.Person) is null ? normalized : null;
    }

    private static (string GivenName, string FamilyName, bool HasExplicitName) SplitPersonName(
        string? givenName,
        string? familyName,
        string? fullName)
    {
        if (!string.IsNullOrWhiteSpace(givenName) || !string.IsNullOrWhiteSpace(familyName))
        {
            var g = string.IsNullOrWhiteSpace(givenName) ? "Colleague" : givenName.Trim();
            var f = string.IsNullOrWhiteSpace(familyName) ? "Member" : familyName.Trim();
            return (g, f, true);
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (string.Empty, string.Empty, false);
        }

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
        {
            return (parts[0], "Member", true);
        }

        return (parts[0], string.Join(" ", parts.Skip(1)), true);
    }

    private static string DeriveDisplayFallbackFromEmail(string normalizedEmail)
    {
        var local = normalizedEmail.Split('@')[0];
        var cleaned = local.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Site" : cleaned;
    }

    private static string? ReadIdempotencyKey(HttpContext httpContext)
    {
        var raw = httpContext.Request.Headers[IdempotencyHeader].ToString();
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private static DateTimeOffset StartOfUtcDay(DateTimeOffset timestamp) =>
        new(timestamp.UtcDateTime.Date, TimeSpan.Zero);

    private static DateTimeOffset? NormalizeEndOfDayIfMidnight(DateTimeOffset? validTo)
    {
        if (validTo is not { } end)
        {
            return null;
        }

        var utc = end.ToUniversalTime();
        return utc.TimeOfDay == TimeSpan.Zero
            ? utc.AddDays(1).AddTicks(-10)
            : utc;
    }

    private static GrantId DeterministicGrantId(TenantId tenant, string idempotencyKey, string discriminator)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"org-grant:{tenant.Value:D}:{idempotencyKey}:{discriminator}"));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return GrantId.FromGuid(new Guid(bytes));
    }

    private static bool GrantCarries(TeamGrantRecord grant, string capability, IReadOnlyDictionary<RoleId, TeamRoleRecord> rolesById) =>
        string.Equals(grant.Capability, capability, StringComparison.Ordinal)
        || (grant.RoleId is { } rid && rolesById.TryGetValue(rid, out var role) && role.Capabilities.Contains(capability, StringComparer.Ordinal));

    private static IReadOnlyList<string> EffectiveCapabilities(TeamGrantRecord grant, IReadOnlyDictionary<RoleId, TeamRoleRecord> rolesById)
    {
        if (grant.Capability is { } cap)
        {
            return [cap];
        }

        return grant.RoleId is { } rid && rolesById.TryGetValue(rid, out var role)
            ? role.Capabilities
            : [];
    }

    private static string GrantSummary(TeamGrantRecord grant, IReadOnlyDictionary<RoleId, TeamRoleRecord> rolesById)
    {
        if (grant.RoleId is { } rid && rolesById.TryGetValue(rid, out var role))
        {
            return role.Name;
        }

        return grant.Capability ?? "Grant";
    }

    private static TeamScopeOption ResolveScopeOption(AuthorityScope scope, IReadOnlyDictionary<AuthorityScope, TeamScopeOption> scopeByScope) =>
        scopeByScope.TryGetValue(scope, out var opt)
            ? opt
            : new TeamScopeOption(scope, ScopePath.TenantWide, scope.Level.ToString(), scope.ToString());

    private static string ScopeKey(AuthorityScope scope) =>
        $"{scope.Level}:{scope.TargetId?.ToString("D") ?? "*"}:{scope.Country ?? "*"}";
}
