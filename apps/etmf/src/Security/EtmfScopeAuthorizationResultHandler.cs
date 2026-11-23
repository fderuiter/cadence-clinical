using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Etmf.Security;

/// <summary>
/// Emits structured HTTP 403 ProblemDetails with <c>code: "FORBIDDEN_SCOPE"</c> whenever an admitted caller fails
/// tenant, study, or site scope requirements so the web client can render actionable scope-assignment guidance
/// rather than a generic permission or empty-state message.
/// </summary>
public sealed class EtmfScopeAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && authorizeResult.AuthorizationFailure is { } failure)
        {
            var failedRequirements = failure.FailedRequirements;
            bool hasNonScopeFailure = failedRequirements.Any(req =>
                req is PermissionRequirement
                || req is RoleRequirement
                || req is UndeclaredEndpointRequirement
                || req is AuditorReadOnlyRequirement
                || req is ChangeJustificationRequirement);

            bool hasScopeFailure = !hasNonScopeFailure && failedRequirements.Any(req =>
                req is TenantScopeRequirement
                || req is CallerStudyScopeRequirement
                || req is CallerStudyAndSiteScopeRequirement
                || req is StudyScopeRequirement
                || req is SiteScopeRequirement);

            if (hasScopeFailure)
            {
                string detail = failure.FailureReasons.FirstOrDefault()?.Message
                    ?? ClinicalScopeGuard.StudyAccessDeniedDetail;

                await Results.Problem(
                    title: "Forbidden",
                    detail: detail,
                    statusCode: StatusCodes.Status403Forbidden,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "FORBIDDEN_SCOPE",
                    }).ExecuteAsync(context);
                return;
            }
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
