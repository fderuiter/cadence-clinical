using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;

namespace Cadence.Security.Authentication;

public class GatewayAuthOptions
{
    public string? GatewaySecret { get; set; }
    public bool RequireGatewaySignature { get; set; } = false;
}

public class GatewayAuthMiddleware : IMiddleware
{
    private readonly GatewayAuthOptions _options;

    public GatewayAuthMiddleware(GatewayAuthOptions? options = null)
    {
        _options = options ?? new GatewayAuthOptions();
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        string userId = context.Request.Headers["X-User-Id"].FirstOrDefault()
            ?? context.Request.Headers["x-user-id"].FirstOrDefault() ?? "";

        string rolesHeader = context.Request.Headers["X-User-Roles"].FirstOrDefault()
            ?? context.Request.Headers["x-user-roles"].FirstOrDefault() ?? "";

        var rawRoles = rolesHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var normalizedRoles = RoleNormalization.GetNormalizedRoles(rawRoles);

        string siteIdHeader = context.Request.Headers["X-Site-Id"].FirstOrDefault()
            ?? context.Request.Headers["x-site-id"].FirstOrDefault()
            ?? context.Request.Headers["X-User-Site"].FirstOrDefault() ?? "";
        var assignedSites = siteIdHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        string sponsorId = context.Request.Headers["X-Sponsor-Id"].FirstOrDefault()
            ?? context.Request.Headers["x-sponsor-id"].FirstOrDefault() ?? "";

        string unblindedHeader = context.Request.Headers["X-Unblinded-Access"].FirstOrDefault()
            ?? context.Request.Headers["x-unblinded-access"].FirstOrDefault() ?? "";
        bool unblindedAccess = string.Equals(unblindedHeader, "true", StringComparison.OrdinalIgnoreCase) || unblindedHeader == "1";

        string changeReason = context.Request.Headers["X-Change-Reason"].FirstOrDefault()
            ?? context.Request.Headers["x-change-reason"].FirstOrDefault()
            ?? context.Request.Headers["X-Reason-For-Change"].FirstOrDefault()
            ?? context.Request.Headers["x-reason-for-change"].FirstOrDefault()
            ?? context.Request.Query["change_reason"].FirstOrDefault()
            ?? context.Request.Query["reason_for_change"].FirstOrDefault() ?? "";

        string timestamp = context.Request.Headers["X-Timestamp"].FirstOrDefault()
            ?? context.Request.Headers["x-timestamp"].FirstOrDefault() ?? "";

        string tenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
            ?? context.Request.Headers["x-tenant-id"].FirstOrDefault() ?? "tenant_default";

        string sigToken = context.Request.Headers["X-Sig-Token"].FirstOrDefault()
            ?? context.Request.Headers["x-sig-token"].FirstOrDefault() ?? "";

        string signature = context.Request.Headers["X-Gateway-Signature"].FirstOrDefault()
            ?? context.Request.Headers["x-gateway-signature"].FirstOrDefault() ?? "";

        string secretStr = _options.GatewaySecret
            ?? Environment.GetEnvironmentVariable("GATEWAY_SECRET")
            ?? "";

        // HMAC verification if signature is present or required
        if (!string.IsNullOrWhiteSpace(signature))
        {
            if (!string.IsNullOrWhiteSpace(secretStr))
            {
                byte[] secretBytes = Encoding.UTF8.GetBytes(secretStr);
                bool isValid = HmacSigner.VerifyGatewaySignature(
                    userId: userId,
                    roles: rolesHeader,
                    timestamp: timestamp,
                    signature: signature,
                    secret: secretBytes,
                    changeReason: string.IsNullOrWhiteSpace(changeReason) ? null : changeReason,
                    siteId: string.IsNullOrWhiteSpace(siteIdHeader) ? null : siteIdHeader,
                    sponsorId: string.IsNullOrWhiteSpace(sponsorId) ? null : sponsorId,
                    unblindedAccess: unblindedAccess,
                    tenantId: tenantId,
                    sigToken: string.IsNullOrWhiteSpace(sigToken) ? null : sigToken
                );

                if (!isValid)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Unauthorized: Invalid Gateway HMAC signature");
                    return;
                }
            }
        }
        else if (_options.RequireGatewaySignature)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: Missing Gateway HMAC signature");
            return;
        }

        var principal = new Principal
        {
            UserId = userId,
            Roles = normalizedRoles,
            AssignedSites = assignedSites,
            UnblindedAccess = unblindedAccess,
            SponsorId = string.IsNullOrWhiteSpace(sponsorId) ? null : sponsorId,
            ChangeReason = string.IsNullOrWhiteSpace(changeReason) ? null : changeReason,
            RawRoles = rawRoles
        };

        context.Items["Principal"] = principal;

        // Set ClaimsPrincipal for standard ASP.NET Core authorization
        var claims = new List<Claim>();
        if (!string.IsNullOrWhiteSpace(userId)) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        foreach (var r in normalizedRoles) claims.Add(new Claim(ClaimTypes.Role, r));
        var identity = new ClaimsIdentity(claims, "Gateway");
        context.User = new ClaimsPrincipal(identity);

        // Auditor persona write restriction check
        bool isAuditor = normalizedRoles.Any(r => RoleNormalization.AUDITOR_ROLES.Contains(r));
        string method = context.Request.Method;
        bool isWrite = HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

        if (isAuditor && isWrite)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden: Auditor personas are restricted to read-only access.");
            return;
        }

        await next(context);
    }
}
