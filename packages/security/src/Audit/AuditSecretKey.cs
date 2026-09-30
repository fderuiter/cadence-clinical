using System;

namespace Cadence.Security.Audit;

/// <summary>
/// Single source of the HMAC key used to chain 21 CFR Part 11 audit digests. Every digest producer and
/// verifier (<see cref="AuditDigestCalculator"/>, <see cref="AuditDigestEngine"/> and
/// <c>Cadence.Compliance.AuditDigestCalculator</c>) resolves the key here, so they can never diverge. There is
/// no built-in default: an unset key fails closed instead of signing the audit trail with a value that is
/// committed to source control.
/// @req:PRD-SYS-053
/// </summary>
public static class AuditSecretKey
{
    /// <summary>Environment variable / configuration key holding the audit HMAC key.</summary>
    public const string EnvironmentVariable = "AUDIT_LOG_SECRET_KEY";

    /// <summary>
    /// Returns <paramref name="explicitKey"/> when it is non-blank, otherwise the trimmed value of
    /// <see cref="EnvironmentVariable"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Neither an explicit key nor the environment variable is set.</exception>
    public static string Resolve(string? explicitKey = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitKey))
        {
            return explicitKey.Trim();
        }

        return Environment.GetEnvironmentVariable(EnvironmentVariable) is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured.Trim()
            : throw new InvalidOperationException(
                $"{EnvironmentVariable} is not set. The GxP audit trail cannot be signed or verified without it.");
    }
}
