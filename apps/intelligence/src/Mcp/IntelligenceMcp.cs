using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Tenancy;
using Cadence.Modules.Intelligence.Tools;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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
        return services;
    }

    /// <summary>
    /// The reason any signed-in caller may reach the endpoint (ADR-2516): every tool reads as that caller and is masked
    /// for both the caller and the agent, and no tool writes.
    /// </summary>
    public const string SignedInCallerReason =
        "Read-only agent tools: every read runs as the signed-in caller, masked for both the caller and the agent, and no tool writes.";

    /// <summary>Maps the endpoint for the gateway-signed caller (ADR-2256, ADR-2516).</summary>
    public static IEndpointConventionBuilder MapIntelligenceMcp(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapMcp(Route).RequireSignedInCaller(SignedInCallerReason);
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
}
