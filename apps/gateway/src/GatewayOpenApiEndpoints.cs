using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Telemetry;
using Cadence.ToolingCore.Services;
using Cadence.Topology;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Scalar.AspNetCore;

namespace Cadence.Apps.Gateway;

/// <summary>
/// Serves unified (<c>/openapi/cdisc.json</c>) and per-contract (<c>/openapi/{contract}.json</c>) OpenAPI documents
/// alongside Scalar interactive documentation on the API gateway in <c>Development</c>, rewriting <c>servers</c>
/// from the active request scheme, host, and forwarded path base without modifying YARP <c>/api/v1/*</c> routes.
/// </summary>
public static class GatewayOpenApiEndpoints
{
    public const string UnifiedDocumentName = "cdisc";

    private const string UpstreamOpenApiHttpClientName = "GatewayUpstreamOpenApi";
    private const string SpecFileSuffix = "_openapi.json";

    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    private static readonly IReadOnlyDictionary<string, (string ServiceName, string UpstreamDocument)> KernelModuleContracts =
        new Dictionary<string, (string ServiceName, string UpstreamDocument)>(StringComparer.Ordinal)
        {
            ["kernel"] = ("kernel", "kernel"),
            ["intelligence"] = ("kernel", "intelligence"),
            ["execution_forms"] = ("kernel", "execution_forms"),
        };

    public static WebApplicationBuilder AddGatewayDevelopmentOpenApi(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.Environment.IsDevelopment())
        {
            return builder;
        }

        builder.Services.AddHttpClient(UpstreamOpenApiHttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        string? specDir = TryFindOpenApiDirectory(builder.Environment.ContentRootPath);
        IReadOnlyList<string> contracts = DiscoverContractNames(specDir);

        builder.Services.Configure<ScalarOptions>(options =>
        {
            options.AddDocument(UnifiedDocumentName, "Cadence Clinical Unified Gateway API");
            options.AddDocument("v1", "Cadence API Gateway (Host)");
            foreach (string contract in contracts)
            {
                options.AddDocument(contract, $"Cadence {contract} Contract");
            }
        });

        return builder;
    }

    public static WebApplication MapGatewayDevelopmentOpenApi(
        this WebApplication app,
        Func<CadenceService, string> resolveServiceUrl)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(resolveServiceUrl);

        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        string? specDir = TryFindOpenApiDirectory(app.Environment.ContentRootPath);
        IReadOnlyList<string> contracts = DiscoverContractNames(specDir);

        app.MapGet(
                $"/openapi/{UnifiedDocumentName}.json",
                async (HttpContext httpContext, IConfiguration configuration, IHttpClientFactory httpClientFactory, CancellationToken ct) =>
                {
                    JsonObject? spec = await LoadUnifiedSpecAsync(specDir, contracts, resolveServiceUrl, httpClientFactory, ct);
                    if (spec is null)
                    {
                        return Results.NotFound();
                    }

                    RewriteServersForActiveRequest(spec, httpContext, configuration);
                    return Results.Content(spec.ToJsonString(IndentedJsonOptions), "application/json; charset=utf-8");
                })
            .ExcludeFromDescription()
            .AllowAnonymous();

        foreach (string contract in contracts)
        {
            string capturedContract = contract;
            app.MapGet(
                    $"/openapi/{capturedContract}.json",
                    async (HttpContext httpContext, IConfiguration configuration, IHttpClientFactory httpClientFactory, CancellationToken ct) =>
                    {
                        JsonObject? spec = await LoadContractSpecAsync(specDir, capturedContract, resolveServiceUrl, httpClientFactory, ct);
                        if (spec is null)
                        {
                            return Results.NotFound();
                        }

                        RewriteServersForActiveRequest(spec, httpContext, configuration);
                        return Results.Content(spec.ToJsonString(IndentedJsonOptions), "application/json; charset=utf-8");
                    })
                .ExcludeFromDescription()
                .AllowAnonymous();
        }

        return app;
    }

    private static void RewriteServersForActiveRequest(
        JsonObject spec,
        HttpContext httpContext,
        IConfiguration configuration)
    {
        string serverUrl = CadenceHttpHardening.ResolveOpenApiServerUrl(
            httpContext,
            configuration[CadenceHttpHardening.OpenApiPublicOriginKey]);

        spec["servers"] = new JsonArray
        {
            new JsonObject
            {
                ["url"] = serverUrl,
            }
        };
    }

    private static async Task<JsonObject?> LoadUnifiedSpecAsync(
        string? specDir,
        IReadOnlyList<string> contracts,
        Func<CadenceService, string> resolveServiceUrl,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        if (specDir is not null)
        {
            string unifiedPath = Path.Combine(specDir, "cdisc_openapi.json");
            if (File.Exists(unifiedPath))
            {
                string raw = await File.ReadAllTextAsync(unifiedPath, ct);
                if (JsonNode.Parse(raw) is JsonObject parsed)
                {
                    return parsed;
                }
            }
        }

        var upstreamSpecs = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (string contract in contracts)
        {
            JsonObject? spec = await FetchUpstreamContractSpecAsync(contract, resolveServiceUrl, httpClientFactory, ct);
            if (spec is not null)
            {
                upstreamSpecs[contract] = spec;
            }
        }

        return upstreamSpecs.Count > 0
            ? OpenApiSyncService.AggregateOpenApiSpecs(AppContext.BaseDirectory, AppContext.BaseDirectory, upstreamSpecs)
            : null;
    }

    private static async Task<JsonObject?> LoadContractSpecAsync(
        string? specDir,
        string contract,
        Func<CadenceService, string> resolveServiceUrl,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        if (specDir is not null)
        {
            string contractPath = Path.Combine(specDir, $"{contract}{SpecFileSuffix}");
            if (File.Exists(contractPath))
            {
                string raw = await File.ReadAllTextAsync(contractPath, ct);
                if (JsonNode.Parse(raw) is JsonObject parsed)
                {
                    return parsed;
                }
            }
        }

        return await FetchUpstreamContractSpecAsync(contract, resolveServiceUrl, httpClientFactory, ct);
    }

    private static async Task<JsonObject?> FetchUpstreamContractSpecAsync(
        string contract,
        Func<CadenceService, string> resolveServiceUrl,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        string serviceName;
        string upstreamDoc;
        if (KernelModuleContracts.TryGetValue(contract, out var kernelRoute))
        {
            serviceName = kernelRoute.ServiceName;
            upstreamDoc = kernelRoute.UpstreamDocument;
        }
        else
        {
            var match = ServiceTopology.Services.FirstOrDefault(s => string.Equals(s.Name, contract, StringComparison.Ordinal));
            if (match is null)
            {
                return null;
            }

            serviceName = match.Name;
            upstreamDoc = "v1";
        }

        string baseUrl = resolveServiceUrl(ServiceTopology.Get(serviceName)).TrimEnd('/');
        string targetUrl = $"{baseUrl}/openapi/{Uri.EscapeDataString(upstreamDoc)}.json";
        try
        {
            using HttpClient client = httpClientFactory.CreateClient(UpstreamOpenApiHttpClientName);
            using HttpResponseMessage response = await client.GetAsync(new Uri(targetUrl, UriKind.Absolute), ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string raw = await response.Content.ReadAsStringAsync(ct);
            return JsonNode.Parse(raw) as JsonObject;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> DiscoverContractNames(string? specDir)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (CadenceService service in ServiceTopology.Services)
        {
            names.Add(service.Name);
        }

        foreach (string moduleContract in KernelModuleContracts.Keys)
        {
            names.Add(moduleContract);
        }

        if (specDir is not null && Directory.Exists(specDir))
        {
            foreach (string file in Directory.EnumerateFiles(specDir, $"*{SpecFileSuffix}", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(file);
                string contract = fileName[..^SpecFileSuffix.Length];
                if (!string.Equals(contract, UnifiedDocumentName, StringComparison.OrdinalIgnoreCase) && contract.Length > 0)
                {
                    names.Add(contract);
                }
            }
        }

        return [.. names];
    }

    private static string? TryFindOpenApiDirectory(string contentRootPath)
    {
        foreach (string seed in new[] { contentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            if (string.IsNullOrWhiteSpace(seed))
            {
                continue;
            }

            string? current = Path.GetFullPath(seed);
            while (!string.IsNullOrEmpty(current))
            {
                string candidate = Path.Combine(current, "docs", "openapi");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                current = Directory.GetParent(current)?.FullName;
            }
        }

        return null;
    }
}
