using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Host.Onboarding;

/// <summary>
/// HTTP endpoint for self-service trial workspace provisioning (<c>POST /api/v1/kernel/onboarding/trial</c>).
/// Intentionally mapped on <see cref="KernelApiEndpointRouteBuilderExtensions.MapKernelApi"/> outside
/// <see cref="KernelTenantFilter"/> so newly registered OIDC callers who lack an active <c>tenant_id</c> claim can
/// bootstrap their isolated trial workspace, receive Keycloak claim writeback, and refresh their access token.
/// </summary>
public static class TrialOnboardingEndpoints
{
    /// <summary>Maps <c>POST /onboarding/trial</c> on the kernel route group without <see cref="KernelTenantFilter"/>.</summary>
    public static RouteGroupBuilder MapTrialOnboarding(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        kernel.MapPost("/onboarding/trial", BootstrapTrialAsync)
            .AllowExpiredTenantWrite()
            .WithName("KernelBootstrapTrialWorkspace")
            .WithSummary("Provisions an isolated self-service trial workspace for an authenticated OIDC caller.")
            .WithDescription(
                "Mapped outside KernelTenantFilter for signed-in callers without an active tenant_id claim. "
                + "Idempotently provisions Tenant, sponsor Organization, pseudonymous Person, TenantMembership, "
                + "Sponsor Admin and Sponsor Designer Roles/Grants, and a Trialing Subscription with Blueprint + Capture "
                + "entitlements, then writes tenant_id and initial roles back to Keycloak.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelErrors(
            [
                .. KernelErrors.CommandPipeline,
                CommercialErrors.OnboardingCallerInvalid,
                CommercialErrors.TierInvalid,
                CommercialErrors.TrialWindowInvalid,
                CommercialErrors.SubscriptionRequired,
                CommercialErrors.EntitlementRequired,
                CommercialErrors.IdentityProviderSyncFailed,
                IdentityErrors.CodeRequired,
                IdentityErrors.NameRequired,
                IdentityErrors.ResidencyInvalid,
                IdentityErrors.TenantUnknown,
                IdentityErrors.PersonRequired,
                IdentityErrors.MembershipRequired,
                StudyErrors.CodeRequired,
                StudyErrors.NameRequired,
                StudyErrors.CountryInvalid,
                StudyErrors.InstitutionUnknown,
                StudyErrors.SiteCodeTaken,
                AuthorityErrors.RoleCodeInvalid,
                AuthorityErrors.RoleNameInvalid,
                AuthorityErrors.RoleCodeTaken,
                AuthorityErrors.CapabilityInvalid,
                AuthorityErrors.GrantTargetInvalid,
                AuthorityErrors.ScopeInvalid,
                AuthorityErrors.ValidityInvalid,
            ]);

        return kernel;
    }

    internal static async Task<Results<Ok<KernelTrialOnboardingResponse>, ProblemHttpResult>> BootstrapTrialAsync(
        KernelTrialOnboardingRequest? request,
        ICurrentActor currentActor,
        TrialOnboardingService service,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return KernelErrorEndpoints.Problem(IdentityErrors.NameRequired);
        }

        var result = await service.BootstrapAsync(
            currentActor.Actor,
            new TrialOnboardingInput(request.OrganizationName, request.Residency, request.Tier),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? TypedResults.Ok(KernelTrialOnboardingResponse.From(result.Value!))
            : KernelErrorEndpoints.Problem(result.Error!);
    }
}

/// <summary>Body of <c>POST /api/v1/kernel/onboarding/trial</c>.</summary>
/// <param name="OrganizationName">Sponsor or research organization display name (stored on <c>kernel.organizations</c>, never on <c>kernel.people</c>).</param>
/// <param name="Residency">Optional regional data-residency code (2–8 uppercase ASCII letters such as <c>EU</c> or <c>US</c>; null for unrestricted).</param>
/// <param name="Tier">Selected evaluation tier (<c>Individual</c>, <c>Select</c>, or <c>Complete</c>; defaults to <c>Individual</c>).</param>
public sealed record KernelTrialOnboardingRequest(
    string OrganizationName,
    string? Residency = null,
    string? Tier = null);

/// <summary>Response from <c>POST /api/v1/kernel/onboarding/trial</c>.</summary>
public sealed record KernelTrialOnboardingResponse(
    bool NewlyProvisioned,
    string TenantCode,
    string TenantRef,
    string OrganizationRef,
    string SandboxInstitutionRef,
    string SandboxInstitutionCode,
    string SandboxSiteRef,
    string SandboxSiteCode,
    string SandboxSiteName,
    string SandboxSiteCountry,
    string PersonRef,
    string MembershipRef,
    string AdminRoleRef,
    string AdminGrantRef,
    IReadOnlyList<string> AssignedRoles,
    IReadOnlyList<string> AssignedSiteIds,
    bool TokenRefreshRequired,
    KernelEntitlementsResponse Entitlements)
{
    internal static KernelTrialOnboardingResponse From(TrialOnboardingOutcome outcome) =>
        new(
            outcome.NewlyProvisioned,
            outcome.TenantCode,
            NodeRef.For(outcome.TenantId).ToString(),
            NodeRef.For(outcome.OrganizationId).ToString(),
            NodeRef.For(outcome.SandboxInstitutionId).ToString(),
            outcome.SandboxInstitutionCode,
            NodeRef.For(outcome.SandboxSiteId).ToString(),
            outcome.SandboxSiteCode,
            outcome.SandboxSiteName,
            outcome.SandboxSiteCountry,
            NodeRef.For(outcome.PersonId).ToString(),
            NodeRef.For(outcome.MembershipId).ToString(),
            NodeRef.For(outcome.AdminRoleId).ToString(),
            NodeRef.For(outcome.AdminGrantId).ToString(),
            outcome.AssignedRoles,
            [outcome.SandboxSiteCode],
            TokenRefreshRequired: true,
            KernelEntitlementsResponse.From(outcome.Entitlements));
}
