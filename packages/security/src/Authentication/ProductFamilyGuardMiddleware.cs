using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Tenancy;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cadence.Security.Authentication;

/// <summary>
/// Endpoint metadata marking a baseline trust, authority, audit, export, lock, or commercial lifecycle route that is
/// never gated behind a module <see cref="ProductFamily"/> entitlement.
/// </summary>
public sealed class AllowBaselineProductFamilyMetadata
{
    /// <summary>The shared singleton instance.</summary>
    public static AllowBaselineProductFamilyMetadata Instance { get; } = new();

    private AllowBaselineProductFamilyMetadata()
    {
    }
}

/// <summary>
/// Endpoint metadata explicitly declaring that a route requires at least one of the specified <see cref="ProductFamily"/>
/// entitlements on the signed gateway posture.
/// </summary>
public sealed record RequireProductFamilyMetadata(IReadOnlyList<ProductFamily> AnyOf);

/// <summary>
/// Endpoint convention helpers for module <see cref="ProductFamily"/> packaging enforcement.
/// </summary>
public static class ProductFamilyEndpointExtensions
{
    /// <summary>Marks an endpoint as baseline so <see cref="ProductFamilyGuardMiddleware"/> allows any workspace tier.</summary>
    public static TBuilder AllowBaselineProductFamily<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(AllowBaselineProductFamilyMetadata.Instance);

    /// <summary>Requires the workspace to hold at least one of <paramref name="families"/>.</summary>
    public static TBuilder RequireProductFamily<TBuilder>(this TBuilder builder, params ProductFamily[] families)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(families);
        if (families.Length == 0)
        {
            throw new ArgumentException("At least one ProductFamily must be specified.", nameof(families));
        }

        return builder.WithMetadata(new RequireProductFamilyMetadata(families));
    }
}

/// <summary>
/// Enforces workspace <see cref="ProductFamily"/> packaging across all 20 backend HTTP surfaces using the signed
/// <see cref="GatewayHeaders.TenantProductFamilies"/> posture on <see cref="Principal"/>. Clinical data authorization
/// remains strictly governed by kernel grants and RBAC; commercial packaging gates module surface access only.
/// </summary>
public sealed class ProductFamilyGuardMiddleware : IMiddleware
{
    private static readonly ProductFamily[] BlueprintOnly = [ProductFamily.Blueprint];
    private static readonly ProductFamily[] CaptureOnly = [ProductFamily.Capture];
    private static readonly ProductFamily[] OrbitOnly = [ProductFamily.Orbit];
    private static readonly ProductFamily[] EngageOnly = [ProductFamily.Engage];
    private static readonly ProductFamily[] AllocateOnly = [ProductFamily.Allocate];
    private static readonly ProductFamily[] VigilOnly = [ProductFamily.Vigil];
    private static readonly ProductFamily[] LensOnly = [ProductFamily.Lens];
    private static readonly ProductFamily[] AssureOnly = [ProductFamily.Assure];
    private static readonly ProductFamily[] StudyCreationFamilies = [ProductFamily.Blueprint, ProductFamily.Capture, ProductFamily.Orbit];
    private static readonly ProductFamily[] ValidationReadFamilies = [ProductFamily.Blueprint, ProductFamily.Capture, ProductFamily.Assure];
    private static readonly ProductFamily[] AllFamilies =
    [
        ProductFamily.Blueprint,
        ProductFamily.Capture,
        ProductFamily.Orbit,
        ProductFamily.Engage,
        ProductFamily.Allocate,
        ProductFamily.Vigil,
        ProductFamily.Lens,
        ProductFamily.Assure,
    ];

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        Endpoint? endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null
            || endpoint?.Metadata.GetMetadata<AllowBaselineProductFamilyMetadata>() is not null)
        {
            await next(context);
            return;
        }

        if (context.Items[GatewayAuthenticationHandler.PrincipalItemKey] is not Principal principal
            || string.IsNullOrWhiteSpace(principal.TenantId))
        {
            await next(context);
            return;
        }

        string path = context.Request.Path.Value ?? string.Empty;
        if (IsExemptServiceProbe(principal, path))
        {
            await next(context);
            return;
        }

        IReadOnlyList<ProductFamily>? requiredAnyOf = endpoint?.Metadata.GetMetadata<RequireProductFamilyMetadata>()?.AnyOf
            ?? ResolveRequiredFamilies(path, context.Request.Method);

        if (requiredAnyOf is null || HasAnyRequiredFamily(principal, requiredAnyOf))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        string requiredNames = string.Join(", ", requiredAnyOf.Select(static f => f.ToString()));
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new
            {
                type = $"https://cadence.clinical/errors/{CommercialErrors.ProductFamilyNotEntitled.Code}",
                title = "Workspace product family not entitled",
                status = StatusCodes.Status403Forbidden,
                detail = $"{CommercialErrors.ProductFamilyNotEntitled.Message} Required product family: {requiredNames}.",
                code = CommercialErrors.ProductFamilyNotEntitled.Code,
            },
            cancellationToken: context.RequestAborted);
    }

    private static bool HasAnyRequiredFamily(Principal principal, IReadOnlyList<ProductFamily> requiredAnyOf)
    {
        for (int i = 0; i < requiredAnyOf.Count; i++)
        {
            if (principal.HasProductFamily(requiredAnyOf[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExemptServiceProbe(Principal principal, string path)
    {
        bool isService = principal.Roles.Any(static r => string.Equals(r, "service", StringComparison.OrdinalIgnoreCase));
        if (!isService)
        {
            return false;
        }

        if (string.Equals(principal.UserId, CommercialOperators.GatewayPostureProbe.Id, StringComparison.Ordinal)
            && string.Equals(path, "/api/v1/kernel/entitlements/posture", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(principal.UserId, CommercialOperators.BillingOperator.Id, StringComparison.Ordinal)
            && StartsWithSegment(path, "/api/v1/kernel/entitlements");
    }

    /// <summary>
    /// Maps an HTTP path and method to the required <see cref="ProductFamily"/> set (any-of), or <c>null</c> for
    /// baseline routes that remain accessible regardless of commercial packaging.
    /// </summary>
    public static IReadOnlyList<ProductFamily>? ResolveRequiredFamilies(string path, string method)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (StartsWithSegment(path, "/mcp"))
        {
            return AllFamilies;
        }

        if (!StartsWithSegment(path, "/api/v1"))
        {
            return null;
        }

        // Baseline gateway & step-up signature surfaces
        if (StartsWithSegment(path, "/api/v1/status")
            || StartsWithSegment(path, "/api/v1/signatures"))
        {
            return null;
        }

        // Kernel host surfaces
        if (StartsWithSegment(path, "/api/v1/kernel"))
        {
            return ResolveKernelFamilies(path, method);
        }

        // Execution surfaces (forms in kernel-api + standalone execution endpoints)
        if (StartsWithSegment(path, "/api/v1/execution"))
        {
            return ResolveExecutionFamilies(path, method);
        }

        // Designer surfaces
        if (StartsWithSegment(path, "/api/v1/studies")
            || StartsWithSegment(path, "/api/v1/protocols"))
        {
            return BlueprintOnly;
        }

        // Interop surfaces
        if (StartsWithSegment(path, "/api/v1/interop/sce"))
        {
            return LensOnly;
        }

        if (StartsWithSegment(path, "/api/v1/interop/esource")
            || StartsWithSegment(path, "/api/v1/interop"))
        {
            return CaptureOnly;
        }

        // Orbit operational surfaces
        if (StartsWithSegment(path, "/api/v1/etmf")
            || StartsWithSegment(path, "/api/v1/ctms")
            || StartsWithSegment(path, "/api/v1/eisf")
            || StartsWithSegment(path, "/api/v1/org")
            || StartsWithSegment(path, "/api/v1/fileshare")
            || StartsWithSegment(path, "/api/v1/payments"))
        {
            return OrbitOnly;
        }

        // Engage participant surfaces
        if (StartsWithSegment(path, "/api/v1/econsent")
            || StartsWithSegment(path, "/api/v1/ai/biometrics")
            || StartsWithSegment(path, "/api/v1/ai/biometric"))
        {
            return EngageOnly;
        }

        // Vigil safety surfaces
        if (StartsWithSegment(path, "/api/v1/safety"))
        {
            return VigilOnly;
        }

        // Lens imaging surfaces
        if (StartsWithSegment(path, "/api/v1/imaging"))
        {
            return LensOnly;
        }

        // Assure quality & GxP training surfaces
        if (StartsWithSegment(path, "/api/v1/quality")
            || StartsWithSegment(path, "/api/v1/knowledge")
            || StartsWithSegment(path, "/api/v1/training"))
        {
            return AssureOnly;
        }

        // Cross-cutting AI, notifications, and support tickets require at least one active product family
        if (StartsWithSegment(path, "/api/v1/ai")
            || StartsWithSegment(path, "/api/v1/notifications")
            || StartsWithSegment(path, "/api/v1/tickets"))
        {
            return AllFamilies;
        }

        return null;
    }

    private static IReadOnlyList<ProductFamily>? ResolveKernelFamilies(string path, string method)
    {
        // Baseline trust, authority, audit, onboarding, entitlement, lock, and regulated export surfaces
        if (StartsWithSegment(path, "/api/v1/kernel/onboarding")
            || StartsWithSegment(path, "/api/v1/kernel/entitlements")
            || StartsWithSegment(path, "/api/v1/kernel/locks")
            || StartsWithSegment(path, "/api/v1/kernel/nodes")
            || StartsWithSegment(path, "/api/v1/kernel/authorization")
            || StartsWithSegment(path, "/api/v1/kernel/search")
            || StartsWithSegment(path, "/api/v1/kernel/activity"))
        {
            return null;
        }

        if (string.Equals(path.TrimEnd('/'), "/api/v1/kernel/organizations", StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsGet(method))
        {
            return null;
        }

        if (string.Equals(path.TrimEnd('/'), "/api/v1/kernel/studies", StringComparison.OrdinalIgnoreCase))
        {
            return HttpMethods.IsGet(method) ? null : StudyCreationFamilies;
        }

        if (StartsWithSegment(path, "/api/v1/kernel/studies"))
        {
            string trimmed = path.TrimEnd('/');
            if (trimmed.EndsWith("/locks", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith("/exports", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (trimmed.Contains("/validation", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationReadFamilies;
            }

            if (trimmed.EndsWith("/proposals", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith("/artifacts", StringComparison.OrdinalIgnoreCase))
            {
                return AllFamilies;
            }

            if (trimmed.EndsWith("/effectivities", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith("/sites", StringComparison.OrdinalIgnoreCase))
            {
                return StudyCreationFamilies;
            }

            return BlueprintOnly;
        }

        if (StartsWithSegment(path, "/api/v1/kernel/enrollments"))
        {
            return path.TrimEnd('/').EndsWith("/casebook", StringComparison.OrdinalIgnoreCase)
                ? null
                : CaptureOnly;
        }

        if (StartsWithSegment(path, "/api/v1/kernel/observations")
            || StartsWithSegment(path, "/api/v1/kernel/assertions"))
        {
            return CaptureOnly;
        }

        if (StartsWithSegment(path, "/api/v1/kernel/validation"))
        {
            return ValidationReadFamilies;
        }

        if (StartsWithSegment(path, "/api/v1/kernel/proposals"))
        {
            return AllFamilies;
        }

        if (StartsWithSegment(path, "/api/v1/kernel/effectivities")
            || StartsWithSegment(path, "/api/v1/kernel/study-sites"))
        {
            return StudyCreationFamilies;
        }

        if (StartsWithSegment(path, "/api/v1/kernel/protocol-versions")
            && path.TrimEnd('/').EndsWith("/effectivities", StringComparison.OrdinalIgnoreCase))
        {
            return StudyCreationFamilies;
        }

        if (StartsWithSegment(path, "/api/v1/kernel/protocol-templates")
            || StartsWithSegment(path, "/api/v1/kernel/protocol-versions")
            || StartsWithSegment(path, "/api/v1/kernel/protocols")
            || StartsWithSegment(path, "/api/v1/kernel/simulation")
            || StartsWithSegment(path, "/api/v1/kernel/library-items"))
        {
            return BlueprintOnly;
        }

        return null;
    }

    private static IReadOnlyList<ProductFamily>? ResolveExecutionFamilies(string path, string method)
    {
        string trimmed = path.TrimEnd('/');

        // Closeout electronic signature on an existing form remains baseline
        if (StartsWithSegment(trimmed, "/api/v1/execution/forms")
            && trimmed.EndsWith("/signatures", StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsPost(method))
        {
            return null;
        }

        if (StartsWithSegment(trimmed, "/api/v1/execution/randomization")
            || StartsWithSegment(trimmed, "/api/v1/execution/rtsm")
            || StartsWithSegment(trimmed, "/api/v1/execution/ip-kits")
            || StartsWithSegment(trimmed, "/api/v1/execution/cgt")
            || StartsWithSegment(trimmed, "/api/v1/execution/devices")
            || (StartsWithSegment(trimmed, "/api/v1/execution/subjects")
                && trimmed.EndsWith("/randomize", StringComparison.OrdinalIgnoreCase)))
        {
            return AllocateOnly;
        }

        if (StartsWithSegment(trimmed, "/api/v1/execution/cec"))
        {
            return VigilOnly;
        }

        if (StartsWithSegment(trimmed, "/api/v1/execution/lab-ranges")
            || StartsWithSegment(trimmed, "/api/v1/execution/labs")
            || StartsWithSegment(trimmed, "/api/v1/execution/lab-toxicity"))
        {
            return LensOnly;
        }

        if (StartsWithSegment(trimmed, "/api/v1/execution/webhooks/etmf")
            || StartsWithSegment(trimmed, "/api/v1/execution/sites")
            || StartsWithSegment(trimmed, "/api/v1/execution/site-compliance"))
        {
            return OrbitOnly;
        }

        return CaptureOnly;
    }

    private static bool StartsWithSegment(string path, string prefix) =>
        path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && (path.Length == prefix.Length || path[prefix.Length] == '/');
}
