using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Cadence.Security.Authentication;

/// <summary>
/// Endpoint metadata marking commercial onboarding or activation routes that remain writable when a tenant's
/// trial or subscription is expired so administrators can reactivate in place.
/// </summary>
public sealed class AllowExpiredTenantWriteMetadata
{
    /// <summary>The shared singleton instance.</summary>
    public static AllowExpiredTenantWriteMetadata Instance { get; } = new();

    private AllowExpiredTenantWriteMetadata()
    {
    }
}

/// <summary>
/// Rejects mutating HTTP requests (<c>POST</c>, <c>PUT</c>, <c>PATCH</c>, <c>DELETE</c>) from a signed read-only
/// (expired-subscription) tenant with RFC 9457 <c>403 K.Subscription.Expired</c>, while preserving regulated
/// read access (<c>GET</c>, <c>HEAD</c>), regulated eTMF exports (<c>etmf_document:export</c>), and explicit
/// closeout/commercial endpoints marked with <see cref="AllowExpiredTenantWriteMetadata"/>.
/// @req:PRD-SYS-053
/// </summary>
public sealed class ExpiredSubscriptionWriteGuardMiddleware : IMiddleware
{
    public const string SubscriptionExpiredCode = "K.Subscription.Expired";

    private static readonly string RegulatedExportPolicy =
        PermissionPolicyProvider.PolicyName("etmf_document:export");

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        string method = context.Request.Method;
        bool isWrite = HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

        if (isWrite
            && context.Items[GatewayAuthenticationHandler.PrincipalItemKey] is Principal { TenantReadOnly: true }
            && !IsExemptEndpoint(context.GetEndpoint()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";

            await WriteProblemDetailsAsync(context).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool IsExemptEndpoint(Endpoint? endpoint)
    {
        if (endpoint is null)
        {
            return false;
        }

        if (endpoint.Metadata.GetMetadata<AllowExpiredTenantWriteMetadata>() is not null)
        {
            return true;
        }

        return endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Any(authorize => string.Equals(authorize.Policy, RegulatedExportPolicy, StringComparison.Ordinal));
    }

    private static Task WriteProblemDetailsAsync(HttpContext context)
    {
        var explanation = RbacExplanationBuilder.ForSubscriptionDenial(
            KernelErrors.SubscriptionExpired.Message,
            "Renew or activate the workspace subscription before creating or updating clinical records.",
            "Ask a workspace administrator to activate a commercial subscription.",
            "subscription.activate");

        var payload = new System.Collections.Generic.Dictionary<string, object?>
        {
            ["type"] = "https://tools.ietf.org/html/rfc9110#section-15.5.4", // deid-ignore
            ["title"] = KernelErrors.SubscriptionExpired.Code,
            ["status"] = StatusCodes.Status403Forbidden,
            ["detail"] = KernelErrors.SubscriptionExpired.Message,
            ["code"] = KernelErrors.SubscriptionExpired.Code,
            ["explanation"] = ProblemExplanation.From(explanation),
        };
        if (context.Request.Path.HasValue)
        {
            payload["instance"] = context.Request.Path.Value;
        }

        return JsonSerializer.SerializeAsync(context.Response.Body, payload, cancellationToken: context.RequestAborted);
    }
}
