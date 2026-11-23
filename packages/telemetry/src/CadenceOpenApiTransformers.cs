using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;

namespace Cadence.Telemetry;

/// <summary>
/// Shared OpenAPI document and operation transformers applied across all Cadence hosts and modules.
/// Projects <c>BearerAuth</c> security schemes, RBAC permissions, clinical scopes, rate-limit policies,
/// and GxP request headers (<c>X-Sig-Token</c>, <c>Idempotency-Key</c>, <c>X-Change-Reason</c>) while
/// excluding YARP reverse-proxy catch-all forwarding routes.
/// </summary>
public static class CadenceOpenApiTransformers
{
    public const string BearerSchemeName = "BearerAuth";
    public const string AuthExtension = "x-cadence-auth";
    public const string PermissionsExtension = "x-cadence-permissions";
    public const string ScopesExtension = "x-cadence-scopes";
    public const string RateLimitExtension = "x-cadence-rate-limit";

    private const string PermissionPolicyPrefix = "Permission:";
    private const string SignedInCallerPolicy = "SignedInCaller";
    private const string ChangeJustificationPolicy = "RequireChangeJustification";

    private const string SigTokenHeader = "X-Sig-Token";
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string ChangeReasonHeader = "X-Change-Reason";

    private const string SigTokenDescription =
        "Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)).";

    private const string RequiredIdempotencyKeyDescription =
        "Caller-supplied command idempotency key (K-TX-3) preventing duplicate clinical writes on retry.";

    private const string OptionalIdempotencyKeyDescription =
        "Optional caller-supplied idempotency key (max 255 characters) ensuring safe replay across retries.";

    private const string RequiredChangeReasonDescription =
        "Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8.";

    private const string OptionalChangeReasonDescription =
        "Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8.";

    public static void Configure(OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Func<ApiDescription, bool>? innerFilter = options.ShouldInclude;
        options.ShouldInclude = description =>
            !IsReverseProxyCatchAll(description) && (innerFilter is null || innerFilter(description));

        options.AddDocumentTransformer(TransformDocumentAsync);
        options.AddOperationTransformer(TransformOperationAsync);
    }

    internal static bool IsReverseProxyCatchAll(ApiDescription description)
    {
        if (description.RelativePath?.Contains("{**catch-all}", StringComparison.OrdinalIgnoreCase) is true)
        {
            return true;
        }

        var metadata = description.ActionDescriptor?.EndpointMetadata;
        if (metadata is null)
        {
            return false;
        }

        return metadata.Any(item =>
            string.Equals(item.GetType().FullName, "Yarp.ReverseProxy.Model.RouteModel", StringComparison.Ordinal));
    }

    private static Task TransformDocumentAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[BearerSchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description =
                "Keycloak OIDC bearer JWT verified at the Cadence API Gateway and forwarded downstream as HMAC-SHA256 signed identity headers (ADR-2256).",
        };

        return Task.CompletedTask;
    }

    private static Task TransformOperationAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor?.EndpointMetadata ?? [];
        bool isAnonymous = metadata.OfType<IAllowAnonymous>().Any();

        var authorizeItems = metadata.OfType<IAuthorizeData>().ToList();
        var permissions = new List<string>();
        var scopes = new List<string>();
        bool signedInCaller = false;
        bool requiresChangeJustification = false;

        foreach (var auth in authorizeItems)
        {
            if (string.IsNullOrWhiteSpace(auth.Policy))
            {
                continue;
            }

            string policy = auth.Policy;
            if (policy.StartsWith(PermissionPolicyPrefix, StringComparison.Ordinal))
            {
                string permission = policy[PermissionPolicyPrefix.Length..];
                if (!permissions.Contains(permission, StringComparer.Ordinal))
                {
                    permissions.Add(permission);
                }
            }
            else if (string.Equals(policy, SignedInCallerPolicy, StringComparison.Ordinal))
            {
                signedInCaller = true;
            }
            else if (string.Equals(policy, ChangeJustificationPolicy, StringComparison.Ordinal))
            {
                requiresChangeJustification = true;
            }
        }

        string? signedInReason = null;
        bool allowExpiredTenantWrite = false;
        bool requiresElectronicSignature = false;
        var explicitHeaders = new List<(string Name, bool Required, string Description, int? MaxLength)>();

        foreach (object item in metadata)
        {
            Type itemType = item.GetType();
            switch (itemType.Name)
            {
                case "SignedInCallerReason":
                    signedInReason = itemType.GetProperty("Reason")?.GetValue(item) as string;
                    break;
                case "EndpointScopeMetadata":
                    {
                        string? kind = itemType.GetProperty("Kind")?.GetValue(item)?.ToString();
                        string? routeParam = itemType.GetProperty("RouteParameter")?.GetValue(item) as string;
                        if (!string.IsNullOrWhiteSpace(kind))
                        {
                            string scopeEntry = string.IsNullOrWhiteSpace(routeParam) ? kind : $"{kind}:{routeParam}";
                            if (!scopes.Contains(scopeEntry, StringComparer.Ordinal))
                            {
                                scopes.Add(scopeEntry);
                            }
                        }

                        break;
                    }
                case "AllowExpiredTenantWriteMetadata":
                    allowExpiredTenantWrite = true;
                    break;
                case "ElectronicSignatureRequired":
                    requiresElectronicSignature = true;
                    break;
                case "EndpointHeaderMetadata":
                    {
                        string? name = itemType.GetProperty("Name")?.GetValue(item) as string;
                        bool required = itemType.GetProperty("Required")?.GetValue(item) is true;
                        string? description = itemType.GetProperty("Description")?.GetValue(item) as string;
                        int? maxLength = itemType.GetProperty("MaxLength")?.GetValue(item) as int?;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            explicitHeaders.Add((name, required, description ?? string.Empty, maxLength));
                        }

                        break;
                    }
                case "KernelErrorsMetadata":
                    InspectKernelErrorsMetadata(item, itemType, explicitHeaders);
                    break;
            }
        }

        string? rateLimitDescription = ResolveRateLimitDescription(metadata);

        if (!isAnonymous)
        {
            operation.Security ??= [];
            if (operation.Security.Count == 0)
            {
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(BearerSchemeName, context.Document)] = [],
                });
            }
        }

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        string authMode = ResolveAuthMode(isAnonymous, permissions.Count, signedInCaller);

        var authNode = new JsonObject
        {
            ["anonymous"] = isAnonymous,
            ["mode"] = authMode,
        };
        if (!string.IsNullOrWhiteSpace(signedInReason))
        {
            authNode["signed_in_reason"] = signedInReason;
        }

        if (allowExpiredTenantWrite)
        {
            authNode["allow_expired_tenant_write"] = true;
        }

        operation.Extensions[AuthExtension] = new JsonNodeExtension(authNode);

        if (permissions.Count > 0)
        {
            permissions.Sort(StringComparer.Ordinal);
            operation.Extensions[PermissionsExtension] = new JsonNodeExtension(
                new JsonArray([.. permissions.Select(p => (JsonNode)JsonValue.Create(p)!)]));
        }

        if (scopes.Count > 0)
        {
            operation.Extensions[ScopesExtension] = new JsonNodeExtension(
                new JsonArray([.. scopes.Select(s => (JsonNode)JsonValue.Create(s)!)]));
        }

        if (!string.IsNullOrWhiteSpace(rateLimitDescription))
        {
            operation.Extensions[RateLimitExtension] = new JsonNodeExtension(JsonValue.Create(rateLimitDescription)!);
        }

        bool usesKernelChangeReason = HandlerUsesKernelChangeReason(metadata);

        if (requiresElectronicSignature)
        {
            UpsertHeaderParameter(operation, SigTokenHeader, required: true, SigTokenDescription, maxLength: null);
        }

        if (usesKernelChangeReason)
        {
            UpsertHeaderParameter(operation, IdempotencyKeyHeader, required: true, RequiredIdempotencyKeyDescription, maxLength: 255);
            UpsertHeaderParameter(operation, ChangeReasonHeader, required: true, RequiredChangeReasonDescription, maxLength: null);
        }

        if (requiresChangeJustification)
        {
            UpsertHeaderParameter(operation, ChangeReasonHeader, required: true, RequiredChangeReasonDescription, maxLength: null);
        }

        foreach (var header in explicitHeaders)
        {
            UpsertHeaderParameter(operation, header.Name, header.Required, header.Description, header.MaxLength);
        }

        EnrichExistingBoundGxpHeaders(operation);
        EnrichNonJsonProducesResponses(operation, metadata);
        return Task.CompletedTask;
    }

    private static void EnrichNonJsonProducesResponses(OpenApiOperation operation, IList<object> metadata)
    {
        var producesItems = metadata.OfType<IProducesResponseTypeMetadata>().ToList();
        if (producesItems.Count == 0)
        {
            return;
        }

        operation.Responses ??= new OpenApiResponses();
        foreach (var produces in producesItems)
        {
            if (produces.StatusCode <= 0)
            {
                continue;
            }

            string statusKey = produces.StatusCode.ToString(CultureInfo.InvariantCulture);
            if (!operation.Responses.TryGetValue(statusKey, out var existingResponse) || existingResponse is not OpenApiResponse response)
            {
                string reasonPhrase = ReasonPhrases.GetReasonPhrase(produces.StatusCode);
                response = new OpenApiResponse
                {
                    Description = string.IsNullOrWhiteSpace(reasonPhrase) ? "Response" : reasonPhrase,
                };
                operation.Responses[statusKey] = response;
            }

            foreach (string rawContentType in produces.ContentTypes)
            {
                if (string.IsNullOrWhiteSpace(rawContentType))
                {
                    continue;
                }

                string mediaType = rawContentType.Split(';', 2)[0].Trim();
                if (IsBinaryMediaType(mediaType))
                {
                    response.Content ??= new Dictionary<string, OpenApiMediaType>(StringComparer.OrdinalIgnoreCase);
                    response.Content[mediaType] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Format = "binary",
                        },
                    };
                }
                else if (IsTextualNonJsonMediaType(mediaType))
                {
                    response.Content ??= new Dictionary<string, OpenApiMediaType>(StringComparer.OrdinalIgnoreCase);
                    response.Content[mediaType] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                        },
                    };
                }
            }
        }
    }

    private static bool IsBinaryMediaType(string mediaType) =>
        string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "application/zip", StringComparison.OrdinalIgnoreCase);

    private static bool IsTextualNonJsonMediaType(string mediaType) =>
        string.Equals(mediaType, "text/csv", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "application/xml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "text/xml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "text/plain", StringComparison.OrdinalIgnoreCase);

    private static bool HandlerUsesKernelChangeReason(IList<object> metadata)
    {
        foreach (var method in metadata.OfType<MethodInfo>())
        {
            foreach (var parameter in method.GetParameters())
            {
                if (IsOrContainsKernelChangeReason(parameter.ParameterType))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void InspectKernelErrorsMetadata(
        object item,
        Type itemType,
        List<(string Name, bool Required, string Description, int? MaxLength)> explicitHeaders)
    {
        if (itemType.GetProperty("Errors")?.GetValue(item) is not System.Collections.IEnumerable errors)
        {
            return;
        }

        foreach (object? err in errors)
        {
            if (err is null)
            {
                continue;
            }

            string? code = err.GetType().GetProperty("Code")?.GetValue(err) as string;
            if (string.Equals(code, "K.Command.IdempotencyKeyRequired", StringComparison.Ordinal))
            {
                explicitHeaders.Add((IdempotencyKeyHeader, true, RequiredIdempotencyKeyDescription, 255));
            }
            else if (string.Equals(code, "K.Command.ReasonForChangeRequired", StringComparison.Ordinal))
            {
                explicitHeaders.Add((ChangeReasonHeader, true, RequiredChangeReasonDescription, null));
            }
        }
    }

    private static bool IsOrContainsKernelChangeReason(Type type)
    {
        if (string.Equals(type.Name, "IKernelChangeReason", StringComparison.Ordinal))
        {
            return true;
        }

        if (!type.IsClass && !type.IsValueType)
        {
            return false;
        }

        if (type.Namespace?.StartsWith("Cadence.", StringComparison.Ordinal) is not true)
        {
            return false;
        }

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (string.Equals(prop.PropertyType.Name, "IKernelChangeReason", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ResolveRateLimitDescription(IList<object> metadata)
    {
        var rateLimitAttr = metadata.OfType<EnableRateLimitingAttribute>().FirstOrDefault();
        if (rateLimitAttr is null || string.IsNullOrWhiteSpace(rateLimitAttr.PolicyName))
        {
            return null;
        }

        return rateLimitAttr.PolicyName switch
        {
            "signature-verification" => "5 requests/minute per authenticated signer (FixedWindow; answers 429 Too Many Requests with Retry-After).",
            _ => $"Rate-limited ({rateLimitAttr.PolicyName}; answers 429 Too Many Requests with Retry-After).",
        };
    }

    private static string ResolveAuthMode(bool isAnonymous, int permissionCount, bool signedInCaller)
    {
        if (isAnonymous)
        {
            return "Anonymous";
        }

        if (permissionCount > 0)
        {
            return "Permission";
        }

        return signedInCaller ? "SignedInCaller" : "Authenticated";
    }

    private static void UpsertHeaderParameter(
        OpenApiOperation operation,
        string name,
        bool required,
        string description,
        int? maxLength)
    {
        operation.Parameters ??= [];
        var existing = operation.Parameters.FirstOrDefault(p =>
            p.In == ParameterLocation.Header && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is OpenApiParameter concrete)
        {
            concrete.Name = name;
            concrete.Required |= required;
            if (string.IsNullOrWhiteSpace(concrete.Description))
            {
                concrete.Description = description;
            }

            if (concrete.Schema is OpenApiSchema existingSchema && maxLength.HasValue && existingSchema.MaxLength is null)
            {
                existingSchema.MaxLength = maxLength;
            }

            return;
        }

        if (existing is not null)
        {
            return;
        }

        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            MinLength = required ? 1 : null,
            MaxLength = maxLength,
        };

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = required,
            Description = description,
            Schema = schema,
        });
    }

    private static void EnrichExistingBoundGxpHeaders(OpenApiOperation operation)
    {
        if (operation.Parameters is null)
        {
            return;
        }

        foreach (var parameter in operation.Parameters)
        {
            if (parameter is not OpenApiParameter concrete || concrete.In != ParameterLocation.Header)
            {
                continue;
            }

            if (string.Equals(concrete.Name, ChangeReasonHeader, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(concrete.Description))
            {
                concrete.Description = concrete.Required
                    ? RequiredChangeReasonDescription
                    : OptionalChangeReasonDescription;
            }
            else if (string.Equals(concrete.Name, IdempotencyKeyHeader, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(concrete.Description))
                {
                    concrete.Description = concrete.Required
                        ? RequiredIdempotencyKeyDescription
                        : OptionalIdempotencyKeyDescription;
                }

                if (concrete.Schema is OpenApiSchema schema && schema.MaxLength is null)
                {
                    schema.MaxLength = 255;
                }
            }
            else if (string.Equals(concrete.Name, SigTokenHeader, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(concrete.Description))
            {
                concrete.Description = SigTokenDescription;
            }
        }
    }
}
