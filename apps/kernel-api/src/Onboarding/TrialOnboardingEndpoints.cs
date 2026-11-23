using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Host.Onboarding;

/// <summary>
/// HTTP endpoint for self-service trial workspace provisioning (<c>POST /api/v1/kernel/onboarding/trial</c>).
/// Intentionally mapped on <see cref="KernelApiEndpointRouteBuilderExtensions.MapKernelApi"/> outside
/// <see cref="KernelTenantFilter"/> so newly registered OIDC callers who lack an active <c>tenant_id</c> claim and
/// hold no clinical roles or foreign tenant bindings can bootstrap an isolated trial workspace.
/// </summary>
public static class TrialOnboardingEndpoints
{
    /// <summary>Header carrying the caller's command idempotency key (K-TX-3).</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Maps <c>POST /onboarding/trial</c> on the kernel route group without <see cref="KernelTenantFilter"/>.</summary>
    public static RouteGroupBuilder MapTrialOnboarding(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        kernel.MapPost("/onboarding/trial", BootstrapTrialAsync)
            .WithName("KernelBootstrapTrialWorkspace")
            .WithSummary("Provisions an isolated self-service trial workspace for an unbound authenticated OIDC caller.")
            .WithDescription(
                "Mapped outside KernelTenantFilter for signed-in callers without an active tenant_id claim, study/site scope, "
                + "or clinical realm roles. Requires caller Idempotency-Key and signed X-Change-Reason headers.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
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
                AuthorityErrors.RoleCodeInvalid,
                AuthorityErrors.RoleNameInvalid,
                AuthorityErrors.RoleCodeTaken,
                AuthorityErrors.CapabilityInvalid,
                AuthorityErrors.GrantTargetInvalid,
                AuthorityErrors.ScopeInvalid,
                AuthorityErrors.ValidityInvalid,
            ])
            .AllowBaselineProductFamily();

        return kernel;
    }

    internal static async Task<Results<Ok<KernelTrialOnboardingResponse>, ProblemHttpResult>> BootstrapTrialAsync(
        KernelTrialOnboardingRequest? request,
        HttpContext context,
        ICurrentActor currentActor,
        IKernelChangeReason changeReason,
        TrialOnboardingService service,
        CancellationToken cancellationToken)
    {
        if (currentActor.Actor is not { Kind: ActorKind.Person, Id.Length: > 0 } caller
            || HasBoundClaimsOrRoles(context))
        {
            return KernelErrorEndpoints.Problem(CommercialErrors.OnboardingCallerInvalid);
        }

        string? idempotencyKey = context.Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return TypedResults.Problem(
                title: "Missing Idempotency-Key",
                detail: KernelErrors.IdempotencyKeyRequired.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = KernelErrors.IdempotencyKeyRequired.Code });
        }

        string? reason = changeReason.Read(context);
        if (string.IsNullOrWhiteSpace(reason))
        {
            return TypedResults.Problem(
                title: "Missing X-Change-Reason",
                detail: KernelErrors.ReasonForChangeRequired.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = KernelErrors.ReasonForChangeRequired.Code });
        }

        if (request is null)
        {
            return KernelErrorEndpoints.Problem(IdentityErrors.NameRequired);
        }

        var result = await service.BootstrapAsync(
            caller,
            new TrialOnboardingInput(request.OrganizationName, request.Residency, request.Tier, idempotencyKey, reason),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? TypedResults.Ok(KernelTrialOnboardingResponse.From(result.Value!))
            : KernelErrorEndpoints.Problem(result.Error!);
    }

    private static bool HasBoundClaimsOrRoles(HttpContext context)
    {
        if (context.Items[GatewayAuthenticationHandler.PrincipalItemKey] is Principal principal
            && (!string.IsNullOrWhiteSpace(principal.TenantId)
                || !string.IsNullOrWhiteSpace(principal.SponsorId)
                || principal.AssignedSites.Any(static id => !string.IsNullOrWhiteSpace(id))
                || principal.AssignedStudies.Any(static id => !string.IsNullOrWhiteSpace(id))
                || principal.Roles.Count > 0
                || principal.RawRoles.Any(static role => role.Trim().Length > 0 && !IsIgnorableDefaultOidcRole(role.Trim()))))
        {
            return true;
        }

        ClaimsPrincipal user = context.User;
        if (user.Claims.Any(claim =>
                claim.Type is CadenceClaimTypes.TenantId or CadenceClaimTypes.SiteId or CadenceClaimTypes.StudyIds or CadenceClaimTypes.SponsorId
                && !string.IsNullOrWhiteSpace(claim.Value)))
        {
            return true;
        }

        return user.Claims
            .Where(claim => claim.Type is ClaimTypes.Role or "role" or "roles")
            .Select(claim => claim.Value.Trim())
            .Any(role => role.Length > 0 && !IsIgnorableDefaultOidcRole(role));
    }

    private static bool IsIgnorableDefaultOidcRole(string role) =>
        role.Equals("offline_access", StringComparison.OrdinalIgnoreCase)
        || role.Equals("uma_authorization", StringComparison.OrdinalIgnoreCase)
        || role.StartsWith("default-roles-", StringComparison.OrdinalIgnoreCase);
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
    string PersonRef,
    string MembershipRef,
    string AdminRoleRef,
    string AdminGrantRef,
    IReadOnlyList<string> AssignedRoles,
    bool TokenRefreshRequired,
    KernelEntitlementsResponse Entitlements)
{
    internal static KernelTrialOnboardingResponse From(TrialOnboardingOutcome outcome) =>
        new(
            outcome.NewlyProvisioned,
            outcome.TenantCode,
            NodeRef.For(outcome.TenantId).ToString(),
            NodeRef.For(outcome.OrganizationId).ToString(),
            NodeRef.For(outcome.PersonId).ToString(),
            NodeRef.For(outcome.MembershipId).ToString(),
            NodeRef.For(outcome.AdminRoleId).ToString(),
            NodeRef.For(outcome.AdminGrantId).ToString(),
            outcome.AssignedRoles,
            TokenRefreshRequired: true,
            KernelEntitlementsResponse.From(outcome.Entitlements));
}
