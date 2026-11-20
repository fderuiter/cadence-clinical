using System;
using System.Net.Http;

using Cadence.Kernel.Modules;
using Cadence.Kernel.Time;
using Cadence.Modules.Org.Endpoints;
using Cadence.Modules.Org.Provisioning;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cadence.Modules.Org;

/// <summary>
/// Registers and maps the Organization Administration module (<c>org</c>) inside the kernel composition host
/// (ADR-2610, ADR-2611).
/// </summary>
public static class OrgModule
{
    /// <summary>Module identifier registered with the kernel composition host.</summary>
    public const string ModuleName = "org";

    /// <summary>Testing host environment name.</summary>
    public const string TestingEnvironment = "Testing";

    /// <summary>
    /// Registers the Organization Administration module and its <see cref="IOidcUserProvisioner"/>.
    /// Uses <see cref="KeycloakOidcUserProvisioner"/> when Keycloak Admin credentials are configured, or
    /// <see cref="InMemoryOidcUserProvisioner"/> in <c>Development</c>/<c>Testing</c> when no live admin endpoint is configured.
    /// </summary>
    public static IServiceCollection AddCadenceOrgModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = BindOptions(configuration);
        bool isDevOrTest = environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironment);
        if (options.InMemoryProvisioner && !isDevOrTest)
        {
            throw new InvalidOperationException(
                $"Org:InMemoryProvisioner cannot be enabled in the {environment.EnvironmentName} environment.");
        }

        services.AddCadenceModule(ModuleName, typeof(OrgModule).Assembly);
        services.TryAddSingleton(options);
        services.TryAddSingleton(options.Keycloak);

        bool hasKeycloakAdmin = !options.InMemoryProvisioner
            && !string.IsNullOrWhiteSpace(options.Keycloak.AdminBaseUrl)
            && (!string.IsNullOrWhiteSpace(options.Keycloak.ClientSecret)
                || (!string.IsNullOrWhiteSpace(options.Keycloak.AdminUsername)
                    && !string.IsNullOrWhiteSpace(options.Keycloak.AdminPassword)));

        if (hasKeycloakAdmin)
        {
            var baseUri = new Uri(options.Keycloak.AdminBaseUrl!.TrimEnd('/'));
            services.AddHttpClient(nameof(KeycloakOidcUserProvisioner), client =>
            {
                client.BaseAddress = baseUri;
                client.Timeout = TimeSpan.FromSeconds(15);
            });
            services.TryAddSingleton<IOidcUserProvisioner>(sp => new KeycloakOidcUserProvisioner(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(KeycloakOidcUserProvisioner)),
                sp.GetRequiredService<OrgKeycloakOptions>(),
                sp.GetRequiredService<IClock>(),
                sp.GetRequiredService<ILogger<KeycloakOidcUserProvisioner>>()));
        }
        else if (isDevOrTest)
        {
            services.TryAddSingleton<InMemoryOidcUserProvisioner>();
            services.TryAddSingleton<IOidcUserProvisioner>(sp => sp.GetRequiredService<InMemoryOidcUserProvisioner>());
        }
        else
        {
            services.TryAddSingleton<IOidcUserProvisioner, UnconfiguredOidcUserProvisioner>();
        }

        return services;
    }

    private sealed class UnconfiguredOidcUserProvisioner : IOidcUserProvisioner
    {
        public Task<OidcProvisioningResult> ProvisionOrReuseAsync(
            OidcProvisioningRequest request,
            System.Threading.CancellationToken cancellationToken = default) =>
            throw new OidcProvisioningException(OrgErrors.OidcProvisioningFailed);
    }

    /// <summary>Maps the Organization Administration endpoints under <c>/api/v1/org</c>.</summary>
    public static IEndpointRouteBuilder MapOrgModule(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.MapOrgEndpoints();
    }

    internal static OrgOptions BindOptions(IConfiguration configuration)
    {
        var options = configuration.GetSection(OrgOptions.SectionName).Get<OrgOptions>() ?? new OrgOptions();
        var kc = options.Keycloak;
        var hostKeycloak = configuration.GetSection("KeycloakAdmin");

        if (string.IsNullOrWhiteSpace(kc.AdminBaseUrl))
        {
            kc.AdminBaseUrl = FirstNonEmpty(
                hostKeycloak["BaseUrl"],
                configuration["KEYCLOAK_ADMIN_URL"],
                DeriveAdminBaseUrl(configuration["OIDC_AUTHORITY"]));
        }

        if (FirstNonEmpty(
                configuration["Org:Keycloak:Realm"],
                hostKeycloak["Realm"],
                configuration["KEYCLOAK_REALM"],
                DeriveRealmFromAuthority(configuration["OIDC_AUTHORITY"])) is { } realm)
        {
            kc.Realm = realm;
        }

        if (FirstNonEmpty(
                configuration["Org:Keycloak:TokenRealm"],
                hostKeycloak["AdminRealm"],
                configuration["KEYCLOAK_ADMIN_TOKEN_REALM"]) is { } tokenRealm)
        {
            kc.TokenRealm = tokenRealm;
        }

        if (FirstNonEmpty(
                configuration["Org:Keycloak:ClientId"],
                hostKeycloak["ClientId"],
                configuration["KEYCLOAK_ADMIN_CLIENT_ID"]) is { } clientId)
        {
            kc.ClientId = clientId;
        }

        kc.ClientSecret = FirstNonEmpty(kc.ClientSecret, configuration["KEYCLOAK_ADMIN_CLIENT_SECRET"]);
        kc.AdminUsername = FirstNonEmpty(kc.AdminUsername, hostKeycloak["AdminUsername"], configuration["KEYCLOAK_ADMIN_USER"]);
        kc.AdminPassword = FirstNonEmpty(kc.AdminPassword, hostKeycloak["AdminPassword"], configuration["KEYCLOAK_ADMIN_PASSWORD"]);
        kc.RedirectUri = FirstNonEmpty(kc.RedirectUri, configuration["KEYCLOAK_INVITE_REDIRECT_URI"]);
        return options;
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return null;
    }

    private static string? DeriveAdminBaseUrl(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority) || !Uri.TryCreate(authority, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var markerIndex = uri.AbsolutePath.IndexOf("/realms/", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var basePath = uri.AbsolutePath[..markerIndex];
        return $"{uri.Scheme}://{uri.Authority}{basePath}";
    }

    private static string? DeriveRealmFromAuthority(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority) || !Uri.TryCreate(authority, UriKind.Absolute, out var uri))
        {
            return null;
        }

        const string marker = "/realms/";
        var markerIndex = uri.AbsolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var remainder = uri.AbsolutePath[(markerIndex + marker.Length)..].Trim('/');
        var slash = remainder.IndexOf('/', StringComparison.Ordinal);
        var realm = slash >= 0 ? remainder[..slash] : remainder;
        return string.IsNullOrWhiteSpace(realm) ? null : realm;
    }
}
