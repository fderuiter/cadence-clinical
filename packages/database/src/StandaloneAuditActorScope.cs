using System;
using System.Security.Claims;
using System.Threading;

using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Database;

/// <summary>
/// Ambient service-account actor scope and fail-closed HTTP caller resolver for standalone
/// <see cref="GxPAuditInterceptor"/> instances attached by <see cref="CadenceDatabaseExtensions.AddCadencePostgresDbContext{TDbContext}"/>.
/// </summary>
public static class StandaloneAuditActorScope
{
    private static readonly AsyncLocal<string?> AmbientActor = new();

    public static string? CurrentActor => AmbientActor.Value;

    public static IDisposable Begin(string actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        string? previous = AmbientActor.Value;
        AmbientActor.Value = actorId.Trim();
        return new RestoreScope(previous);
    }

    public static string ResolveRequiredActor(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (AmbientActor.Value is { Length: > 0 } ambientActor)
        {
            return ambientActor;
        }

        HttpContext? httpContext = serviceProvider.GetService<IHttpContextAccessor>()?.HttpContext;
        if (httpContext is not null)
        {
            string? resolvedCaller = CallerScope.Principal(httpContext)?.UserId
                ?? ResolveAuthenticatedPrincipalId(httpContext.User);

            if (!string.IsNullOrWhiteSpace(resolvedCaller))
            {
                return resolvedCaller;
            }
        }

        throw new InvalidOperationException(
            "Authenticated caller identity or service-account audit actor scope is required for GxP writes.");
    }

    private static string? ResolveAuthenticatedPrincipalId(ClaimsPrincipal user)
    {
        if (user.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        return NullIfWhiteSpace(user.FindFirst(CadenceClaimTypes.Subject)?.Value)
            ?? NullIfWhiteSpace(user.FindFirst(ClaimTypes.NameIdentifier)?.Value)
            ?? NullIfWhiteSpace(user.Identity.Name);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class RestoreScope(string? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                AmbientActor.Value = previous;
            }
        }
    }
}
