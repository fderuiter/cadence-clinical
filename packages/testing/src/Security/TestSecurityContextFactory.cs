using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Security;

namespace Cadence.Testing.Security;

public record TestSecurityContext(
    string UserId = "test-user-001",
    IReadOnlyList<string>? Roles = null,
    string TenantId = "tenant_default",
    string AuthLevel = "authenticated"
)
{
    public IReadOnlyList<string> Roles { get; init; } = Roles ?? new[] { "Site Investigator / CRC" };
}

public static class TestSecurityContextFactory
{
    public static TestSecurityContext Create(
        string userId = "test-user-001",
        IEnumerable<string>? roles = null,
        string tenantId = "tenant_default")
    {
        var rolesList = roles?.ToList() ?? new List<string> { "Site Investigator / CRC" };
        return new TestSecurityContext(
            UserId: userId,
            Roles: rolesList,
            TenantId: tenantId,
            AuthLevel: "authenticated"
        );
    }

    public static Dictionary<string, object> CreateTestToken(
        string userId = "test-user-001",
        IEnumerable<string>? roles = null,
        string tenantId = "tenant_default",
        int expiresIn = 3600)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var rolesList = roles?.ToList() ?? new List<string> { "Site Investigator / CRC" };
        return new Dictionary<string, object>
        {
            ["sub"] = userId,
            ["roles"] = rolesList,
            ["tenant_id"] = tenantId,
            ["iat"] = now,
            ["exp"] = now + expiresIn
        };
    }

    public static string GenerateGatewaySignature(
        string userId,
        string roles,
        string timestamp,
        byte[]? secret = null,
        string? changeReason = null,
        string? siteId = null,
        string? sponsorId = null,
        bool unblindedAccess = false,
        string? tenantId = null,
        string? sigToken = null)
    {
        byte[] secretBytes = secret ?? Encoding.UTF8.GetBytes(
            Environment.GetEnvironmentVariable("GATEWAY_SECRET") ?? "internal-gateway-secret-12345"
        );

        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["change_reason"] = changeReason ?? "",
            ["roles"] = roles,
            ["site_id"] = siteId ?? "",
            ["sponsor_id"] = sponsorId ?? "",
            ["tenant_id"] = tenantId ?? "",
            ["timestamp"] = timestamp,
            ["unblinded_access"] = unblindedAccess,
            ["user_id"] = userId
        };

        if (sigToken != null)
        {
            payload["sig_token"] = sigToken;
        }

        string serialized = JsonSerializer.Serialize(payload);
        return HmacSigner.ComputeSignature(serialized, Encoding.UTF8.GetString(secretBytes));
    }

    public static Dictionary<string, string> CreateTestAuthHeaders(
        string userId = "test-user-001",
        IEnumerable<string>? roles = null,
        string tenantId = "tenant_default",
        string? changeReason = "Automated test change justification reason",
        string? siteId = null,
        string? sponsorId = null,
        bool unblindedAccess = false,
        string? sigToken = null,
        byte[]? secret = null)
    {
        var rolesList = roles?.ToList() ?? new List<string> { "Site Investigator / CRC" };
        string rolesStr = string.Join(",", rolesList);
        string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        string signature = GenerateGatewaySignature(
            userId: userId,
            roles: rolesStr,
            timestamp: ts,
            secret: secret,
            changeReason: changeReason,
            siteId: siteId,
            sponsorId: sponsorId,
            unblindedAccess: unblindedAccess,
            tenantId: tenantId,
            sigToken: sigToken
        );

        var headers = new Dictionary<string, string>
        {
            ["X-User-Id"] = userId,
            ["X-User-Roles"] = rolesStr,
            ["X-Gateway-Timestamp"] = ts,
            ["X-Timestamp"] = ts,
            ["X-Tenant-Id"] = tenantId,
            ["X-Gateway-Signature"] = signature,
            ["X-Signature-Version"] = "2"
        };

        if (!string.IsNullOrEmpty(changeReason))
        {
            headers["X-Change-Reason"] = changeReason;
        }

        if (!string.IsNullOrEmpty(siteId))
        {
            headers["X-Site-Id"] = siteId;
        }

        if (!string.IsNullOrEmpty(sponsorId))
        {
            headers["X-Sponsor-Id"] = sponsorId;
        }

        if (unblindedAccess)
        {
            headers["X-Unblinded-Access"] = "true";
        }

        if (!string.IsNullOrEmpty(sigToken))
        {
            headers["X-Sig-Token"] = sigToken;
        }

        return headers;
    }
}
