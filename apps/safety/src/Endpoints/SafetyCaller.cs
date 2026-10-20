using System.Security.Claims;

using Cadence.Security.Authentication;

namespace Cadence.Apps.Safety.Endpoints;

/// <summary>
/// Reads the caller off the request's verified gateway claims (<c>sub</c>), never off the request body or an unsigned
/// header (#5462).
/// </summary>
internal static class SafetyCaller
{
    /// <summary>The verified caller's user id, or null when the request is not authenticated.</summary>
    public static string? VerifiedUserId(this ClaimsPrincipal? user)
    {
        if (user?.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        string? userId = user.FindFirst(CadenceClaimTypes.Subject)?.Value;
        return string.IsNullOrWhiteSpace(userId) ? null : userId;
    }
}
