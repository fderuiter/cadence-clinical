using System;
using System.Collections.Generic;
using System.IO;
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
/// Enforces contracted <see cref="TenantScopingCaps"/> across all 20 backend HTTP surfaces when the gateway-signed
/// <see cref="GatewayHeaders.TenantScoping"/> posture is present, and translates standalone persistence posture
/// refusals into RFC 9457 <c>403</c> <c>application/problem+json</c> responses.
/// </summary>
public sealed class SubscriptionScopingGuardMiddleware : IMiddleware
{
    private const int MaxInspectedJsonBytes = 65_536;

    private static readonly string RegulatedExportPolicy =
        PermissionPolicyProvider.PolicyName("etmf_document:export");

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (IsExemptRequest(context))
        {
            await InvokeTranslatingPersistenceRefusalsAsync(context, next);
            return;
        }

        if (context.Items[GatewayAuthenticationHandler.PrincipalItemKey] is Principal
            {
                TenantId: { Length: > 0 },
                TenantScoping: { } scoping,
            } principal)
        {
            string? violation = CheckPrincipalScopeCaps(principal, scoping)
                ?? await CheckJsonPayloadScopingAsync(context, scoping);
            if (violation is not null)
            {
                await WriteProblemAsync(
                    context,
                    CommercialErrors.ScopingCapExceeded.Code,
                    "Workspace subscription scoping cap exceeded",
                    violation);
                return;
            }
        }

        await InvokeTranslatingPersistenceRefusalsAsync(context, next);
    }

    private static async Task InvokeTranslatingPersistenceRefusalsAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (InvalidOperationException ex) when (!context.Response.HasStarted && TryMapPersistenceRefusal(ex.Message, out string? code, out string? title))
        {
            await WriteProblemAsync(context, code!, title!, ex.Message);
        }
    }

    private static bool TryMapPersistenceRefusal(string message, out string? code, out string? title)
    {
        if (message.StartsWith(CommercialErrors.ScopingCapExceeded.Code + ":", StringComparison.Ordinal))
        {
            code = CommercialErrors.ScopingCapExceeded.Code;
            title = "Workspace subscription scoping cap exceeded";
            return true;
        }

        if (message.StartsWith(CommercialErrors.ProductFamilyNotEntitled.Code + ":", StringComparison.Ordinal))
        {
            code = CommercialErrors.ProductFamilyNotEntitled.Code;
            title = "Workspace product family not entitled";
            return true;
        }

        if (message.StartsWith(KernelErrors.SubscriptionExpired.Code + ":", StringComparison.Ordinal))
        {
            code = KernelErrors.SubscriptionExpired.Code;
            title = "Trial subscription expired";
            return true;
        }

        code = null;
        title = null;
        return false;
    }

    private static bool IsExemptRequest(HttpContext context)
    {
        string method = context.Request.Method;
        if (HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method)
            || HttpMethods.IsTrace(method))
        {
            return true;
        }

        Endpoint? endpoint = context.GetEndpoint();
        if (endpoint is not null)
        {
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                || endpoint.Metadata.GetMetadata<AllowBaselineProductFamilyMetadata>() is not null
                || endpoint.Metadata.GetMetadata<AllowExpiredTenantWriteMetadata>() is not null)
            {
                return true;
            }

            if (endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Any(authorize => string.Equals(authorize.Policy, RegulatedExportPolicy, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        string path = context.Request.Path.Value ?? string.Empty;
        return path.StartsWith("/api/v1/kernel/entitlements", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/v1/kernel/locks", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/v1/signatures", StringComparison.OrdinalIgnoreCase);
    }

    private static string? CheckPrincipalScopeCaps(Principal principal, TenantScopingCaps scoping)
    {
        int explicitStudies = principal.AssignedStudies
            .Where(static s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s, StudyAccessEvaluator.AllStudies, StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (explicitStudies > scoping.ActiveStudyCount)
        {
            return $"Assigned study scope count ({explicitStudies}) exceeds the workspace subscription's contracted active study cap ({scoping.ActiveStudyCount}).";
        }

        int explicitSites = principal.AssignedSites
            .Where(static s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s, "*", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (explicitSites > scoping.SiteCount)
        {
            return $"Assigned site scope count ({explicitSites}) exceeds the workspace subscription's contracted site cap ({scoping.SiteCount}).";
        }

        return null;
    }

    private static async Task<string?> CheckJsonPayloadScopingAsync(HttpContext context, TenantScopingCaps scoping)
    {
        string? contentType = context.Request.ContentType;
        if (string.IsNullOrWhiteSpace(contentType)
            || !contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || context.Request.ContentLength is <= 0 or > MaxInspectedJsonBytes)
        {
            return null;
        }

        context.Request.EnableBuffering();
        if (!context.Request.Body.CanSeek)
        {
            return null;
        }

        context.Request.Body.Position = 0;
        byte[] buffer;
        using (var ms = new MemoryStream())
        {
            await context.Request.Body.CopyToAsync(ms, context.RequestAborted);
            context.Request.Body.Position = 0;
            if (ms.Length is 0 or > MaxInspectedJsonBytes)
            {
                return null;
            }

            buffer = ms.ToArray();
        }

        try
        {
            using var doc = JsonDocument.Parse(buffer);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return InspectJsonObject(doc.RootElement, scoping);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? InspectJsonObject(JsonElement root, TenantScopingCaps scoping)
    {
        if ((TryGetNonBlankString(root, "country", out string? country)
                || TryGetNonBlankString(root, "country_code", out country))
            && !IsContractedCountry(country!, scoping.Countries))
        {
            return $"Country '{country}' is outside the workspace subscription's contracted countries ({string.Join(", ", scoping.Countries)}).";
        }

        if (root.TryGetProperty("countries", out JsonElement countriesEl) && countriesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in countriesEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String
                    && item.GetString() is { } entry
                    && !string.IsNullOrWhiteSpace(entry)
                    && !IsContractedCountry(entry.Trim(), scoping.Countries))
                {
                    return $"Country '{entry.Trim()}' is outside the workspace subscription's contracted countries ({string.Join(", ", scoping.Countries)}).";
                }
            }
        }

        if ((TryGetNonBlankString(root, "study_phase", out string? phase)
                || TryGetNonBlankString(root, "phase", out phase))
            && !string.Equals(phase, scoping.StudyPhase, StringComparison.OrdinalIgnoreCase))
        {
            return $"Study phase '{phase}' does not match the workspace subscription's contracted phase ('{scoping.StudyPhase}').";
        }

        if ((TryGetPositiveInt(root, "study_duration_months", out int duration)
                || TryGetPositiveInt(root, "planned_duration_months", out duration))
            && duration > scoping.StudyDurationMonths)
        {
            return $"Study duration ({duration} months) exceeds the workspace subscription's contracted cap ({scoping.StudyDurationMonths} months).";
        }

        if ((TryGetPositiveInt(root, "planned_sites", out int sites)
                || TryGetPositiveInt(root, "target_site_count", out sites)
                || TryGetPositiveInt(root, "site_count", out sites))
            && sites > scoping.SiteCount)
        {
            return $"Planned site count ({sites}) exceeds the workspace subscription's contracted cap ({scoping.SiteCount}).";
        }

        if ((TryGetPositiveInt(root, "planned_enrollment", out int participants)
                || TryGetPositiveInt(root, "target_enrollment", out participants)
                || TryGetPositiveInt(root, "participant_count", out participants))
            && participants > scoping.ParticipantCount)
        {
            return $"Planned participant count ({participants}) exceeds the workspace subscription's contracted cap ({scoping.ParticipantCount}).";
        }

        if (TryGetPositiveInt(root, "active_study_count", out int activeStudies)
            && activeStudies > scoping.ActiveStudyCount)
        {
            return $"Active study count ({activeStudies}) exceeds the workspace subscription's contracted cap ({scoping.ActiveStudyCount}).";
        }

        return null;
    }

    private static bool IsContractedCountry(string candidate, IReadOnlyList<string> allowed) =>
        allowed.Any(c => string.Equals(c, candidate, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetNonBlankString(JsonElement obj, string propertyName, out string? value)
    {
        if (obj.TryGetProperty(propertyName, out JsonElement prop)
            && prop.ValueKind == JsonValueKind.String
            && prop.GetString() is { } raw
            && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw.Trim();
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetPositiveInt(JsonElement obj, string propertyName, out int value)
    {
        if (obj.TryGetProperty(propertyName, out JsonElement prop)
            && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt32(out int parsed)
            && parsed > 0)
        {
            value = parsed;
            return true;
        }

        value = 0;
        return false;
    }

    private static Task WriteProblemAsync(HttpContext context, string code, string title, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        var explanation = RbacExplanationBuilder.ForSubscriptionDenial(
            detail,
            "Stay within the workspace subscription's contracted study, site, country, and participant scoping caps.",
            "Ask a workspace administrator to amend the workspace subscription scoping caps.",
            "subscription.amend");
        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            new
            {
                type = $"https://cadence.clinical/errors/{code}",
                title,
                status = StatusCodes.Status403Forbidden,
                detail,
                code,
                explanation = ProblemExplanation.From(explanation),
            },
            cancellationToken: context.RequestAborted);
    }
}
