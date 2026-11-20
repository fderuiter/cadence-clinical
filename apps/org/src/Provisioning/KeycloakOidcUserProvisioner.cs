using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Time;

using Microsoft.Extensions.Logging;

namespace Cadence.Modules.Org.Provisioning;

/// <summary>
/// Keycloak 26 Admin REST API implementation of <see cref="IOidcUserProvisioner"/> (ADR-2611).
/// Provisions or reuses realm users with <c>tenant_id</c>, <c>study_ids</c>, and <c>site_id</c> attributes,
/// assigns realm roles, and triggers Keycloak's <c>UPDATE_PASSWORD</c> welcome email without logging PII.
/// </summary>
public sealed partial class KeycloakOidcUserProvisioner(
    HttpClient httpClient,
    OrgKeycloakOptions options,
    IClock clock,
    ILogger<KeycloakOidcUserProvisioner> logger) : IOidcUserProvisioner, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedAccessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;
    private volatile bool _unmanagedProfileChecked;

    /// <inheritdoc />
    public void Dispose() => _tokenLock.Dispose();

    /// <inheritdoc />
    public async Task<OidcProvisioningResult> ProvisionOrReuseAsync(OidcProvisioningRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var token = await AcquireAdminTokenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureUnmanagedAttributesEnabledAsync(token, cancellationToken).ConfigureAwait(false);

        var existing = await FindUserByEmailAsync(token, normalizedEmail, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return await ReuseExistingUserAsync(token, existing, request, cancellationToken).ConfigureAwait(false);
        }

        var createdId = await CreateUserAsync(token, normalizedEmail, request, cancellationToken).ConfigureAwait(false);
        if (createdId is null)
        {
            // Concurrent creation race: re-query exact email match and validate tenant ownership.
            var concurrent = await FindUserByEmailAsync(token, normalizedEmail, cancellationToken).ConfigureAwait(false)
                ?? throw new OidcProvisioningException(OrgErrors.OidcProvisioningFailed);
            return await ReuseExistingUserAsync(token, concurrent, request, cancellationToken).ConfigureAwait(false);
        }

        await EnsureRealmRolesAsync(token, createdId.Value, request.RealmRoles, cancellationToken).ConfigureAwait(false);

        bool welcomeSent = false;
        if (options.SendWelcomeEmail)
        {
            await SendWelcomeActionsEmailAsync(token, createdId.Value, cancellationToken).ConfigureAwait(false);
            welcomeSent = true;
        }

        var personId = PersonId.FromGuid(createdId.Value);
        LogUserProvisioned(logger, personId.Value, options.Realm, request.TenantCode, welcomeSent);
        return new OidcProvisioningResult(personId, OidcProvisioningOutcome.Created, welcomeSent);
    }

    private async Task EnsureUnmanagedAttributesEnabledAsync(string token, CancellationToken cancellationToken)
    {
        if (_unmanagedProfileChecked)
        {
            return;
        }

        var profilePath = $"/admin/realms/{Uri.EscapeDataString(options.Realm)}/users/profile";
        using var getReq = CreateAuthorizedRequest(HttpMethod.Get, profilePath, token);
        using var getResp = await httpClient.SendAsync(getReq, cancellationToken).ConfigureAwait(false);
        if (!getResp.IsSuccessStatusCode)
        {
            _unmanagedProfileChecked = true;
            return;
        }

        var raw = await getResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw) || JsonNode.Parse(raw) is not JsonObject profile)
        {
            _unmanagedProfileChecked = true;
            return;
        }

        if (profile["unmanagedAttributePolicy"]?.GetValue<string>() is { Length: > 0 })
        {
            _unmanagedProfileChecked = true;
            return;
        }

        profile["unmanagedAttributePolicy"] = "ENABLED";
        using var putReq = CreateAuthorizedRequest(HttpMethod.Put, profilePath, token);
        putReq.Content = new StringContent(profile.ToJsonString(), Encoding.UTF8, "application/json");
        using var putResp = await httpClient.SendAsync(putReq, cancellationToken).ConfigureAwait(false);
        if (!putResp.IsSuccessStatusCode)
        {
            LogUserProfileUpdateFailed(logger, options.Realm, (int)putResp.StatusCode);
            throw new OidcProvisioningException(OrgErrors.OidcProvisioningFailed);
        }

        _unmanagedProfileChecked = true;
    }

    private async Task<OidcProvisioningResult> ReuseExistingUserAsync(
        string token,
        KeycloakUserRepresentation existing,
        OidcProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(existing.Id, out var userGuid))
        {
            throw new OidcProvisioningException(OrgErrors.OidcProvisioningFailed);
        }

        var attributes = existing.Attributes is not null
            ? new Dictionary<string, List<string>>(existing.Attributes, StringComparer.Ordinal)
            : new Dictionary<string, List<string>>(StringComparer.Ordinal);

        if (attributes.TryGetValue("tenant_id", out var tenantValues)
            && tenantValues is { Count: > 0 }
            && !tenantValues.Contains(request.TenantCode, StringComparer.Ordinal))
        {
            LogCrossTenantBlocked(logger, userGuid, request.TenantCode);
            throw new OidcProvisioningException(OrgErrors.CrossTenantIdentityConflict);
        }

        MergeAttributes(attributes, request.TenantCode, request.StudyCodes, request.SiteCode);
        var updated = existing with
        {
            Enabled = true,
            Attributes = attributes,
        };

        using var updateReq = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/admin/realms/{Uri.EscapeDataString(options.Realm)}/users/{Uri.EscapeDataString(userGuid.ToString("D"))}",
            token);
        updateReq.Content = JsonContent.Create(updated, options: JsonOptions);
        using var updateResp = await httpClient.SendAsync(updateReq, cancellationToken).ConfigureAwait(false);
        if (!updateResp.IsSuccessStatusCode)
        {
            LogAttributeSyncFailed(logger, userGuid, (int)updateResp.StatusCode);
            throw new OidcProvisioningException(OrgErrors.OidcProvisioningFailed);
        }

        await EnsureRealmRolesAsync(token, userGuid, request.RealmRoles, cancellationToken).ConfigureAwait(false);

        var personId = PersonId.FromGuid(userGuid);
        LogUserReused(logger, personId.Value, options.Realm, request.TenantCode);
        return new OidcProvisioningResult(personId, OidcProvisioningOutcome.ReusedExisting, WelcomeEmailSent: false);
    }

    private async Task<Guid?> CreateUserAsync(
        string token,
        string normalizedEmail,
        OidcProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        var attributes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        MergeAttributes(attributes, request.TenantCode, request.StudyCodes, request.SiteCode);

        var payload = new KeycloakUserRepresentation(
            Id: request.KnownTenantPersonId?.Value.ToString("D"),
            Username: normalizedEmail,
            Email: normalizedEmail,
            FirstName: request.GivenName,
            LastName: request.FamilyName,
            Enabled: true,
            EmailVerified: false,
            RequiredActions: ["UPDATE_PASSWORD"],
            Attributes: attributes);

        using var createReq = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/admin/realms/{Uri.EscapeDataString(options.Realm)}/users",
            token);
        createReq.Content = JsonContent.Create(payload, options: JsonOptions);
        using var createResp = await httpClient.SendAsync(createReq, cancellationToken).ConfigureAwait(false);

        if (createResp.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }

        if (!createResp.IsSuccessStatusCode)
        {
            LogUserCreateFailed(logger, options.Realm, request.TenantCode, (int)createResp.StatusCode);
            throw new OidcProvisioningException(OrgErrors.OidcProvisioningFailed);
        }

        if (ExtractUserIdFromLocation(createResp.Headers.Location) is { } locationGuid)
        {
            return locationGuid;
        }

        var lookedUp = await FindUserByEmailAsync(token, normalizedEmail, cancellationToken).ConfigureAwait(false);
        return lookedUp is not null && Guid.TryParse(lookedUp.Id, out var parsed)
            ? parsed
            : throw new OidcProvisioningException(OrgErrors.OidcProvisioningFailed);
    }

    private async Task<KeycloakUserRepresentation?> FindUserByEmailAsync(
        string token,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var path = $"/admin/realms/{Uri.EscapeDataString(options.Realm)}/users?email={Uri.EscapeDataString(normalizedEmail)}&exact=true";
        using var req = CreateAuthorizedRequest(HttpMethod.Get, path, token);
        using var resp = await httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            LogUserLookupFailed(logger, options.Realm, (int)resp.StatusCode);
            throw new OidcProvisioningException(OrgErrors.OidcProvisioningFailed);
        }

        var users = await resp.Content.ReadFromJsonAsync<List<KeycloakUserRepresentation>>(JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        return users.FirstOrDefault(u => string.Equals(u.Email?.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase));
    }

    private async Task EnsureRealmRolesAsync(
        string token,
        Guid userId,
        IReadOnlyList<string> realmRoles,
        CancellationToken cancellationToken)
    {
        if (realmRoles.Count == 0)
        {
            return;
        }

        var toAssign = new List<KeycloakRoleRepresentation>();
        foreach (var roleName in realmRoles.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.Ordinal))
        {
            using var roleReq = CreateAuthorizedRequest(
                HttpMethod.Get,
                $"/admin/realms/{Uri.EscapeDataString(options.Realm)}/roles/{Uri.EscapeDataString(roleName)}",
                token);
            using var roleResp = await httpClient.SendAsync(roleReq, cancellationToken).ConfigureAwait(false);
            if (roleResp.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            if (!roleResp.IsSuccessStatusCode)
            {
                throw new OidcProvisioningException(OrgErrors.OidcProvisioningFailed);
            }

            if (await roleResp.Content.ReadFromJsonAsync<KeycloakRoleRepresentation>(JsonOptions, cancellationToken).ConfigureAwait(false) is { } roleRep)
            {
                toAssign.Add(roleRep);
            }
        }

        if (toAssign.Count == 0)
        {
            return;
        }

        using var mapReq = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/admin/realms/{Uri.EscapeDataString(options.Realm)}/users/{Uri.EscapeDataString(userId.ToString("D"))}/role-mappings/realm",
            token);
        mapReq.Content = JsonContent.Create(toAssign, options: JsonOptions);
        using var mapResp = await httpClient.SendAsync(mapReq, cancellationToken).ConfigureAwait(false);
        if (!mapResp.IsSuccessStatusCode)
        {
            LogRoleMappingFailed(logger, userId, (int)mapResp.StatusCode);
            throw new OidcProvisioningException(OrgErrors.OidcProvisioningFailed);
        }
    }

    private async Task SendWelcomeActionsEmailAsync(string token, Guid userId, CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"lifespan={Math.Max(300, options.ActionLifespanSeconds)}",
        };
        if (!string.IsNullOrWhiteSpace(options.RedirectClientId))
        {
            query.Add($"client_id={Uri.EscapeDataString(options.RedirectClientId)}");
        }

        if (!string.IsNullOrWhiteSpace(options.RedirectUri))
        {
            query.Add($"redirect_uri={Uri.EscapeDataString(options.RedirectUri)}");
        }

        var path = $"/admin/realms/{Uri.EscapeDataString(options.Realm)}/users/{Uri.EscapeDataString(userId.ToString("D"))}/execute-actions-email?{string.Join("&", query)}";
        using var emailReq = CreateAuthorizedRequest(HttpMethod.Put, path, token);
        emailReq.Content = JsonContent.Create(new[] { "UPDATE_PASSWORD" }, options: JsonOptions);
        using var emailResp = await httpClient.SendAsync(emailReq, cancellationToken).ConfigureAwait(false);
        if (!emailResp.IsSuccessStatusCode)
        {
            LogWelcomeEmailFailed(logger, userId, (int)emailResp.StatusCode);
            throw new OidcProvisioningException(OrgErrors.OidcProvisioningFailed);
        }
    }

    private async Task<string> AcquireAdminTokenAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (_cachedAccessToken is { } valid && now < _tokenExpiresAt)
        {
            return valid;
        }

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = clock.UtcNow;
            if (_cachedAccessToken is { } rechecked && now < _tokenExpiresAt)
            {
                return rechecked;
            }

            var form = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["client_id"] = options.ClientId,
            };

            if (!string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                form["grant_type"] = "client_credentials";
                form["client_secret"] = options.ClientSecret;
            }
            else if (!string.IsNullOrWhiteSpace(options.AdminUsername) && !string.IsNullOrWhiteSpace(options.AdminPassword))
            {
                form["grant_type"] = "password";
                form["username"] = options.AdminUsername;
                form["password"] = options.AdminPassword;
            }
            else
            {
                throw new OidcProvisioningException(OrgErrors.OidcProvisioningFailed);
            }

            var tokenPath = $"/realms/{Uri.EscapeDataString(options.TokenRealm)}/protocol/openid-connect/token";
            using var tokenReq = new HttpRequestMessage(HttpMethod.Post, new Uri(tokenPath, UriKind.Relative))
            {
                Content = new FormUrlEncodedContent(form),
            };
            using var tokenResp = await httpClient.SendAsync(tokenReq, cancellationToken).ConfigureAwait(false);
            if (!tokenResp.IsSuccessStatusCode)
            {
                LogAdminTokenFailed(logger, options.TokenRealm, (int)tokenResp.StatusCode);
                throw new OidcProvisioningException(OrgErrors.OidcProvisioningFailed);
            }

            var payload = await tokenResp.Content.ReadFromJsonAsync<KeycloakTokenResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
            if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
            {
                throw new OidcProvisioningException(OrgErrors.OidcProvisioningFailed);
            }

            var lifetimeSeconds = Math.Max(15, payload.ExpiresIn - 30);
            _cachedAccessToken = payload.AccessToken;
            _tokenExpiresAt = clock.UtcNow.AddSeconds(lifetimeSeconds);
            return _cachedAccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string relativePath, string bearerToken)
    {
        var req = new HttpRequestMessage(method, new Uri(relativePath, UriKind.Relative));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return req;
    }

    private static void MergeAttributes(
        Dictionary<string, List<string>> attributes,
        string tenantCode,
        IReadOnlyList<string> studyCodes,
        string? siteCode)
    {
        attributes["tenant_id"] = [tenantCode];

        var studies = attributes.TryGetValue("study_ids", out var existingStudies) && existingStudies is not null
            ? new SortedSet<string>(existingStudies.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.Ordinal)
            : new SortedSet<string>(StringComparer.Ordinal);
        foreach (var study in studyCodes.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            studies.Add(study);
        }

        if (studies.Count > 0)
        {
            attributes["study_ids"] = studies.Contains("*") ? ["*"] : [.. studies];
        }

        if (!string.IsNullOrWhiteSpace(siteCode))
        {
            attributes["site_id"] = [siteCode];
        }
    }

    private static Guid? ExtractUserIdFromLocation(Uri? location)
    {
        if (location is null)
        {
            return null;
        }

        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
        var lastSegment = path.TrimEnd('/').Split('/').LastOrDefault();
        return Guid.TryParse(lastSegment, out var guid) ? guid : null;
    }

    [LoggerMessage(EventId = 2610, Level = LogLevel.Information, Message = "Provisioned OIDC user {PersonId} in realm {Realm} for tenant {TenantCode} (welcomeEmailSent={WelcomeSent})")]
    private static partial void LogUserProvisioned(ILogger logger, Guid personId, string realm, string tenantCode, bool welcomeSent);

    [LoggerMessage(EventId = 2611, Level = LogLevel.Information, Message = "Reused existing OIDC user {PersonId} in realm {Realm} for tenant {TenantCode}")]
    private static partial void LogUserReused(ILogger logger, Guid personId, string realm, string tenantCode);

    [LoggerMessage(EventId = 2612, Level = LogLevel.Warning, Message = "Blocked cross-tenant OIDC invitation reuse for subject {PersonId} into tenant {TenantCode}")]
    private static partial void LogCrossTenantBlocked(ILogger logger, Guid personId, string tenantCode);

    [LoggerMessage(EventId = 2613, Level = LogLevel.Error, Message = "Keycloak user attribute sync failed for {PersonId} with HTTP {StatusCode}")]
    private static partial void LogAttributeSyncFailed(ILogger logger, Guid personId, int statusCode);

    [LoggerMessage(EventId = 2614, Level = LogLevel.Error, Message = "Keycloak user creation in realm {Realm} for tenant {TenantCode} failed with HTTP {StatusCode}")]
    private static partial void LogUserCreateFailed(ILogger logger, string realm, string tenantCode, int statusCode);

    [LoggerMessage(EventId = 2615, Level = LogLevel.Error, Message = "Keycloak user lookup in realm {Realm} failed with HTTP {StatusCode}")]
    private static partial void LogUserLookupFailed(ILogger logger, string realm, int statusCode);

    [LoggerMessage(EventId = 2616, Level = LogLevel.Error, Message = "Keycloak realm role mapping for {PersonId} failed with HTTP {StatusCode}")]
    private static partial void LogRoleMappingFailed(ILogger logger, Guid personId, int statusCode);

    [LoggerMessage(EventId = 2617, Level = LogLevel.Error, Message = "Keycloak welcome actions email dispatch failed for {PersonId} with HTTP {StatusCode}")]
    private static partial void LogWelcomeEmailFailed(ILogger logger, Guid personId, int statusCode);

    [LoggerMessage(EventId = 2618, Level = LogLevel.Error, Message = "Keycloak admin token acquisition against realm {TokenRealm} failed with HTTP {StatusCode}")]
    private static partial void LogAdminTokenFailed(ILogger logger, string tokenRealm, int statusCode);

    [LoggerMessage(EventId = 2619, Level = LogLevel.Error, Message = "Keycloak user profile unmanagedAttributePolicy update in realm {Realm} failed with HTTP {StatusCode}")]
    private static partial void LogUserProfileUpdateFailed(ILogger logger, string realm, int statusCode);

    private sealed record KeycloakTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record KeycloakUserRepresentation(
        string? Id,
        string? Username,
        string? Email,
        string? FirstName,
        string? LastName,
        bool Enabled,
        bool EmailVerified,
        IReadOnlyList<string>? RequiredActions,
        Dictionary<string, List<string>>? Attributes);

    private sealed record KeycloakRoleRepresentation(
        string? Id,
        string? Name);
}
