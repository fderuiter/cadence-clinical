using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Cadence.Security.Authentication;
using Cadence.Security.Blinding;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Apps.Execution.Endpoints.Filters;

/// <summary>
/// Global endpoint response filter enforcing automatic payload masking on execution API responses
/// based on the caller principal's blinding level and roles.
/// @req:PRD-SYS-053
/// </summary>
public sealed class BlindingResponseFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context).ConfigureAwait(false);

        if (result is null)
        {
            return null;
        }

        Principal? principal = ResolvePrincipal(context.HttpContext);

        if (result is IValueHttpResult valueResult)
        {
            var rawValue = valueResult.Value;
            if (rawValue is null || rawValue is ProblemDetails || rawValue is HttpValidationProblemDetails)
            {
                return result;
            }

            var maskedValue = FieldMasker.MaskPayload(rawValue, principal);

            int statusCode = (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;

            string? location = valueResult.GetType().GetProperty("Location")?.GetValue(valueResult) as string;

            if (statusCode == StatusCodes.Status201Created && !string.IsNullOrEmpty(location))
            {
                return Results.Created(location, maskedValue);
            }

            return Results.Ok(maskedValue);
        }

        if (result is not IResult)
        {
            return FieldMasker.MaskPayload(result, principal);
        }

        return result;
    }

    private static Principal? ResolvePrincipal(HttpContext httpContext)
    {
        if (httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] is Principal principal)
        {
            return principal;
        }

        if (httpContext.User.Identity is { IsAuthenticated: true })
        {
            var roles = httpContext.User.Claims
                .Where(c => c.Type == "role" || c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            bool unblinded = string.Equals(
                httpContext.User.FindFirst(CadenceClaimTypes.UnblindedAccess)?.Value,
                "true",
                System.StringComparison.OrdinalIgnoreCase);

            return new Principal
            {
                UserId = httpContext.User.Identity.Name ?? "",
                Roles = RoleNormalization.GetNormalizedRoles(roles),
                RawRoles = roles,
                UnblindedAccess = unblinded
            };
        }

        return null;
    }
}
