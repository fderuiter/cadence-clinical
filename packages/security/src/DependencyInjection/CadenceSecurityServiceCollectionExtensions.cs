using System;

using Cadence.Kernel.Time;
using Cadence.Security.Audit;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Security.DependencyInjection;

public class CadenceSecurityOptions
{
    /// <summary>
    /// Shared gateway HMAC secret. When left unset it is read from the <c>GATEWAY_SECRET</c> configuration
    /// key. There is no default: the host fails at startup if neither supplies a secret of at least
    /// <see cref="GatewayAuthenticationOptions.MinimumSecretLength"/> characters.
    /// </summary>
    public string? GatewaySecret { get; set; }

    /// <summary>
    /// GxP audit-trail HMAC key. When left unset it is read from the <c>AUDIT_LOG_SECRET_KEY</c> configuration
    /// key; there is no default, so resolving <see cref="AuditDigestEngine"/> without one throws.
    /// </summary>
    public string? AuditLogSecretKey { get; set; }
}

public static class CadenceSecurityServiceCollectionExtensions
{
    /// <summary>The configuration key (and environment variable) holding the shared gateway secret.</summary>
    public const string GatewaySecretConfigurationKey = "GATEWAY_SECRET"; // pragma: allowlist secret

    /// <summary>
    /// Registers Cadence security engine services, authentication middleware, authorization policies, and audit components.
    /// @req:PRD-SYS-053
    /// </summary>
    public static IServiceCollection AddCadenceSecurity(this IServiceCollection services, Action<CadenceSecurityOptions>? configure = null)
    {
        var options = new CadenceSecurityOptions();
        configure?.Invoke(options);

        services.AddHttpContextAccessor();
        services.AddSingleton(options);

        services.AddAuthentication(GatewayAuthenticationHandler.SchemeName)
            .AddScheme<GatewayAuthenticationOptions, GatewayAuthenticationHandler>(GatewayAuthenticationHandler.SchemeName, null);

        services.AddOptions<GatewayAuthenticationOptions>(GatewayAuthenticationHandler.SchemeName)
            .Configure<IServiceProvider>((authOptions, sp) =>
                authOptions.GatewaySecret ??= options.GatewaySecret ?? sp.GetService<IConfiguration>()?[GatewaySecretConfigurationKey])
            .Validate(
                authOptions => authOptions.GatewaySecret is { Length: >= GatewayAuthenticationOptions.MinimumSecretLength },
                $"{GatewaySecretConfigurationKey} must be set to a secret of at least {GatewayAuthenticationOptions.MinimumSecretLength} characters.")
            .ValidateOnStart();

        services.AddTransient<AuditorWriteGuardMiddleware>();
        // Process-local single use by default; every host with an e-signature route replaces it with the durable
        // store, and outside Development the startup check refuses one that does not (ADR-2515, #5434).
        services.TryAddSingleton<ISigTokenUseStore>(sp => new ReplayCache(sp.GetService<IClock>()));
        services.AddHostedService<SigTokenUseStoreStartupCheck>();
        services.AddSingleton(sp => new SigTokenVerifier(sp.GetRequiredService<ISigTokenUseStore>(), sp.GetService<IClock>()));
        services.AddSingleton<IAuditLogWriter, InMemoryAuditStore>();
        services.AddSingleton(sp => new AuditDigestEngine(
            sp.GetRequiredService<IAuditLogWriter>(),
            options.AuditLogSecretKey ?? sp.GetService<IConfiguration>()?[AuditSecretKey.EnvironmentVariable],
            sp.GetService<IClock>()));

        // Authorization Handlers
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, RoleAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, TenantScopeAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, StudyScopeAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, SiteScopeAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, CallerStudyScopeAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, CallerStudyAndSiteScopeAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, UndeclaredEndpointAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, ChangeJustificationAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, AuditorReadOnlyAuthorizationHandler>();
        // RequirePermission("resource:action") names a policy built on demand from the permission matrix (ADR-2510).
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        services.AddAuthorization(authOptions =>
        {
            // Deny by default (ADR-2516): an endpoint that declares no authorization answers 401 to an unsigned
            // caller and 403 to every signed one. Each route opts in with RequirePermission, RequireSignedInCaller
            // or, for health, status and development API documents only, AllowAnonymous().
            authOptions.FallbackPolicy = new AuthorizationPolicyBuilder(GatewayAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .AddRequirements(new UndeclaredEndpointRequirement())
                .Build();

            authOptions.AddPolicy(CadencePolicies.SignedInCaller, policy => policy
                .AddAuthenticationSchemes(GatewayAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser());

            authOptions.AddPolicy(CadencePolicies.TenantScope, policy => policy
                .AddAuthenticationSchemes(GatewayAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantScopeRequirement()));

            authOptions.AddPolicy(CadencePolicies.StudyScope, policy => policy
                .AddAuthenticationSchemes(GatewayAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantScopeRequirement(), new StudyScopeRequirement()));

            authOptions.AddPolicy(CadencePolicies.SiteScope, policy => policy
                .AddAuthenticationSchemes(GatewayAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantScopeRequirement(), new SiteScopeRequirement()));

            authOptions.AddPolicy(CadencePolicies.CallerStudyScope, policy => policy
                .AddAuthenticationSchemes(GatewayAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantScopeRequirement(), new CallerStudyScopeRequirement()));

            authOptions.AddPolicy(CadencePolicies.CallerStudyAndSiteScope, policy => policy
                .AddAuthenticationSchemes(GatewayAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantScopeRequirement(), new CallerStudyAndSiteScopeRequirement()));

            authOptions.AddPolicy("RequireChangeJustification", policy =>
                policy.Requirements.Add(new ChangeJustificationRequirement()));

            authOptions.AddPolicy("AuditorReadOnly", policy =>
                policy.Requirements.Add(new AuditorReadOnlyRequirement()));
        });

        return services;
    }

    /// <summary>
    /// Adds gateway-signature authentication, authorization (with the deny-by-default fallback policy)
    /// and the auditor write guard to the pipeline.
    /// </summary>
    public static IApplicationBuilder UseCadenceSecurity(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app.UseMiddleware<AuditorWriteGuardMiddleware>();
    }

    /// <summary>
    /// Requires the caller's roles to hold <paramref name="permission"/> (<c>resource:action</c>) in
    /// <see cref="PermissionMatrix"/>; any other authenticated caller gets <c>403</c>. The policy is built by
    /// <see cref="PermissionPolicyProvider"/> (ADR-2510).
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return builder.RequireAuthorization(new AuthorizeAttribute { Policy = PermissionPolicyProvider.PolicyName(permission) });
    }

    /// <summary>
    /// Admits any signed-in caller, for a route whose handler decides access per object, or that reads or computes
    /// nothing a signed-in caller may not see (ADR-2516). <paramref name="reason"/> says which, and is kept on the
    /// endpoint as <see cref="SignedInCallerReason"/> so the route inventory and reviews can read it. Every such
    /// route is also listed, with its reason, in the route authorization test, so a new one needs review.
    /// </summary>
    public static TBuilder RequireSignedInCaller<TBuilder>(this TBuilder builder, string reason) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return builder
            .RequireAuthorization(CadencePolicies.SignedInCaller)
            .WithMetadata(new SignedInCallerReason(reason));
    }

    /// <summary>
    /// Requires the caller to carry a verified active <c>X-Tenant-Id</c> (ADR-2514, PRD-CRF-013).
    /// </summary>
    public static TBuilder RequireTenantScope<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder
            .RequireAuthorization(CadencePolicies.TenantScope)
            .WithMetadata(new EndpointScopeMetadata(EndpointScopeKind.Tenant));

    /// <summary>
    /// Requires the caller to carry a verified active tenant and at least one study assignment (<c>X-Study-Ids</c>),
    /// where object- or payload-level study matching is enforced in the handler via <see cref="ClinicalScopeGuard"/>.
    /// </summary>
    public static TBuilder RequireCallerStudyScope<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder
            .RequireAuthorization(CadencePolicies.CallerStudyScope)
            .WithMetadata(new EndpointScopeMetadata(EndpointScopeKind.CallerStudy));

    /// <summary>
    /// Requires the caller to carry a verified active tenant, at least one study assignment, and (when site-scoped)
    /// at least one site assignment, where object- or payload-level study/site matching is enforced via <see cref="ClinicalScopeGuard"/>.
    /// </summary>
    public static TBuilder RequireCallerStudyAndSiteScope<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder
            .RequireAuthorization(CadencePolicies.CallerStudyAndSiteScope)
            .WithMetadata(new EndpointScopeMetadata(EndpointScopeKind.CallerStudyAndSite));

    /// <summary>
    /// Requires the study named by the route value <paramref name="routeParameter"/> to be one the signed caller is
    /// assigned to (<c>X-Study-Ids</c>, or <c>*</c> for every study). It fails closed: a route without that value, or a
    /// caller with no assignment, gets <c>403</c>. The study id is never read from the query, body or an unsigned
    /// header (ADR-2516). Combine it with <see cref="RequirePermission{TBuilder}"/>: scope narrows a permission and
    /// never grants one.
    /// </summary>
    public static TBuilder RequireStudyScope<TBuilder>(this TBuilder builder, string routeParameter) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeParameter);
        return builder
            .RequireAuthorization(CadencePolicies.StudyScope)
            .WithMetadata(new StudyScopeRouteParameter(routeParameter))
            .WithMetadata(new EndpointScopeMetadata(EndpointScopeKind.RouteStudy, routeParameter));
    }

    /// <summary>
    /// Requires the site named by the route value <paramref name="routeParameter"/> to pass
    /// <see cref="SiteAccessEvaluator.CanAccessSite"/> for the signed caller: a site-scoped role reaches only its signed
    /// sites (ADR-2516). A route without that value gets <c>403</c>.
    /// </summary>
    public static TBuilder RequireSiteScope<TBuilder>(this TBuilder builder, string routeParameter) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeParameter);
        return builder
            .RequireAuthorization(CadencePolicies.SiteScope)
            .WithMetadata(new SiteScopeRouteParameter(routeParameter))
            .WithMetadata(new EndpointScopeMetadata(EndpointScopeKind.RouteSite, routeParameter));
    }

    /// <summary>
    /// Minimal API extension asserting required change justification reason on write requests.
    /// </summary>
    public static TBuilder RequireChangeJustification<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization("RequireChangeJustification");
    }
}
