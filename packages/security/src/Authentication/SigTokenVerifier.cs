using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;

using Microsoft.IdentityModel.Tokens;

namespace Cadence.Security.Authentication;

/// <summary>
/// Centralized 21 CFR Part 11 signature token verifier and single-use consumer.
/// @req:PRD-SYS-053
/// </summary>
public class SigTokenVerifier
{
    /// <summary>The error code every rejection carries (contract §3.1).</summary>
    public const string ReauthenticationRequired = "REAUTHENTICATION_REQUIRED";

    private readonly ReplayCache _replayCache;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    public SigTokenVerifier(ReplayCache? replayCache = null)
    {
        _replayCache = replayCache ?? new ReplayCache();
        _tokenHandler = new JwtSecurityTokenHandler();
    }

    /// <summary>
    /// Verifies and single-use consumes an e-signature JWT re-authentication token.
    /// Throws UnauthorizedAccessException on any validation, binding, or replay failure.
    /// </summary>
    public JwtSecurityToken VerifyAndConsumeSigToken(string? sigToken, string expectedUserId, byte[]? secret = null)
    {
        var jwtToken = Validate(sigToken, expectedUserId, secret is { Length: > 0 } ? secret : SecretFromEnvironment());
        Consume(sigToken!, jwtToken);
        return jwtToken;
    }

    /// <summary>
    /// Verifies a v3 token for <paramref name="expectedUserId"/> that is bound to exactly <paramref name="binding"/>,
    /// then consumes it. Contract steps 1 to 6: presence, HS256 signature, 60-second lifetime, signer, action and
    /// semantic action (and batch when the endpoint names one), single use. Auditor tokens are refused. Nothing
    /// is consumed unless every check passes. Throws <see cref="UnauthorizedAccessException"/>
    /// ("REAUTHENTICATION_REQUIRED") on any failure.
    /// </summary>
    public JwtSecurityToken VerifyAndConsumeSigToken(string? sigToken, string expectedUserId, SigTokenBinding binding, byte[]? secret = null)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var effectiveSecret = secret is { Length: > 0 } ? secret : SecretFromEnvironment();
        var jwtToken = Validate(sigToken, expectedUserId, effectiveSecret);

        string? Claim(string type) => jwtToken.Claims.FirstOrDefault(c => c.Type == type)?.Value;

        bool bound =
            string.Equals(Claim(SigTokenClaims.Version), SigTokenClaims.CurrentVersion, StringComparison.Ordinal)
            && string.Equals(Claim(SigTokenClaims.Action), binding.Action, StringComparison.Ordinal)
            && string.Equals(Claim(SigTokenClaims.SemanticAction), binding.SemanticAction, StringComparison.Ordinal)
            && (binding.BatchId is null || string.Equals(Claim(SigTokenClaims.BatchId), binding.BatchId, StringComparison.Ordinal))
            && jwtToken.IssuedAt != DateTime.MinValue
            && jwtToken.ValidTo - jwtToken.IssuedAt <= SigTokenClaims.Lifetime
            && SigTokenIssuer.MayObtainSignatureToken(jwtToken.Claims.Where(c => c.Type == SigTokenClaims.Roles).Select(c => c.Value));
        if (!bound)
        {
            throw new UnauthorizedAccessException(ReauthenticationRequired);
        }

        Consume(sigToken!, jwtToken);
        return jwtToken;
    }

    private JwtSecurityToken Validate(string? sigToken, string expectedUserId, byte[] secret)
    {
        if (string.IsNullOrWhiteSpace(sigToken) || secret.Length == 0)
        {
            throw new UnauthorizedAccessException(ReauthenticationRequired);
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(secret),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(5)
        };

        SecurityToken validatedToken;
        ClaimsPrincipal claimsPrincipal;
        try
        {
            claimsPrincipal = _tokenHandler.ValidateToken(sigToken, validationParameters, out validatedToken);
        }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException(ReauthenticationRequired, ex);
        }

        if (validatedToken is not JwtSecurityToken jwtToken)
        {
            throw new UnauthorizedAccessException(ReauthenticationRequired);
        }

        // Signer identity binding (sub claim)
        string sub = jwtToken.Subject ?? claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        if (string.IsNullOrEmpty(expectedUserId) || !string.Equals(sub, expectedUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(ReauthenticationRequired);
        }

        return jwtToken;
    }

    // Single-use consumption (replay prevention)
    private void Consume(string sigToken, JwtSecurityToken jwtToken)
    {
        double exp = new DateTimeOffset(jwtToken.ValidTo).ToUnixTimeSeconds();
        if (!_replayCache.Consume(sigToken, jwtToken.Id, exp))
        {
            throw new UnauthorizedAccessException(ReauthenticationRequired);
        }
    }

    private static byte[] SecretFromEnvironment()
    {
        string? gatewaySecret = Environment.GetEnvironmentVariable("GATEWAY_SECRET");
        if (string.IsNullOrWhiteSpace(gatewaySecret))
        {
            gatewaySecret = "cadence-test-gateway-secret-0123456789abcdef"; // pragma: allowlist secret
        }

        return Encoding.UTF8.GetBytes(gatewaySecret);
    }
}
