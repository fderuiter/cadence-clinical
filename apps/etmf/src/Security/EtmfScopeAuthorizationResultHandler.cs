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
    private readonly CadenceAuthorizationMiddlewareResultHandler _handler = new();

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult) =>
        _handler.HandleAsync(next, context, policy, authorizeResult);
}
