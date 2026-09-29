using System;

using Cadence.Security.Authentication;

namespace Cadence.Security;

/// <summary>
/// Forwarding primitive for high-throughput HMAC-SHA256 signature generation and validation for inter-service Gateway authentication.
/// @req:PRD-SYS-053
/// </summary>
public static class HmacSigner
{
    public static string ComputeSignature(string payload, string secret)
        => Authentication.HmacSigner.ComputeSignature(payload, secret);

    public static bool VerifySignature(string payload, string secret, string expectedSignature)
        => Authentication.HmacSigner.VerifySignature(payload, secret, expectedSignature);
}
