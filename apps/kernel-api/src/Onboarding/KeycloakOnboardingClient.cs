using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Kernel.Host.Onboarding;

/// <summary>
/// Writes trial workspace claims (<c>tenant_id</c>, <c>study_ids</c>) and initial realm roles back to the OIDC
/// identity provider so a subsequent token refresh resolves the caller's newly provisioned kernel tenant.
/// </summary>
public interface IKeycloakOnboardingClient
{
    /// <summary>Assigns <paramref name="tenantCode"/> and <paramref name="realmRoles"/> to <paramref name="subjectId"/>.</summary>
    Task AssignTrialWorkspaceAsync(
        string subjectId,
        string tenantCode,
        IReadOnlyList<string> realmRoles,
        CancellationToken cancellationToken);
}

/// <summary>Configuration for Keycloak Admin REST writeback during trial workspace bootstrap.</summary>
public sealed class KeycloakAdminOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "KeycloakAdmin";

    /// <summary>Base URL of the Keycloak server; empty disables remote HTTP writeback.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Target Cadence realm containing clinical users and realm roles.</summary>
    public string Realm { get; set; } = "cadence";

    /// <summary>Realm used to authenticate the admin operator.</summary>
    public string AdminRealm { get; set; } = "master";

    /// <summary>OIDC client used for administrative token acquisition.</summary>
    public string ClientId { get; set; } = "admin-cli";

    /// <summary>Keycloak admin username.</summary>
    public string AdminUsername { get; set; } = "admin";

    /// <summary>Keycloak admin password.</summary>
    public string? AdminPassword { get; set; }
}

/// <summary>Raised when Keycloak Admin REST synchronization fails.</summary>
public sealed class KeycloakOnboardingException : Exception
{
    public KeycloakOnboardingException()
    {
    }

    public KeycloakOnboardingException(string message)
        : base(message)
    {
    }

    public KeycloakOnboardingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>HTTP implementation of <see cref="IKeycloakOnboardingClient"/> over the Keycloak 26 Admin REST API.</summary>
internal sealed partial class HttpKeycloakOnboardingClient(
    HttpClient httpClient,
    IOptions<KeycloakAdminOptions> options,
    ILogger<HttpKeycloakOnboardingClient> logger) : IKeycloakOnboardingClient
{
    public async Task AssignTrialWorkspaceAsync(
        string subjectId,
        string tenantCode,
        IReadOnlyList<string> realmRoles,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantCode);
        ArgumentNullException.ThrowIfNull(realmRoles);

        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            LogWritebackSkipped(logger, subjectId, tenantCode);
            return;
        }

        string baseUrl = config.BaseUrl.TrimEnd('/');
        try
        {
            string accessToken = await AcquireAdminTokenAsync(baseUrl, config, cancellationToken).ConfigureAwait(false);
            await EnsureUnmanagedAttributesEnabledAsync(baseUrl, config.Realm, accessToken, cancellationToken).ConfigureAwait(false);
            await UpdateUserTenantAttributesAsync(baseUrl, config.Realm, accessToken, subjectId, tenantCode, cancellationToken).ConfigureAwait(false);
            await AssignRealmRolesAsync(baseUrl, config.Realm, accessToken, subjectId, realmRoles, cancellationToken).ConfigureAwait(false);
            LogWritebackCompleted(logger, subjectId, tenantCode);
        }
        catch (KeycloakOnboardingException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            LogWritebackFailed(logger, ex, subjectId, tenantCode);
            throw new KeycloakOnboardingException("Failed to synchronize trial workspace attributes with Keycloak.", ex);
        }
    }

    private async Task<string> AcquireAdminTokenAsync(
        string baseUrl,
        KeycloakAdminOptions config,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/realms/{Uri.EscapeDataString(config.AdminRealm)}/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("client_id", config.ClientId),
                new KeyValuePair<string, string>("username", config.AdminUsername),
                new KeyValuePair<string, string>("password", config.AdminPassword ?? string.Empty),
            ]),
        };

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new KeycloakOnboardingException($"Keycloak admin token endpoint returned HTTP {(int)response.StatusCode}.");
        }

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) as JsonObject;
        string? token = payload?["access_token"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new KeycloakOnboardingException("Keycloak admin token response omitted access_token.");
        }

        return token;
    }

    private async Task EnsureUnmanagedAttributesEnabledAsync(
        string baseUrl,
        string realm,
        string accessToken,
        CancellationToken cancellationToken)
    {
        string profileUrl = $"{baseUrl}/admin/realms/{Uri.EscapeDataString(realm)}/users/profile";
        using var getRequest = Authorized(HttpMethod.Get, profileUrl, accessToken);
        using var getResponse = await httpClient.SendAsync(getRequest, cancellationToken).ConfigureAwait(false);
        if (!getResponse.IsSuccessStatusCode)
        {
            return;
        }

        if (JsonNode.Parse(await getResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) is not JsonObject profile)
        {
            return;
        }

        if (profile["unmanagedAttributePolicy"]?.GetValue<string>() is { Length: > 0 })
        {
            return;
        }

        profile["unmanagedAttributePolicy"] = "ENABLED";
        using var putRequest = AuthorizedJson(HttpMethod.Put, profileUrl, accessToken, profile);
        using var putResponse = await httpClient.SendAsync(putRequest, cancellationToken).ConfigureAwait(false);
        if (!putResponse.IsSuccessStatusCode)
        {
            throw new KeycloakOnboardingException($"Keycloak user profile update returned HTTP {(int)putResponse.StatusCode}.");
        }
    }

    private async Task UpdateUserTenantAttributesAsync(
        string baseUrl,
        string realm,
        string accessToken,
        string subjectId,
        string tenantCode,
        CancellationToken cancellationToken)
    {
        string userUrl = $"{baseUrl}/admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(subjectId)}";
        using var getRequest = Authorized(HttpMethod.Get, userUrl, accessToken);
        using var getResponse = await httpClient.SendAsync(getRequest, cancellationToken).ConfigureAwait(false);
        if (!getResponse.IsSuccessStatusCode)
        {
            throw new KeycloakOnboardingException($"Keycloak user lookup returned HTTP {(int)getResponse.StatusCode}.");
        }

        if (JsonNode.Parse(await getResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) is not JsonObject user)
        {
            throw new KeycloakOnboardingException("Keycloak user representation was invalid JSON.");
        }

        if (user["attributes"] is not JsonObject attributes)
        {
            attributes = new JsonObject();
            user["attributes"] = attributes;
        }

        attributes["tenant_id"] = new JsonArray(tenantCode);
        attributes["study_ids"] = new JsonArray("*");

        using var putRequest = AuthorizedJson(HttpMethod.Put, userUrl, accessToken, user);
        using var putResponse = await httpClient.SendAsync(putRequest, cancellationToken).ConfigureAwait(false);
        if (!putResponse.IsSuccessStatusCode)
        {
            throw new KeycloakOnboardingException($"Keycloak user attribute update returned HTTP {(int)putResponse.StatusCode}.");
        }
    }

    private async Task AssignRealmRolesAsync(
        string baseUrl,
        string realm,
        string accessToken,
        string subjectId,
        IReadOnlyList<string> realmRoles,
        CancellationToken cancellationToken)
    {
        if (realmRoles.Count == 0)
        {
            return;
        }

        var representations = new JsonArray();
        foreach (string roleName in realmRoles)
        {
            string roleUrl = $"{baseUrl}/admin/realms/{Uri.EscapeDataString(realm)}/roles/{Uri.EscapeDataString(roleName)}";
            using var roleRequest = Authorized(HttpMethod.Get, roleUrl, accessToken);
            using var roleResponse = await httpClient.SendAsync(roleRequest, cancellationToken).ConfigureAwait(false);
            if (!roleResponse.IsSuccessStatusCode)
            {
                throw new KeycloakOnboardingException($"Keycloak role lookup for '{roleName}' returned HTTP {(int)roleResponse.StatusCode}.");
            }

            if (JsonNode.Parse(await roleResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) is JsonObject roleObj
                && roleObj["id"]?.GetValue<string>() is { Length: > 0 } id
                && roleObj["name"]?.GetValue<string>() is { Length: > 0 } name)
            {
                representations.Add(new JsonObject
                {
                    ["id"] = id,
                    ["name"] = name,
                });
            }
        }

        if (representations.Count == 0)
        {
            return;
        }

        string mappingUrl = $"{baseUrl}/admin/realms/{Uri.EscapeDataString(realm)}/users/{Uri.EscapeDataString(subjectId)}/role-mappings/realm";
        using var postRequest = AuthorizedJson(HttpMethod.Post, mappingUrl, accessToken, representations);
        using var postResponse = await httpClient.SendAsync(postRequest, cancellationToken).ConfigureAwait(false);
        if (!postResponse.IsSuccessStatusCode)
        {
            throw new KeycloakOnboardingException($"Keycloak realm role assignment returned HTTP {(int)postResponse.StatusCode}.");
        }
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static HttpRequestMessage AuthorizedJson(HttpMethod method, string url, string accessToken, JsonNode payload)
    {
        var request = Authorized(method, url, accessToken);
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        return request;
    }

    [LoggerMessage(EventId = 2590, Level = LogLevel.Debug, Message = "KeycloakAdmin:BaseUrl is unset; skipped external claim writeback for subject {SubjectId} -> {TenantCode}")]
    private static partial void LogWritebackSkipped(ILogger logger, string subjectId, string tenantCode);

    [LoggerMessage(EventId = 2591, Level = LogLevel.Information, Message = "Assigned trial tenant {TenantCode} and initial roles in Keycloak for subject {SubjectId}")]
    private static partial void LogWritebackCompleted(ILogger logger, string subjectId, string tenantCode);

    [LoggerMessage(EventId = 2592, Level = LogLevel.Warning, Message = "Keycloak writeback failed for subject {SubjectId} -> {TenantCode}")]
    private static partial void LogWritebackFailed(ILogger logger, Exception exception, string subjectId, string tenantCode);
}

/// <summary>DI registration for Keycloak trial onboarding writeback.</summary>
public static class KeycloakOnboardingServiceCollectionExtensions
{
    /// <summary>Named <see cref="HttpClient"/> used for Keycloak Admin REST requests.</summary>
    public const string HttpClientName = "KeycloakOnboarding";

    public static IServiceCollection AddKeycloakOnboarding(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<KeycloakAdminOptions>(configuration.GetSection(KeycloakAdminOptions.SectionName));
        services.AddHttpClient<HttpKeycloakOnboardingClient>(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
        services.TryAddTransient<IKeycloakOnboardingClient>(sp => sp.GetRequiredService<HttpKeycloakOnboardingClient>());
        return services;
    }
}
