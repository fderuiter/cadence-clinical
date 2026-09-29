using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// The caller identity established by gateway-signature authentication. Application services take identity
/// and roles from here, never from request bodies or unsigned headers.
/// @req:PRD-SYS-053
/// </summary>
public sealed record AuthenticatedCaller(string UserId, IReadOnlyList<string> Roles)
{
    /// <summary>Returns the first of the caller's roles that satisfies <paramref name="isAuthorized"/>, if any.</summary>
    public string? FirstRoleMatching(Func<string, bool> isAuthorized) => Roles.FirstOrDefault(isAuthorized);
}
