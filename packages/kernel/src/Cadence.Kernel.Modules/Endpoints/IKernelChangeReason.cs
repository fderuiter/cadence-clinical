using Microsoft.AspNetCore.Http;

namespace Cadence.Kernel.Modules;

/// <summary>
/// The reason for change the caller gave, from their verified identity (ADR-2495: the gateway signs it). The host
/// implements it; the endpoints never read an unsigned reason.
/// </summary>
public interface IKernelChangeReason
{
    /// <summary>The verified reason for change on the request, or null when it carries none.</summary>
    string? Read(HttpContext context);
}
