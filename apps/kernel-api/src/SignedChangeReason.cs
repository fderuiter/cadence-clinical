using Cadence.Kernel.Api;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;

namespace Cadence.Kernel.Host;

/// <summary>
/// The reason for change from the gateway-signed caller identity (ADR-2495, ADR-2256). The gateway authentication
/// handler verifies the reason as part of the signature before it stores the principal, so an unsigned or altered
/// reason never reaches a kernel command.
/// </summary>
internal sealed class SignedChangeReason : IKernelChangeReason
{
    public string? Read(HttpContext context) =>
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] is Principal { ChangeReason: { Length: > 0 } reason }
            ? reason
            : null;
}
