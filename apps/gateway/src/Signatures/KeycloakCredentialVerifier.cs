using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Cadence.Apps.Gateway.Signatures;

public enum CredentialCheckOutcome
{
    Verified,
    Rejected,
    Unavailable,
}

/// <summary>The result of re-checking a signer's credentials. <see cref="Subject"/> is set only when verified.</summary>
public sealed record CredentialCheck(CredentialCheckOutcome Outcome, string? Subject = null)
{
    public static CredentialCheck Rejected { get; } = new(CredentialCheckOutcome.Rejected);
    public static CredentialCheck Unavailable { get; } = new(CredentialCheckOutcome.Unavailable);
}

/// <summary>
/// Re-checks a signer's password (and one-time code, when their account requires one) with the identity
/// provider. Implementations must never log or keep the password.
/// </summary>
public interface ICredentialVerifier
{
    Task<CredentialCheck> VerifyAsync(string username, string password, string? oneTimeCode, CancellationToken cancellationToken);
}

/// <summary>
/// Where and as which client the gateway re-checks signer credentials.
/// </summary>
public sealed class SignatureCredentialOptions
{
    public const string DefaultClientId = "cadence-esignature";

    /// <summary>The realm's OIDC token endpoint. Derived from the OIDC settings unless <c>OIDC_TOKEN_ENDPOINT</c> is set.</summary>
    public Uri? TokenEndpoint { get; set; }

    /// <summary>A Keycloak client with only "Direct access grants" enabled (<c>OIDC_SIGNATURE_CLIENT_ID</c>).</summary>
    public string ClientId { get; set; } = DefaultClientId;

    /// <summary>Set when that client is confidential (<c>OIDC_SIGNATURE_CLIENT_SECRET</c>).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// The Keycloak token endpoint for a realm: <c>OIDC_TOKEN_ENDPOINT</c> when set, otherwise next to the
    /// discovery document the gateway already uses (<c>OIDC_METADATA_ADDRESS</c>, or <c>OIDC_AUTHORITY</c>).
    /// </summary>
    public static Uri ResolveTokenEndpoint(string? tokenEndpoint, string? metadataAddress, string authority)
    {
        if (!string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            return new Uri(tokenEndpoint, UriKind.Absolute);
        }

        const string discoverySuffix = "/.well-known/openid-configuration";
        string realm = string.IsNullOrWhiteSpace(metadataAddress) ? authority : metadataAddress;
        if (realm.EndsWith(discoverySuffix, StringComparison.Ordinal))
        {
            realm = realm[..^discoverySuffix.Length];
        }

        return new Uri($"{realm.TrimEnd('/')}/protocol/openid-connect/token", UriKind.Absolute);
    }
}

/// <summary>
/// Verifies credentials with a Keycloak direct-access (resource owner password) grant on a client reserved for
/// signing, then ends the short session that grant opened. Not retried: each attempt counts towards the realm's
/// brute-force lockout.
/// @req:PRD-SYS-053
/// </summary>
public sealed class KeycloakCredentialVerifier(
    HttpClient httpClient,
    IOptions<SignatureCredentialOptions> options,
    ILogger<KeycloakCredentialVerifier> logger) : ICredentialVerifier
{
    public async Task<CredentialCheck> VerifyAsync(string username, string password, string? oneTimeCode, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var tokenEndpoint = settings.TokenEndpoint ?? throw new InvalidOperationException("The signature token endpoint is not configured.");

        var form = ClientFields(settings);
        form.Add(new("grant_type", "password"));
        form.Add(new("scope", "openid"));
        form.Add(new("username", username));
        form.Add(new("password", password));
        if (!string.IsNullOrWhiteSpace(oneTimeCode))
        {
            form.Add(new("totp", oneTimeCode));
        }

        TokenResponse? tokens;
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await httpClient.PostAsync(tokenEndpoint, content, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                // invalid_grant covers a wrong password or code and a disabled or locked account. Anything else
                // (invalid_client, unauthorized_client) is the gateway's misconfiguration, not the signer's.
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken).ConfigureAwait(false);
                if (string.Equals(error?.Error, "invalid_grant", StringComparison.Ordinal))
                {
                    logger.LogWarning("Signature re-authentication rejected by the identity provider.");
                    return CredentialCheck.Rejected;
                }

                logger.LogError("Identity provider refused the signature client ({Error}); check OIDC_SIGNATURE_CLIENT_ID.", error?.Error);
                return CredentialCheck.Unavailable;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Identity provider token endpoint answered {Status} during signature re-authentication.", (int)response.StatusCode);
                return CredentialCheck.Unavailable;
            }

            tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (ex is TaskCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogError(ex, "Identity provider unreachable during signature re-authentication.");
            return CredentialCheck.Unavailable;
        }

        // The token came straight from the configured token endpoint over the back channel, so its subject can be
        // read without re-validating its signature (OIDC Core §3.1.3.7).
        string? subject = tokens?.AccessToken is { Length: > 0 } accessToken ? new JsonWebToken(accessToken).Subject : null;
        if (tokens?.RefreshToken is { Length: > 0 } refreshToken)
        {
            await EndSessionAsync(settings, tokenEndpoint, refreshToken, cancellationToken).ConfigureAwait(false);
        }

        return string.IsNullOrWhiteSpace(subject)
            ? CredentialCheck.Unavailable
            : new CredentialCheck(CredentialCheckOutcome.Verified, subject);
    }

    private async Task EndSessionAsync(SignatureCredentialOptions settings, Uri tokenEndpoint, string refreshToken, CancellationToken cancellationToken)
    {
        string path = tokenEndpoint.AbsolutePath;
        if (!path.EndsWith("/token", StringComparison.Ordinal))
        {
            return;
        }

        var logoutEndpoint = new UriBuilder(tokenEndpoint) { Path = path[..^"token".Length] + "logout" }.Uri;
        var form = ClientFields(settings);
        form.Add(new("refresh_token", refreshToken));
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await httpClient.PostAsync(logoutEndpoint, content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Could not end the signature re-authentication session ({Status}); it expires on its own.", (int)response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not end the signature re-authentication session; it expires on its own.");
        }
    }

    private static List<KeyValuePair<string, string>> ClientFields(SignatureCredentialOptions settings)
    {
        var fields = new List<KeyValuePair<string, string>> { new("client_id", settings.ClientId) };
        if (!string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            fields.Add(new("client_secret", settings.ClientSecret));
        }

        return fields;
    }

    private sealed record ErrorResponse([property: JsonPropertyName("error")] string? Error);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);
}
