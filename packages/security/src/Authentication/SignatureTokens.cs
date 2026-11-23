using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Cadence.Security.Rbac;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cadence.Security.Authentication;

/// <summary>
/// Claim names and constants of the e-signature step-up token (<c>X-Sig-Token</c>), per
/// <c>docs/SDLC/Signature_Token_Cryptographic_Contract.md</c>.
/// </summary>
public static class SigTokenClaims
{
    public const string Username = "username";
    public const string Roles = "roles";
    public const string Action = "action";
    public const string SemanticAction = "semantic_action";
    public const string BatchId = "batch_id";
    public const string Version = "sig_ver";

    /// <summary>The only contract version issued and accepted.</summary>
    public const string CurrentVersion = "v3";

    /// <summary>How long a token is valid after issue. The verifier rejects any token issued for longer.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
}

/// <summary>
/// What a signature token authorizes: one signer, one concrete action and its namespaced meaning
/// (for example <c>/api/v1/execution/forms/F-1/approve</c> and <c>execution.form.approve</c>), and optionally
/// one batch.
/// </summary>
public sealed record SigTokenGrant(
    string Subject,
    string Username,
    IReadOnlyList<string> Roles,
    string Action,
    string SemanticAction,
    string? BatchId = null);

/// <summary>
/// What a signature-gated endpoint expects a token to be bound to.
/// </summary>
public sealed record SigTokenBinding(string Action, string SemanticAction, string? BatchId = null);

/// <summary>
/// Computes the deterministic <c>batch_id</c> claim for batch signature endpoints:
/// the lower-case hex SHA-256 digest of trimmed target identifiers sorted with
/// <see cref="StringComparer.Ordinal"/> and joined with <c>","</c>.
/// </summary>
public static class SigTokenBatchId
{
    public static string? FromIds(IEnumerable<string?>? ids)
    {
        if (ids is null)
        {
            return null;
        }

        var trimmed = new List<string>();
        foreach (string? id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            trimmed.Add(id.Trim());
        }

        if (trimmed.Count == 0)
        {
            return null;
        }

        trimmed.Sort(StringComparer.Ordinal);
        string canonical = string.Join(",", trimmed);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

/// <summary>
/// Issues e-signature step-up tokens. Only the gateway calls this, and only after the signer has re-entered
/// their credentials.
/// @req:PRD-SYS-053
/// </summary>
public static class SigTokenIssuer
{
    /// <summary>
    /// Auditors and inspectors are read-only and may never sign (21 CFR Part 11 contract §4).
    /// </summary>
    public static bool MayObtainSignatureToken(IEnumerable<string> roles) =>
        !roles.Any(role =>
            RoleNormalization.AUDITOR_ROLES.Contains(role)
            || RoleNormalization.AUDITOR_ROLES.Contains(role.Replace(' ', '_')));

    /// <summary>Returns a compact HS256 JWT valid for <see cref="SigTokenClaims.Lifetime"/> from <paramref name="issuedAt"/>.</summary>
    public static string Issue(SigTokenGrant grant, DateTimeOffset issuedAt, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(secret);
        if (!MayObtainSignatureToken(grant.Roles))
        {
            throw new InvalidOperationException("Auditors and inspectors cannot obtain e-signature tokens.");
        }

        var claims = new Dictionary<string, object>
        {
            [SigTokenClaims.Username] = grant.Username,
            [SigTokenClaims.Roles] = grant.Roles.ToArray(),
            [SigTokenClaims.Action] = grant.Action,
            [SigTokenClaims.SemanticAction] = grant.SemanticAction,
            [SigTokenClaims.Version] = SigTokenClaims.CurrentVersion,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
        };
        if (!string.IsNullOrWhiteSpace(grant.BatchId))
        {
            claims[SigTokenClaims.BatchId] = grant.BatchId;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, grant.Subject)]),
            Claims = claims,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = issuedAt.Add(SigTokenClaims.Lifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(secret), SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }
}
