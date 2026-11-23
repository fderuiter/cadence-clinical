using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Tenancy;
using Cadence.Modules.Intelligence.Tools;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Cadence.Modules.Intelligence.Mcp;

/// <summary>
/// The MCP server for outside agents (ADR-2633 §4.8): the same governed read tools, visibility and untrusted-data
/// envelope as in-app agents, over the Streamable HTTP transport. The signed-in caller is the requester, the agent is
/// <see cref="AgentName"/>, and the allow-list is <see cref="KernelToolNames.McpProfile"/>. It exposes no write tool.
/// </summary>
public static class IntelligenceMcp
{
    /// <summary>Where the endpoint is mapped.</summary>
    public const string Route = "/api/v1/ai/mcp";

    /// <summary>The agent name outside callers act through (<c>agent:mcp</c>).</summary>
    public const string AgentName = "mcp";

    /// <summary>OpenAPI operation identifier for the Streamable HTTP MCP endpoint.</summary>
    public const string OperationId = "IntelligenceMcpStreamableHttp";

    /// <summary>OpenAPI summary for <see cref="Route"/>.</summary>
    public const string OperationSummary =
        "Executes stateless Streamable HTTP Model Context Protocol (MCP) JSON-RPC 2.0 requests for governed read-only clinical tools.";

    /// <summary>OpenAPI description covering transport, authentication, and rate-limit rules for <see cref="Route"/>.</summary>
    public const string OperationDescription =
        "Stateless Streamable HTTP MCP transport (ADR-2633 §4.8) accepting JSON-RPC 2.0 requests (initialize, notifications/initialized, ping, tools/list, tools/call) "
        + "and answering with application/json or text/event-stream envelopes. Requires a gateway-verified OIDC BearerAuth token (SignedInCaller): "
        + "every tool executes as the signed-in caller acting through agent:mcp over KernelToolNames.McpProfile, masked in place for both caller and agent (K-AUTH-4, ADR-2471), "
        + "with zero write capabilities. Subject-partitioned fixed-window gateway rate limiting applies (GATEWAY_RATE_LIMIT_PER_MINUTE, default 600 requests/minute per authenticated sub; 429 Too Many Requests with Retry-After).";

    /// <summary>Registers the MCP server. Call <see cref="IntelligenceToolsServiceCollectionExtensions.AddIntelligenceTools"/> too.</summary>
    public static IServiceCollection AddIntelligenceMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "cadence",
                    Title = "Cadence clinical research",
                    Version = typeof(IntelligenceMcp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0",
                };
                options.ServerInstructions =
                    "Read-only access to Cadence clinical records, as the signed-in person and never wider. " + ToolDataPolicy.SystemNotice;
            })
            .WithHttpTransport(options => options.Stateless = true)
            .WithListToolsHandler(ListToolsAsync)
            .WithCallToolHandler(CallToolAsync);

        services.Configure<OpenApiOptions>("kernel", options => options.AddDocumentTransformer(TransformKernelOpenApiAsync));
        return services;
    }

    /// <summary>
    /// The reason any signed-in caller may reach the endpoint (ADR-2516): every tool reads as that caller and is masked
    /// for both the caller and the agent, and no tool writes.
    /// </summary>
    public const string SignedInCallerReason =
        "Read-only agent tools: every read runs as the signed-in caller, masked for both the caller and the agent, and no tool writes.";

    /// <summary>Maps the endpoint for the gateway-signed caller (ADR-2256, ADR-2516) with OpenAPI metadata.</summary>
    public static IEndpointConventionBuilder MapIntelligenceMcp(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints
            .MapMcp(Route)
            .RequireSignedInCaller(SignedInCallerReason)
            .WithMetadata(
                IntelligenceMcpEndpointMarker.Instance,
                new TagsAttribute("Intelligence"),
                new EndpointSummaryAttribute(OperationSummary),
                new EndpointDescriptionAttribute(OperationDescription));
    }

    internal static ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var catalog = Services(request).GetRequiredService<AgentToolCatalog>();
        var tools = catalog.ToolsFor(Profile(catalog))
            .Select(t => new Tool
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = t.InputSchema,
                Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false },
            })
            .ToList();
        return ValueTask.FromResult(new ListToolsResult { Tools = tools });
    }

    internal static async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var services = Services(request);
        var name = request.Params?.Name ?? string.Empty;
        if (await PrincipalAsync(request.User, services, cancellationToken).ConfigureAwait(false) is not { } principal)
        {
            return Result(ToolResultEnvelope.WrapAsElement(name, ToolOutcome.Failure(ToolErrors.NotFoundOrNotVisible, "Your sign-in names no active tenant.")));
        }

        var catalog = services.GetRequiredService<AgentToolCatalog>();
        var session = catalog.OpenSession(principal, Profile(catalog));
        var arguments = JsonSerializer.SerializeToElement(
            new Dictionary<string, JsonElement>(request.Params?.Arguments ?? new Dictionary<string, JsonElement>(), StringComparer.Ordinal),
            IntelligenceToolsJsonContext.Default.DictionaryStringJsonElement);
        return Result(await session.InvokeAsync(name, arguments, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The caller as requester and <c>agent:mcp</c> as the agent, or null when the sign-in names no active tenant.</summary>
    internal static async Task<AgentPrincipal?> PrincipalAsync(ClaimsPrincipal? user, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (user?.Identity?.IsAuthenticated != true
            || user.FindFirst(CadenceClaimTypes.Subject)?.Value is not { Length: > 0 } subject
            || user.FindFirst(CadenceClaimTypes.TenantId)?.Value is not { Length: > 0 } code)
        {
            return null;
        }

        var tenant = await services.GetRequiredService<ITenantDirectory>().FindActiveByCodeAsync(code, cancellationToken).ConfigureAwait(false);
        return tenant is { } found ? new AgentPrincipal(found, Actor.AutomationAgent(AgentName), Actor.Person(subject), Guid.CreateVersion7()) : null;
    }

    private static ToolAllowList Profile(AgentToolCatalog catalog) =>
        ToolAllowList.Create(KernelToolNames.McpProfile.Where(catalog.Contains), catalog);

    private static IServiceProvider Services(MessageContext request) =>
        request.Services ?? throw new InvalidOperationException("The MCP request carries no service provider.");

    private static CallToolResult Result(JsonElement envelope) => new()
    {
        Content = [new TextContentBlock { Text = envelope.GetRawText() }],
        StructuredContent = envelope,
        IsError = envelope.TryGetProperty("error", out _),
    };

    private sealed class IntelligenceMcpEndpointMarker
    {
        public static IntelligenceMcpEndpointMarker Instance { get; } = new();
    }

    private static Task TransformKernelOpenApiAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        bool isMapped = context.ApplicationServices
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .Any(ep => ep.Metadata.GetMetadata<IntelligenceMcpEndpointMarker>() is not null);
        if (!isMapped)
        {
            return Task.CompletedTask;
        }

        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        RegisterMcpSchemas(document, document.Components.Schemas);

        document.Tags ??= new HashSet<OpenApiTag>();
        if (document.Tags.All(t => !string.Equals(t.Name, "Intelligence", StringComparison.Ordinal)))
        {
            document.Tags.Add(new OpenApiTag
            {
                Name = "Intelligence",
                Description = "Governed Streamable HTTP MCP server for read-only clinical agent tools (ADR-2633).",
            });
        }

        document.Paths ??= new OpenApiPaths();
        if (!document.Paths.TryGetValue(Route, out var existingPath) || existingPath is not OpenApiPathItem pathItem)
        {
            pathItem = new OpenApiPathItem();
            document.Paths[Route] = pathItem;
        }

        pathItem.Operations ??= [];
        pathItem.Operations[HttpMethod.Post] = BuildMcpPostOperation(document);
        return Task.CompletedTask;
    }

    private static void RegisterMcpSchemas(OpenApiDocument document, IDictionary<string, IOpenApiSchema> schemas)
    {
        if (!schemas.ContainsKey("ProblemDetails"))
        {
            schemas["ProblemDetails"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "RFC 7807 / RFC 9457 problem details payload.",
                Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
                {
                    ["type"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                    ["title"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                    ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null, Format = "int32" },
                    ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                    ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                },
            };
        }

        schemas["McpToolCallParams"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "Parameters for a JSON-RPC 2.0 tools/call invocation against KernelToolNames.McpProfile.",
            Required = new HashSet<string>(StringComparer.Ordinal) { "name" },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["name"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "Registered read-only tool identifier in KernelToolNames.McpProfile.",
                },
                ["arguments"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    AdditionalPropertiesAllowed = true,
                    Description = "Tool-specific JSON arguments validated against the tool's InputSchema.",
                },
            },
        };

        schemas["McpJsonRpcRequest"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "JSON-RPC 2.0 request or notification message sent over the Streamable HTTP MCP transport.",
            Required = new HashSet<string>(StringComparer.Ordinal) { "jsonrpc", "method" },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["jsonrpc"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = [JsonValue.Create("2.0")!],
                    Description = "JSON-RPC protocol version (must be \"2.0\").",
                },
                ["id"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String | JsonSchemaType.Integer | JsonSchemaType.Null,
                    Description = "Caller request correlation identifier (omitted on notifications such as notifications/initialized).",
                },
                ["method"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum =
                    [
                        JsonValue.Create("initialize")!,
                        JsonValue.Create("notifications/initialized")!,
                        JsonValue.Create("ping")!,
                        JsonValue.Create("tools/list")!,
                        JsonValue.Create("tools/call")!,
                    ],
                    Description = "MCP JSON-RPC method name.",
                },
                ["params"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object | JsonSchemaType.Null,
                    AdditionalPropertiesAllowed = true,
                    Description = "Method parameters (for tools/call, conforms to McpToolCallParams).",
                },
            },
        };

        schemas["McpJsonRpcError"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "JSON-RPC 2.0 error object returned when protocol parsing or method dispatch fails.",
            Required = new HashSet<string>(StringComparer.Ordinal) { "code", "message" },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["code"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                ["message"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["data"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object | JsonSchemaType.Null,
                    AdditionalPropertiesAllowed = true,
                },
            },
        };

        schemas["McpJsonRpcResponse"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "JSON-RPC 2.0 response envelope carrying governed read-only tool metadata or ToolResultEnvelope content.",
            Required = new HashSet<string>(StringComparer.Ordinal) { "jsonrpc", "id" },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["jsonrpc"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = [JsonValue.Create("2.0")!],
                },
                ["id"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String | JsonSchemaType.Integer | JsonSchemaType.Null,
                },
                ["result"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object | JsonSchemaType.Null,
                    AdditionalPropertiesAllowed = true,
                    Description = "Method result payload (InitializeResult, ListToolsResult, or CallToolResult with ToolResultEnvelope).",
                },
                ["error"] = new OpenApiSchemaReference("McpJsonRpcError", document),
            },
        };
    }

    private static OpenApiOperation BuildMcpPostOperation(OpenApiDocument document)
    {
        var problemMedia = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
        {
            ["application/problem+json"] = new OpenApiMediaType
            {
                Schema = new OpenApiSchemaReference("ProblemDetails", document),
            },
        };

        return new OpenApiOperation
        {
            OperationId = OperationId,
            Summary = OperationSummary,
            Description = OperationDescription,
            Tags = new HashSet<OpenApiTagReference> { new("Intelligence", document) },
            Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("BearerAuth", document)] = [],
                },
            ],
            RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description = "JSON-RPC 2.0 MCP message (initialize, ping, tools/list, tools/call, or notification).",
                Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchemaReference("McpJsonRpcRequest", document),
                    },
                },
            },
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Description = "JSON-RPC 2.0 MCP response envelope (application/json or text/event-stream).",
                    Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchemaReference("McpJsonRpcResponse", document),
                        },
                        ["text/event-stream"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
                                Description = "Server-Sent Events stream of JSON-RPC 2.0 MCP messages.",
                            },
                        },
                    },
                },
                ["202"] = new OpenApiResponse
                {
                    Description = "Accepted JSON-RPC 2.0 client notification with no response body.",
                },
                ["400"] = new OpenApiResponse
                {
                    Description = "Malformed JSON-RPC 2.0 payload or unsupported Streamable HTTP MCP transport headers.",
                    Content = problemMedia,
                },
                ["401"] = new OpenApiResponse
                {
                    Description = "Missing or invalid gateway-verified OIDC BearerAuth credentials.",
                    Content = problemMedia,
                },
                ["403"] = new OpenApiResponse
                {
                    Description = "Caller is denied by gateway authorization or auditor/tenant guardrails.",
                    Content = problemMedia,
                },
                ["429"] = new OpenApiResponse
                {
                    Description = "Caller exceeded the per-subject gateway fixed-window rate limit (includes Retry-After header).",
                    Content = problemMedia,
                },
            },
            Extensions = new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal)
            {
                ["x-cadence-auth"] = new JsonNodeExtension(new JsonObject
                {
                    ["anonymous"] = false,
                    ["mode"] = "SignedInCaller",
                    ["signed_in_reason"] = SignedInCallerReason,
                }),
                ["x-cadence-scopes"] = new JsonNodeExtension(new JsonArray(JsonValue.Create("Tenant")!)),
                ["x-cadence-rate-limit"] = new JsonNodeExtension(
                    JsonValue.Create("600 requests/minute per authenticated subject (GATEWAY_RATE_LIMIT_PER_MINUTE fixed window; answers 429 with Retry-After)")!),
            },
        };
    }
}
