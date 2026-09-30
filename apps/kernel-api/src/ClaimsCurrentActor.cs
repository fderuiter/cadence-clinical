using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;

namespace Cadence.Kernel.Host;

/// <summary>
/// The acting person, from the subject of the gateway-signed caller identity (ADR-2256). Never read from a request
/// body or an unsigned header; a request without a verified subject has no actor, and the kernel denies it (K-TX-5).
/// </summary>
internal sealed class ClaimsCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    public Actor? Actor =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
        && user.FindFirst(CadenceClaimTypes.Subject)?.Value is { Length: > 0 } subject
            ? Cadence.Kernel.Commands.Actor.Person(subject)
            : null;
}
