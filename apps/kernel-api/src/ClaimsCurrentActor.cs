using System;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;

namespace Cadence.Kernel.Host;

/// <summary>
/// The acting person, from the subject of the gateway-signed caller identity (ADR-2256). Never read from a request
/// body or an unsigned header. A request without a verified subject, or whose subject names no accountable person
/// (a reserved id such as <c>system</c>, or a service or agent id), has no actor, and the kernel denies it (K-TX-5).
/// Host-internal work outside a request (the development tenant bootstrap, ADR-2542) acts as the service account its
/// own scope's <see cref="ServiceActorScope"/> names; request scopes never set one.
/// </summary>
internal sealed class ClaimsCurrentActor(IHttpContextAccessor accessor, ServiceActorScope service) : ICurrentActor
{
    public Actor? Actor => service.Actor ?? RequestActor;

    private Actor? RequestActor =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
        && user.FindFirst(CadenceClaimTypes.Subject)?.Value is { Length: > 0 } subject
            ? PersonOrNull(subject)
            : null;

    private static Actor? PersonOrNull(string subject)
    {
        try
        {
            return Cadence.Kernel.Commands.Actor.Person(subject);
        }
        catch (ArgumentException)
        {
            // The kernel refuses ids that identify no accountable person; such a caller acts as no one.
            return null;
        }
    }
}
