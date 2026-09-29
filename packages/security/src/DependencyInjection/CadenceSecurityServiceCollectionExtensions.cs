using System;

using Cadence.Security.Audit;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Security.DependencyInjection;

public class CadenceSecurityOptions
{
    public string? GatewaySecret { get; set; }
    public string? AuditLogSecretKey { get; set; }
    public bool RequireGatewaySignature { get; set; } = false;
}

public static class CadenceSecurityServiceCollectionExtensions
{
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
        services.AddSingleton(new GatewayAuthOptions
        {
            GatewaySecret = options.GatewaySecret,
            RequireGatewaySignature = options.RequireGatewaySignature
        });

        services.AddTransient<GatewayAuthMiddleware>();
        services.AddSingleton<ReplayCache>();
        services.AddSingleton<SigTokenVerifier>();
        services.AddSingleton<IAuditLogWriter, InMemoryAuditStore>();
        services.AddSingleton<AuditDigestEngine>();
        services.AddTransient<GxPAuditInterceptor>();

        // Authorization Handlers
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, RoleAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, StudyScopeAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, ChangeJustificationAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, AuditorReadOnlyAuthorizationHandler>();

        services.AddAuthorization(authOptions =>
        {
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
    /// Configures Cadence security middleware pipeline (Gateway HMAC authentication &amp; identity resolution).
    /// </summary>
    public static IApplicationBuilder UseCadenceSecurity(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GatewayAuthMiddleware>();
    }

    /// <summary>
    /// Minimal API extension asserting required permission ("resource:action").
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission) where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(new AuthorizeAttribute { Policy = $"Permission:{permission}" });
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
