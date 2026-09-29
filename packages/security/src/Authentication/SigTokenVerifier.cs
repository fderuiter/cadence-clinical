using System;
using System.IdentityModel.Tokens.Jwt;
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
        if (string.IsNullOrWhiteSpace(sigToken))
        {
            throw new UnauthorizedAccessException("REAUTHENTICATION_REQUIRED");
        }

        if (secret == null || secret.Length == 0)
        {
            string? gatewaySecret = Environment.GetEnvironmentVariable("GATEWAY_SECRET");
            if (string.IsNullOrWhiteSpace(gatewaySecret))
            {
                throw new InvalidOperationException("GATEWAY_SECRET is not configured; e-signature tokens cannot be verified.");
            }

            secret = Encoding.UTF8.GetBytes(gatewaySecret);
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(secret),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
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
            throw new UnauthorizedAccessException("REAUTHENTICATION_REQUIRED", ex);
        }

        if (validatedToken is not JwtSecurityToken jwtToken)
        {
            throw new UnauthorizedAccessException("REAUTHENTICATION_REQUIRED");
        }

        // 1. Signer identity binding (sub claim)
        string sub = jwtToken.Subject ?? claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        if (!string.Equals(sub, expectedUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("REAUTHENTICATION_REQUIRED");
        }

        // 2. Single-use consumption (replay prevention)
        string jti = jwtToken.Id;
        double exp = new DateTimeOffset(jwtToken.ValidTo).ToUnixTimeSeconds();

        if (!_replayCache.Consume(sigToken, jti, exp))
        {
            throw new UnauthorizedAccessException("REAUTHENTICATION_REQUIRED");
        }

        return jwtToken;
    }
}
