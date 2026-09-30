using System;

using Cadence.Kernel.Time;
using Cadence.Security.Audit;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<ReplayCache>();
        services.AddSingleton<SigTokenVerifier>();
        services.AddSingleton<IAuditLogWriter, InMemoryAuditStore>();
        services.AddSingleton(sp => new AuditDigestEngine(
            sp.GetRequiredService<IAuditLogWriter>(),
            options.AuditLogSecretKey ?? sp.GetService<IConfiguration>()?[AuditSecretKey.EnvironmentVariable],
            sp.GetService<IClock>()));

        // Authorization Handlers
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, RoleAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, StudyScopeAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, ChangeJustificationAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, AuditorReadOnlyAuthorizationHandler>();
        // RequirePermission("resource:action") names a policy built on demand from the permission matrix (ADR-2510).
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        services.AddAuthorization(authOptions =>
        {
            // Every endpoint needs an authenticated caller unless it opts out with AllowAnonymous()
            // (health and status probes do).
            authOptions.FallbackPolicy = new AuthorizationPolicyBuilder(GatewayAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .Build();

            authOptions.AddPolicy("RequireStudyScope", policy =>
                policy.Requirements.Add(new StudyScopeRequirement()));

            authOptions.AddPolicy("RequireChangeJustification", policy =>
                policy.Requirements.Add(new ChangeJustificationRequirement()));

            authOptions.AddPolicy("AuditorReadOnly", policy =>
                policy.Requirements.Add(new AuditorReadOnlyRequirement()));
        });

        return services;
    }

    /// <summary>
    /// Adds gateway-signature authentication, authorization (with the authenticated-caller fallback policy)
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
    /// Minimal API extension asserting required study scope access.
    /// </summary>
    public static TBuilder RequireStudyScope<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization("RequireStudyScope");
    }

    /// <summary>
    /// Minimal API extension asserting required change justification reason on write requests.
    /// </summary>
    public static TBuilder RequireChangeJustification<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization("RequireChangeJustification");
    }

    /// <summary>
    /// Minimal API extension asserting required role.
    /// </summary>
    public static TBuilder RequireRole<TBuilder>(this TBuilder builder, params string[] roles) where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(new AuthorizeAttribute { Roles = string.Join(",", roles) });
    }
}
