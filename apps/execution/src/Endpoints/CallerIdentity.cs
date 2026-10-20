using System.Linq;
using System.Security.Claims;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Security.Authentication;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Reads the authenticated caller off the request's validated claims.
/// </summary>
internal static class CallerIdentity
{
    public static AuthenticatedCaller? ToAuthenticatedCaller(this ClaimsPrincipal user)
    {
        string? userId = user.FindFirst(CadenceClaimTypes.Subject)?.Value;
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        return new AuthenticatedCaller(userId, roles);
    }
}
