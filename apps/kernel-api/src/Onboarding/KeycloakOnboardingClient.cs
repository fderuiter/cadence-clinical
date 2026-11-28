using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Tenancy;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Kernel.Host.Onboarding;

/// <summary>
/// Writes trial workspace claims (<c>tenant_id</c>, explicit <c>site_id</c>, explicit <c>study_ids</c>) and initial
/// realm roles back to the OIDC identity provider (<c>ADMIN_EDIT</c> user-profile attributes) so a subsequent token
/// refresh resolves the caller's newly provisioned kernel tenant without granting global wildcard study access (<c>*</c>).
/// </summary>
public interface IKeycloakOnboardingClient : IKernelIdentityScopeSynchronizer
{
    /// <summary>
    /// Assigns <paramref name="tenantCode"/>, <paramref name="realmRoles"/>, explicit <paramref name="siteCodes"/>,
    /// and explicit <paramref name="studyCodes"/> to <paramref name="subjectId"/>.
    /// </summary>
    Task AssignTrialWorkspaceAsync(
        string subjectId,
        string tenantCode,
        IReadOnlyList<string> realmRoles,
        IReadOnlyList<string> siteCodes,
        IReadOnlyList<string> studyCodes,
        CancellationToken cancellationToken);
}

/// <summary>Configuration for Keycloak Admin REST writeback during trial workspace bootstrap.</summary>
public sealed class KeycloakAdminOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "KeycloakAdmin";

    /// <summary>Base URL of the Keycloak server; required for fail-closed Admin REST synchronization.</summary>
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
        IReadOnlyList<string> siteCodes,
        IReadOnlyList<string> studyCodes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantCode);
        ArgumentNullException.ThrowIfNull(realmRoles);
        ArgumentNullException.ThrowIfNull(siteCodes);
        ArgumentNullException.ThrowIfNull(studyCodes);

        var config = options.Value;
        if (!IsConfigured(config))
        {
            LogWritebackMisconfigured(logger, subjectId, tenantCode);
            throw new KeycloakOnboardingException(
                "KeycloakAdmin BaseUrl, realm, client_id, and admin credentials must be configured for trial workspace synchronization.");
        }

        string baseUrl = config.BaseUrl!.Trim().TrimEnd('/');
        try
        {
            string accessToken = await AcquireAdminTokenAsync(baseUrl, config, cancellationToken).ConfigureAwait(false);
            await UpdateUserTenantAttributesAsync(
                baseUrl,
                config.Realm,
                accessToken,
                subjectId,
                tenantCode,
                siteCodes,
                studyCodes,
                preserveExistingTenantScopes: true,
                cancellationToken).ConfigureAwait(false);
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

    public async Task GrantStudyScopeAsync(
        Actor caller,
        TenantId tenantId,
        string? tenantCode,
        string studyCode,
        CancellationToken cancellationToken = default)
    {
        if (caller is not { Kind: ActorKind.Person, Id.Length: > 0 } || string.IsNullOrWhiteSpace(studyCode))
        {
            return;
        }

        string trimmedStudy = studyCode.Trim();
        if (string.Equals(trimmedStudy, "*", StringComparison.Ordinal))
        {
            return;
        }

        var config = options.Value;
        if (!IsConfigured(config))
        {
            LogStudyScopeSkipped(logger, caller.Id, trimmedStudy);
            return;
        }

        string baseUrl = config.BaseUrl!.Trim().TrimEnd('/');
        try
        {
            string accessToken = await AcquireAdminTokenAsync(baseUrl, config, cancellationToken).ConfigureAwait(false);
            await AppendUserStudyScopeAsync(baseUrl, config.Realm, accessToken, caller.Id, tenantCode, trimmedStudy, cancellationToken).ConfigureAwait(false);
            LogStudyScopeSynced(logger, caller.Id, trimmedStudy);
        }
        catch (Exception ex) when (ex is KeycloakOnboardingException or HttpRequestException or TaskCanceledException or JsonException)
        {
            LogStudyScopeSyncFailed(logger, ex, caller.Id, trimmedStudy);
        }
    }

    public async Task GrantSiteScopeAsync(
        Actor caller,
        TenantId tenantId,
        string? tenantCode,
        string siteCode,
        CancellationToken cancellationToken = default)
    {
        if (caller is not { Kind: ActorKind.Person, Id.Length: > 0 } || string.IsNullOrWhiteSpace(siteCode))
        {
            return;
        }

        string trimmedSite = siteCode.Trim();
        if (string.Equals(trimmedSite, "*", StringComparison.Ordinal))
        {
            return;
        }

        var config = options.Value;
        if (!IsConfigured(config))
        {
            LogSiteScopeSkipped(logger, caller.Id, trimmedSite);
            return;
        }

        string baseUrl = config.BaseUrl!.Trim().TrimEnd('/');
        try
        {
            string accessToken = await AcquireAdminTokenAsync(baseUrl, config, cancellationToken).ConfigureAwait(false);
            await AppendUserSiteScopeAsync(baseUrl, config.Realm, accessToken, caller.Id, tenantCode, trimmedSite, cancellationToken).ConfigureAwait(false);
            LogSiteScopeSynced(logger, caller.Id, trimmedSite);
        }
        catch (Exception ex) when (ex is KeycloakOnboardingException or HttpRequestException or TaskCanceledException or JsonException)
        {
            LogSiteScopeSyncFailed(logger, ex, caller.Id, trimmedSite);
        }
    }

    private static bool IsConfigured(KeycloakAdminOptions config) =>
        !string.IsNullOrWhiteSpace(config.BaseUrl)
        && Uri.TryCreate(config.BaseUrl.Trim(), UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(config.Realm)
        && !string.IsNullOrWhiteSpace(config.AdminRealm)
        && !string.IsNullOrWhiteSpace(config.ClientId)
        && !string.IsNullOrWhiteSpace(config.AdminUsername)
        && !string.IsNullOrWhiteSpace(config.AdminPassword);

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
                new KeyValuePair<string, string>("password", config.AdminPassword!),
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

    private async Task UpdateUserTenantAttributesAsync(
        string baseUrl,
        string realm,
        string accessToken,
        string subjectId,
        string tenantCode,
        IReadOnlyList<string> siteCodes,
        IReadOnlyList<string> studyCodes,
        bool preserveExistingTenantScopes,
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

        bool sameTenant = preserveExistingTenantScopes
            && ReadAttributeStrings(attributes, "tenant_id")
                .Any(existing => string.Equals(existing, tenantCode, StringComparison.Ordinal));

        var effectiveSites = MergeExplicitScopeValues(
            sameTenant ? ReadAttributeStrings(attributes, "site_id") : [],
            siteCodes);
        var effectiveStudies = MergeExplicitScopeValues(
            sameTenant ? ReadAttributeStrings(attributes, "study_ids") : [],
            studyCodes);

        attributes["tenant_id"] = new JsonArray(tenantCode);
        WriteOrRemoveScopeAttribute(attributes, "site_id", effectiveSites);
        WriteOrRemoveScopeAttribute(attributes, "study_ids", effectiveStudies);

        using var putRequest = AuthorizedJson(HttpMethod.Put, userUrl, accessToken, user);
        using var putResponse = await httpClient.SendAsync(putRequest, cancellationToken).ConfigureAwait(false);
        if (!putResponse.IsSuccessStatusCode)
        {
            throw new KeycloakOnboardingException($"Keycloak user attribute update returned HTTP {(int)putResponse.StatusCode}.");
        }
    }

    private Task AppendUserStudyScopeAsync(
        string baseUrl,
        string realm,
        string accessToken,
        string subjectId,
        string? tenantCode,
        string studyCode,
        CancellationToken cancellationToken) =>
        AppendUserScopeAttributeAsync(
            baseUrl,
            realm,
            accessToken,
            subjectId,
            tenantCode,
            attributeKey: "study_ids",
            scopeValue: studyCode,
            cancellationToken);

    private Task AppendUserSiteScopeAsync(
        string baseUrl,
        string realm,
        string accessToken,
        string subjectId,
        string? tenantCode,
        string siteCode,
        CancellationToken cancellationToken) =>
        AppendUserScopeAttributeAsync(
            baseUrl,
            realm,
            accessToken,
            subjectId,
            tenantCode,
            attributeKey: "site_id",
            scopeValue: siteCode,
            cancellationToken);

    private async Task AppendUserScopeAttributeAsync(
        string baseUrl,
        string realm,
        string accessToken,
        string subjectId,
        string? tenantCode,
        string attributeKey,
        string scopeValue,
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

        var existingTenants = ReadAttributeStrings(attributes, "tenant_id");
        if (existingTenants.Count > 0 && !string.IsNullOrWhiteSpace(tenantCode)
            && !existingTenants.Any(t => string.Equals(t, tenantCode.Trim(), StringComparison.Ordinal)))
        {
            return;
        }

        if (existingTenants.Count == 0 && !string.IsNullOrWhiteSpace(tenantCode))
        {
            attributes["tenant_id"] = new JsonArray(tenantCode.Trim());
        }

        var mergedValues = MergeExplicitScopeValues(ReadAttributeStrings(attributes, attributeKey), [scopeValue]);
        WriteOrRemoveScopeAttribute(attributes, attributeKey, mergedValues);

        using var putRequest = AuthorizedJson(HttpMethod.Put, userUrl, accessToken, user);
        using var putResponse = await httpClient.SendAsync(putRequest, cancellationToken).ConfigureAwait(false);
        if (!putResponse.IsSuccessStatusCode)
        {
            throw new KeycloakOnboardingException($"Keycloak user attribute update returned HTTP {(int)putResponse.StatusCode}.");
        }
    }

    private static List<string> ReadAttributeStrings(JsonObject attributes, string key)
    {
        var results = new List<string>();
        if (attributes[key] is JsonArray array)
        {
            foreach (var node in array)
            {
                if (node?.GetValue<string>() is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value))
                {
                    results.Add(value.Trim());
                }
            }
        }
        else if (attributes[key] is JsonValue scalar
            && scalar.TryGetValue<string>(out var single)
            && !string.IsNullOrWhiteSpace(single))
        {
            results.Add(single.Trim());
        }

        return results;
    }

    private static List<string> MergeExplicitScopeValues(IEnumerable<string> existing, IEnumerable<string> incoming)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        foreach (string candidate in existing.Concat(incoming))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            string trimmed = candidate.Trim();
            if (string.Equals(trimmed, "*", StringComparison.Ordinal))
            {
                continue;
            }

            if (seen.Add(trimmed))
            {
                ordered.Add(trimmed);
            }
        }

        return ordered;
    }

    private static void WriteOrRemoveScopeAttribute(JsonObject attributes, string key, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            attributes.Remove(key);
            return;
        }

        var array = new JsonArray();
        foreach (string value in values)
        {
            array.Add(value);
        }

        attributes[key] = array;
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

    [LoggerMessage(EventId = 2590, Level = LogLevel.Warning, Message = "KeycloakAdmin configuration is incomplete; refusing claim writeback for subject {SubjectId} -> {TenantCode}")]
    private static partial void LogWritebackMisconfigured(ILogger logger, string subjectId, string tenantCode);

    [LoggerMessage(EventId = 2591, Level = LogLevel.Information, Message = "Assigned trial tenant {TenantCode}, sandbox site scope, and initial roles in Keycloak for subject {SubjectId}")]
    private static partial void LogWritebackCompleted(ILogger logger, string subjectId, string tenantCode);

    [LoggerMessage(EventId = 2592, Level = LogLevel.Warning, Message = "Keycloak writeback failed for subject {SubjectId} -> {TenantCode}")]
    private static partial void LogWritebackFailed(ILogger logger, Exception exception, string subjectId, string tenantCode);

    [LoggerMessage(EventId = 2593, Level = LogLevel.Debug, Message = "KeycloakAdmin:BaseUrl is unset; skipped explicit study_ids sync for subject {SubjectId} -> {StudyCode}")]
    private static partial void LogStudyScopeSkipped(ILogger logger, string subjectId, string studyCode);

    [LoggerMessage(EventId = 2594, Level = LogLevel.Information, Message = "Appended explicit study scope {StudyCode} in Keycloak for subject {SubjectId}")]
    private static partial void LogStudyScopeSynced(ILogger logger, string subjectId, string studyCode);

    [LoggerMessage(EventId = 2596, Level = LogLevel.Warning, Message = "Failed to append explicit study scope {StudyCode} in Keycloak for subject {SubjectId}")]
    private static partial void LogStudyScopeSyncFailed(ILogger logger, Exception exception, string subjectId, string studyCode);

    [LoggerMessage(EventId = 2597, Level = LogLevel.Debug, Message = "KeycloakAdmin:BaseUrl is unset; skipped explicit site_id sync for subject {SubjectId} -> {SiteCode}")]
    private static partial void LogSiteScopeSkipped(ILogger logger, string subjectId, string siteCode);

    [LoggerMessage(EventId = 2598, Level = LogLevel.Information, Message = "Appended explicit site scope {SiteCode} in Keycloak for subject {SubjectId}")]
    private static partial void LogSiteScopeSynced(ILogger logger, string subjectId, string siteCode);

    [LoggerMessage(EventId = 2599, Level = LogLevel.Warning, Message = "Failed to append explicit site scope {SiteCode} in Keycloak for subject {SubjectId}")]
    private static partial void LogSiteScopeSyncFailed(ILogger logger, Exception exception, string subjectId, string siteCode);
}

/// <summary>DI registration for Keycloak trial onboarding and explicit scope writeback.</summary>
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
        services.TryAddTransient<IKernelIdentityScopeSynchronizer>(sp => sp.GetRequiredService<IKeycloakOnboardingClient>());
        return services;
    }
}
