using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cadence.Kernel.Host.Onboarding;

/// <summary>
/// Self-service trial workspace bootstrap for newly registered OIDC users lacking an active <c>tenant_id</c> claim.
/// Dispatches single-aggregate commands through <see cref="IKernelCommandSender"/> as
/// <see cref="KernelAuthorityOptions.TrialOnboardingOperator"/> so every record (<c>Tenant</c>, <c>Organization</c>,
/// pseudonymous <c>Person</c>, <c>TenantMembership</c>, <c>Role</c>, <c>Grant</c>, <c>Subscription</c>, and
/// <c>ProductEntitlement</c>) commits with full kernel audit and outbox guarantees, then writes <c>tenant_id</c> and
/// initial roles back to Keycloak. Strictly idempotent per OIDC <c>sub</c>.
/// </summary>
public sealed partial class TrialOnboardingService(
    ITenantDirectory directory,
    ITenantEntitlementReader entitlementReader,
    IServiceScopeFactory scopes,
    IKeycloakOnboardingClient keycloak,
    IClock clock,
    ILogger<TrialOnboardingService> logger)
{
    /// <summary>Initial realm roles written back to Keycloak for the workspace founder.</summary>
    public static readonly IReadOnlyList<string> InitialKeycloakRoles = ["Sponsor Admin", "Sponsor Designer"];

    /// <summary>Fixed valid-from anchor for initial trial grants so command payloads remain identical across retries.</summary>
    internal static readonly DateTimeOffset GrantEpoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const int TrialDurationDays = 30;
    private const string Reason = "Bootstrap self-service pre-release trial workspace for authenticated OIDC caller";

    private static readonly string[] OnboardingOperatorCapabilities =
    [
        "organization.register",
        "person.register",
        "tenant.enroll_member",
        "subscription.provision",
        "entitlement.grant",
        "work_queue.define",
    ];

    private static readonly string[] SponsorAdminCapabilities =
    [
        "organization.register",
        "person.register",
        "tenant.enroll_member",
        "role.define",
        "grant.issue",
        "grant.delegate",
        "grant.revoke",
        "study.register",
        "site.register",
        "study_site.link",
        "study_site.activate",
        "participant.register",
        "participant.screen",
        "enrollment.change_status",
        "participant.withdraw",
        "observation.record",
        "observation.correct",
        "observation.verify",
        "observation.query",
        "observation.query.answer",
        "assertion.retract",
        "lock.apply",
        "lock.release",
        "collection.open",
        "collection.record",
        "collection.record_unasked",
        "collection.sign",
        "attestation.sign",
        "code_system.register",
        "code_system.import_version",
        "concept.define",
        "concept.revise",
        "effectivity.declare",
        "effectivity.change_status",
        "subscription.switch_tier",
        "subscription.activate",
        "subscription.expire",
        "entitlement.grant",
        "entitlement.revoke",
        "execution.query.raise",
        "execution.query.answer",
        "execution.query.manage",
        "library_item.draft",
        "library_item.approve",
        "library_item.deprecate",
        "standard.register",
        "standard.approve",
        "study.declare_standards",
        "validation_run.record",
        "validation_finding.investigate",
        "validation_finding.classify",
        "validation_finding.accept",
        "validation_finding.explain",
        "validation_finding.reopen",
        "work_queue.define",
        "proposal.approve",
        "proposal.reject",
    ];

    private static readonly string[] SponsorDesignerCapabilities =
    [
        "study.register",
        "protocol.draft",
        "protocol.revise_draft",
        "protocol.submit_for_review",
        "protocol.return_to_draft",
        "protocol.approve",
        "protocol.withdraw",
        "protocol.amend",
        "effectivity.declare",
        "effectivity.change_status",
        "code_system.register",
        "code_system.import_version",
        "concept.define",
        "concept.revise",
        "library_item.draft",
        "library_item.approve",
        "standard.register",
        "standard.approve",
        "study.declare_standards",
        "validation_run.record",
        "attestation.sign",
    ];

    private static readonly (string Consumer, string RoleCode, string Name, string[] Capabilities)[] ModuleServiceAccounts =
    [
        ("execution-forms", "TRIAL_EXECUTION_FORMS", "Execution forms consumer", ["execution.form.open"]),
        ("execution-queries", "TRIAL_EXECUTION_QUERIES", "Execution queries consumer", ["observation.query", "observation.query.answer", "assertion.retract"]),
        ("execution-edit-checks", "TRIAL_EXECUTION_EDIT_CHECKS", "Execution edit checks consumer", ["validation_run.record", "collection.record_unasked", "execution.query.raise", "execution.query.manage"]),
    ];

    public async Task<CommandResult<TrialOnboardingOutcome>> BootstrapAsync(
        Actor? caller,
        TrialOnboardingInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (caller is not { Kind: ActorKind.Person, Id.Length: > 0 })
        {
            return CommercialErrors.OnboardingCallerInvalid;
        }

        string organizationName = (input.OrganizationName ?? string.Empty).Trim();
        if (organizationName.Length is 0 or > 200)
        {
            return IdentityErrors.NameRequired;
        }

        string? residency = string.IsNullOrWhiteSpace(input.Residency)
            ? null
            : input.Residency.Trim().ToUpperInvariant();
        if (residency is not null && !ResidencyPattern().IsMatch(residency))
        {
            return IdentityErrors.ResidencyInvalid;
        }

        CommercialTier requestedTier = CommercialTier.Individual;
        if (!string.IsNullOrWhiteSpace(input.Tier)
            && !Enum.TryParse(input.Tier.Trim(), ignoreCase: true, out requestedTier))
        {
            return CommercialErrors.TierInvalid;
        }

        string subject = caller.Id;
        var ids = new TrialIds(subject);
        string tenantCode = ids.TenantCode;

        var existingTenant = await directory.FindActiveByCodeAsync(tenantCode, cancellationToken).ConfigureAwait(false);
        bool newlyProvisioned = existingTenant is null;
        TenantId tenant;
        if (existingTenant is { } resolvedTenant)
        {
            tenant = resolvedTenant;
        }
        else
        {
            var provisioned = await SendAsOperatorAsync(
                new ProvisionTenant(ids.Tenant, tenantCode, organizationName, ids.Key("tenant"), Reason, residency),
                cancellationToken).ConfigureAwait(false);
            if (!provisioned.IsSuccess)
            {
                if (provisioned.Error?.Code == KernelErrors.AlreadyExists.Code
                    && await directory.FindActiveByCodeAsync(tenantCode, cancellationToken).ConfigureAwait(false) is { } raced)
                {
                    tenant = raced;
                    newlyProvisioned = false;
                }
                else
                {
                    return provisioned.Error!;
                }
            }
            else
            {
                tenant = provisioned.Value;
            }
        }

        if (await EnsureRoleAndGrantAsync(
                tenant,
                ids.Role("TRIAL_ONBOARDING"),
                "TRIAL_ONBOARDING",
                "Trial onboarding operator",
                OnboardingOperatorCapabilities,
                ids.Grant("operator"),
                KernelAuthorityOptions.TrialOnboardingOperator,
                AuthorityScope.Tenant(),
                GrantEpoch,
                withGrantOption: false,
                ids,
                "operator",
                cancellationToken).ConfigureAwait(false) is { } operatorErr)
        {
            return operatorErr;
        }

        OrganizationId organizationId = ids.Organization;
        if (await EnsureRecordAsync(
                new RegisterOrganization(
                    tenant,
                    organizationId,
                    ids.OrganizationCode,
                    organizationName,
                    OrganizationKind.Sponsor,
                    ids.Key($"organization:{organizationName}"),
                    Reason),
                cancellationToken).ConfigureAwait(false) is { } orgErr)
        {
            return orgErr;
        }

        if (await EnsureRecordAsync(
                new RegisterPerson(tenant, ids.Person, ids.Key("person"), Reason),
                cancellationToken).ConfigureAwait(false) is { } personErr)
        {
            return personErr;
        }

        if (await EnsureRecordAsync(
                new RegisterTenantMembership(tenant, ids.Membership, ids.Person, ids.Key("membership"), Reason),
                cancellationToken).ConfigureAwait(false) is { } membershipErr)
        {
            return membershipErr;
        }

        if (await EnsureRoleAndGrantAsync(
                tenant,
                ids.Role("SPONSOR_ADMIN"),
                "SPONSOR_ADMIN",
                "Sponsor administrator",
                SponsorAdminCapabilities,
                ids.Grant("founder:sponsor-admin"),
                Actor.Person(subject),
                AuthorityScope.Tenant(),
                GrantEpoch,
                withGrantOption: true,
                ids,
                "sponsor-admin",
                cancellationToken).ConfigureAwait(false) is { } adminErr)
        {
            return adminErr;
        }

        if (await EnsureRoleAndGrantAsync(
                tenant,
                ids.Role("SPONSOR_DESIGNER"),
                "SPONSOR_DESIGNER",
                "Sponsor study designer",
                SponsorDesignerCapabilities,
                ids.Grant("founder:sponsor-designer"),
                Actor.Person(subject),
                AuthorityScope.Tenant(),
                GrantEpoch,
                withGrantOption: false,
                ids,
                "sponsor-designer",
                cancellationToken).ConfigureAwait(false) is { } designerErr)
        {
            return designerErr;
        }

        foreach (var (consumer, roleCode, roleName, capabilities) in ModuleServiceAccounts)
        {
            if (await EnsureRoleAndGrantAsync(
                    tenant,
                    ids.Role(roleCode),
                    roleCode,
                    roleName,
                    capabilities,
                    ids.Grant($"svc:{consumer}"),
                    Actor.ServiceAccount(consumer),
                    AuthorityScope.Tenant(),
                    GrantEpoch,
                    withGrantOption: false,
                    ids,
                    $"svc:{consumer}",
                    cancellationToken).ConfigureAwait(false) is { } svcErr)
            {
                return svcErr;
            }
        }

        if (await EnsureDefaultWorkQueuesAsync(tenant, ids, cancellationToken).ConfigureAwait(false) is { } wqErr)
        {
            return wqErr;
        }

        var currentEntitlements = await entitlementReader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        SubscriptionId subscriptionId = currentEntitlements.SubscriptionId ?? ids.Subscription;
        if (currentEntitlements.SubscriptionId is null)
        {
            var trialEndsAt = KernelTimestamps.ToStoredPrecision(clock.UtcNow).AddDays(TrialDurationDays);
            var subResult = await SendAsOperatorAsync(
                new ProvisionSubscription(
                    tenant,
                    subscriptionId,
                    requestedTier,
                    SubscriptionStatus.Trialing,
                    trialEndsAt,
                    ids.Key($"subscription:{requestedTier}:{trialEndsAt.UtcTicks}"),
                    Reason),
                cancellationToken).ConfigureAwait(false);
            if (!subResult.IsSuccess && subResult.Error?.Code != CommercialErrors.SubscriptionAlreadyProvisioned.Code)
            {
                return subResult.Error!;
            }
        }

        var effectiveTier = currentEntitlements.SubscriptionId is not null ? currentEntitlements.Tier : requestedTier;
        foreach (var family in CommercialCatalog.FamiliesForTier(effectiveTier))
        {
            var entitlementResult = await SendAsOperatorAsync(
                new GrantProductEntitlement(
                    tenant,
                    ids.Entitlement(family),
                    subscriptionId,
                    family,
                    ids.Key($"entitlement:{family}"),
                    Reason),
                cancellationToken).ConfigureAwait(false);
            if (!entitlementResult.IsSuccess)
            {
                return entitlementResult.Error!;
            }
        }

        try
        {
            await keycloak.AssignTrialWorkspaceAsync(subject, tenantCode, InitialKeycloakRoles, cancellationToken).ConfigureAwait(false);
        }
        catch (KeycloakOnboardingException)
        {
            return CommercialErrors.IdentityProviderSyncFailed;
        }

        var finalSnapshot = await entitlementReader.GetEntitlementsAsync(tenant, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        LogTrialProvisioned(logger, tenantCode, tenant, newlyProvisioned, finalSnapshot.Tier);

        return CommandResult<TrialOnboardingOutcome>.Success(
            new TrialOnboardingOutcome(
                newlyProvisioned,
                tenant,
                tenantCode,
                organizationId,
                ids.Person,
                ids.Membership,
                ids.Role("SPONSOR_ADMIN"),
                ids.Grant("founder:sponsor-admin"),
                InitialKeycloakRoles,
                finalSnapshot));
    }

    /// <summary>Derives the deterministic kernel tenant code for an OIDC <paramref name="subject"/>.</summary>
    public static string DeriveTenantCode(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-trial-tenant-code:{subject}"));
        return $"trial-{Convert.ToHexStringLower(hash)[..12]}";
    }

    private async Task<KernelError?> EnsureRoleAndGrantAsync(
        TenantId tenant,
        RoleId roleId,
        string roleCode,
        string roleName,
        IReadOnlyList<string> capabilities,
        GrantId grantId,
        Actor grantee,
        AuthorityScope scope,
        DateTimeOffset validFrom,
        bool withGrantOption,
        TrialIds ids,
        string keySuffix,
        CancellationToken cancellationToken)
    {
        var roleResult = await SendAsOperatorAsync(
            new DefineRole(tenant, roleId, roleCode, roleName, capabilities, ids.Key($"role:{roleCode}"), Reason),
            cancellationToken).ConfigureAwait(false);
        if (!roleResult.IsSuccess
            && roleResult.Error?.Code != KernelErrors.AlreadyExists.Code
            && roleResult.Error?.Code != AuthorityErrors.RoleCodeTaken.Code)
        {
            return roleResult.Error!;
        }

        var grantResult = await SendAsOperatorAsync(
            new IssueGrant(tenant, grantId, grantee, null, roleId, scope, validFrom, null, withGrantOption, ids.Key($"grant:{keySuffix}"), Reason),
            cancellationToken).ConfigureAwait(false);
        if (!grantResult.IsSuccess && grantResult.Error?.Code != KernelErrors.AlreadyExists.Code)
        {
            return grantResult.Error!;
        }

        return null;
    }

    private async Task<KernelError?> EnsureDefaultWorkQueuesAsync(
        TenantId tenant,
        TrialIds ids,
        CancellationToken cancellationToken)
    {
        HashSet<string> existingCodes = [];
        await using (var scope = scopes.CreateAsyncScope())
        {
            if (scope.ServiceProvider.GetService<IWorkQueueDefinitions>() is { } definitions)
            {
                var current = await definitions.CurrentAsync(tenant, cancellationToken).ConfigureAwait(false);
                existingCodes = current.Select(d => d.Code).ToHashSet(StringComparer.Ordinal);
            }
        }

        foreach (var (code, title, capability, verbClass, source) in DevelopmentDemoData.DefaultWorkQueues)
        {
            if (existingCodes.Contains(code))
            {
                continue;
            }

            if (await EnsureRecordAsync(
                    new DefineWorkQueue(
                        tenant,
                        ids.WorkQueueDefinition(code),
                        code,
                        title,
                        capability,
                        verbClass,
                        source,
                        ids.Key($"work-queue:{code}"),
                        Reason),
                    cancellationToken).ConfigureAwait(false) is { } err)
            {
                return err;
            }
        }

        return null;
    }

    private async Task<KernelError?> EnsureRecordAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
    {
        var result = await SendAsOperatorAsync(command, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess || result.Error?.Code == KernelErrors.AlreadyExists.Code)
        {
            return null;
        }

        return result.Error!;
    }

    private async Task<CommandResult<TResult>> SendAsOperatorAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ServiceActorScope>().Actor = KernelAuthorityOptions.TrialOnboardingOperator;
        return await services.GetRequiredService<IKernelCommandSender>().SendAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 2595, Level = LogLevel.Information, Message = "Trial workspace {TenantCode} ({TenantId}) ready (NewlyProvisioned={NewlyProvisioned}, Tier={Tier})")]
    private static partial void LogTrialProvisioned(ILogger logger, string tenantCode, TenantId tenantId, bool newlyProvisioned, CommercialTier tier);

    [GeneratedRegex("^[A-Z]{2,8}$", RegexOptions.CultureInvariant)]
    private static partial Regex ResidencyPattern();

    private sealed class TrialIds(string subject)
    {
        public string TenantCode { get; } = DeriveTenantCode(subject);

        public string OrganizationCode => $"ORG-{TenantCode["trial-".Length..].ToUpperInvariant()}";

        public TenantId Tenant => TenantId.FromGuid(Derive("tenant", "primary"));

        public OrganizationId Organization => OrganizationId.FromGuid(Derive("organization", "sponsor"));

        public PersonId Person => PersonId.FromGuid(Derive("person", "founder"));

        public TenantMembershipId Membership => TenantMembershipId.FromGuid(Derive("membership", "founder"));

        public SubscriptionId Subscription => SubscriptionId.FromGuid(Derive("subscription", "trial"));

        public RoleId Role(string code) => RoleId.FromGuid(Derive("role", code));

        public GrantId Grant(string name) => GrantId.FromGuid(Derive("grant", name));

        public ProductEntitlementId Entitlement(ProductFamily family) =>
            ProductEntitlementId.FromGuid(Derive("entitlement", family.ToString()));

        public WorkQueueDefinitionId WorkQueueDefinition(string code) =>
            WorkQueueDefinitionId.FromGuid(Derive("work-queue-definition", code));

        public string Key(string step) => $"trial-onboarding:{TenantCode}:{step}";

        private Guid Derive(string kind, string name)
        {
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-trial-onboarding:{subject}:{kind}:{name}"), hash);
            var bytes = hash[..16];
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            return new Guid(bytes, bigEndian: true);
        }
    }
}

/// <summary>Validated inputs to <see cref="TrialOnboardingService"/>.</summary>
public sealed record TrialOnboardingInput(string OrganizationName, string? Residency = null, string? Tier = null);

/// <summary>Result of provisioning or replaying a self-service trial workspace.</summary>
public sealed record TrialOnboardingOutcome(
    bool NewlyProvisioned,
    TenantId TenantId,
    string TenantCode,
    OrganizationId OrganizationId,
    PersonId PersonId,
    TenantMembershipId MembershipId,
    RoleId AdminRoleId,
    GrantId AdminGrantId,
    IReadOnlyList<string> AssignedRoles,
    TenantEntitlementSnapshot Entitlements);
