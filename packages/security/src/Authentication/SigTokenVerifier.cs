using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Time;

using Microsoft.IdentityModel.Tokens;

namespace Cadence.Security.Authentication;

/// <summary>
/// Verifies an e-signature step-up token (<c>X-Sig-Token</c>) and consumes it, so each token signs at most once
/// (21 CFR 11.200(a)(1)).
/// <para>
/// <b>Acceptance window.</b> A token is accepted only while <c>iat - ClockSkew &lt;= now &lt; exp + ClockSkew</c>,
/// measured on the injected <see cref="IClock"/>, and only if it was issued for at most
/// <see cref="SigTokenClaims.Lifetime"/> (60 seconds). <see cref="ClockSkew"/> (5 seconds) absorbs clock drift
/// between the gateway that issues and the service that verifies, so a token is usable for at most 70 seconds of
/// verifier time.
/// </para>
/// <para>
/// <b>Single use.</b> The token's <c>jti</c> is recorded in the registered <see cref="ISigTokenUseStore"/>, kept until
/// <c>exp + ClockSkew</c>, the end of the window. A record forgotten any earlier would let the token be replayed in
/// the skew after expiry. How far "single" reaches depends on the store: process memory by default, a database table
/// where a host registers one (ADR-2515).
/// </para>
/// @req:SRS-SYS-005
/// </summary>
public class SigTokenVerifier(ISigTokenUseStore useStore, IClock? clock = null)
{
    /// <summary>The error code every rejection carries (contract §3.1).</summary>
    public const string ReauthenticationRequired = "REAUTHENTICATION_REQUIRED";

    /// <summary>Allowed drift between the issuing gateway's clock and the verifier's.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(5);

    private readonly ISigTokenUseStore _useStore = useStore ?? throw new ArgumentNullException(nameof(useStore));
    private readonly IClock _clock = clock ?? SystemClock.Instance;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    /// <summary>A verifier with its own process-local store, for tests and single-instance tools.</summary>
    public SigTokenVerifier(IClock? clock = null)
        : this(new ReplayCache(clock), clock)
    {
    }

    /// <summary>
    /// Verifies a v3 token for <paramref name="expectedUserId"/> that is bound to exactly <paramref name="binding"/>,
    /// then consumes it. Contract steps 1 to 6: presence, HS256 signature, lifetime and acceptance window, signer,
    /// action and semantic action (and batch when the endpoint names one), single use. Auditor tokens are refused.
    /// Nothing is consumed unless every other check passes. Throws <see cref="UnauthorizedAccessException"/>
    /// (<see cref="ReauthenticationRequired"/>) on any failure.
    /// </summary>
    public async Task<JwtSecurityToken> VerifyAndConsumeAsync(
        string? sigToken,
        string expectedUserId,
        SigTokenBinding binding,
        byte[] secret,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(secret);

        var jwtToken = Validate(sigToken, expectedUserId, secret);

        string? Claim(string type) => jwtToken.Claims.FirstOrDefault(c => c.Type == type)?.Value;

        bool bound =
            string.Equals(Claim(SigTokenClaims.Version), SigTokenClaims.CurrentVersion, StringComparison.Ordinal)
            && string.Equals(Claim(SigTokenClaims.Action), binding.Action, StringComparison.Ordinal)
            && string.Equals(Claim(SigTokenClaims.SemanticAction), binding.SemanticAction, StringComparison.Ordinal)
            && (binding.BatchId is null || string.Equals(Claim(SigTokenClaims.BatchId), binding.BatchId, StringComparison.Ordinal))
            && SigTokenIssuer.MayObtainSignatureToken(jwtToken.Claims.Where(c => c.Type == SigTokenClaims.Roles).Select(c => c.Value));
        if (!bound)
        {
            throw new UnauthorizedAccessException(ReauthenticationRequired);
        }

        var retainUntil = new DateTimeOffset(jwtToken.ValidTo, TimeSpan.Zero) + ClockSkew;
        if (!await _useStore.TryRecordFirstUseAsync(jwtToken.Id, retainUntil, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException(ReauthenticationRequired);
        }

        return jwtToken;
    }

    private JwtSecurityToken Validate(string? sigToken, string expectedUserId, byte[] secret)
    {
        if (string.IsNullOrWhiteSpace(sigToken) || secret.Length == 0 || string.IsNullOrEmpty(expectedUserId))
        {
            throw new UnauthorizedAccessException(ReauthenticationRequired);
        }

        // Lifetime is checked below against the injected clock, not the handler's wall clock.
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(secret),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
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

        if (validatedToken is not JwtSecurityToken jwtToken
            || string.IsNullOrWhiteSpace(jwtToken.Id)
            || !WithinAcceptanceWindow(jwtToken))
        {
            throw new UnauthorizedAccessException(ReauthenticationRequired);
        }

        // Signer identity binding (sub claim)
        string sub = jwtToken.Subject ?? claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        if (!string.Equals(sub, expectedUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(ReauthenticationRequired);
        }

        return jwtToken;
    }

    private bool WithinAcceptanceWindow(JwtSecurityToken token)
    {
        if (token.IssuedAt == DateTime.MinValue || token.ValidTo == DateTime.MinValue)
        {
            return false;
        }

        var issuedAt = new DateTimeOffset(token.IssuedAt, TimeSpan.Zero);
        var expires = new DateTimeOffset(token.ValidTo, TimeSpan.Zero);
        var notBefore = token.ValidFrom == DateTime.MinValue ? issuedAt : new DateTimeOffset(token.ValidFrom, TimeSpan.Zero);
        var now = _clock.UtcNow;

        return expires > issuedAt
            && expires - issuedAt <= SigTokenClaims.Lifetime
            && now >= issuedAt - ClockSkew
            && now >= notBefore - ClockSkew
            && now < expires + ClockSkew;
    }
}
