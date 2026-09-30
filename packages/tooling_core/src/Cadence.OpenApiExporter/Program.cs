extern alias aigateway;
extern alias ctms;
extern alias designer;
extern alias econsent;
extern alias eisf;
extern alias etmf;
extern alias execution;
extern alias fileshare;
extern alias imaging;
extern alias interop;
extern alias knowledge;
extern alias notifications;
extern alias org;
extern alias payments;
extern alias quality;
extern alias safety;
extern alias tickets;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cadence.OpenApiExporter;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("========================================================================");
        Console.WriteLine(" Cadence.OpenApiExporter - Standalone .NET OpenAPI Specification CLI");
        Console.WriteLine("========================================================================");

        string? outputDir = null;
        int outputFlagIndex = Array.FindIndex(args, a => a == "--output-dir" || a == "-o");
        if (outputFlagIndex >= 0 && outputFlagIndex + 1 < args.Length)
        {
            outputDir = args[outputFlagIndex + 1];
        }

        string repoRoot = FindRepoRoot();
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            outputDir = Path.Combine(repoRoot, "docs", "openapi");
        }

        Directory.CreateDirectory(outputDir);
        Console.WriteLine($"[INFO] Target output directory: {outputDir}");

        var services = new List<DotnetServiceRegistration>
        {
            new("ai_gateway", "AiGateway_", typeof(aigateway::Cadence.Apps.AiGateway.Program), Path.Combine(repoRoot, "apps", "ai_gateway", "src")),
            new("ctms", "Ctms_", typeof(ctms::Cadence.Apps.Ctms.Program), Path.Combine(repoRoot, "apps", "ctms", "src")),
            new("econsent", "Econsent_", typeof(econsent::Cadence.Apps.Econsent.Program), Path.Combine(repoRoot, "apps", "econsent", "src")),
            new("eisf", "Eisf_", typeof(eisf::Cadence.Apps.Eisf.Program), Path.Combine(repoRoot, "apps", "eisf", "src")),
            new("etmf", "Etmf_", typeof(etmf::Cadence.Apps.Etmf.Program), Path.Combine(repoRoot, "apps", "etmf", "src")),
            new("fileshare", "Fileshare_", typeof(fileshare::Cadence.Apps.Fileshare.Program), Path.Combine(repoRoot, "apps", "fileshare", "src")),
            new("imaging", "Imaging_", typeof(imaging::Cadence.Apps.Imaging.Program), Path.Combine(repoRoot, "apps", "imaging", "src")),
            new("interop", "Interop_", typeof(interop::Cadence.Apps.Interop.Program), Path.Combine(repoRoot, "apps", "interop", "src")),
            new("knowledge", "Knowledge_", typeof(knowledge::Cadence.Apps.Knowledge.Program), Path.Combine(repoRoot, "apps", "knowledge", "src")),
            new("notifications", "Notifications_", typeof(notifications::Cadence.Apps.Notifications.Program), Path.Combine(repoRoot, "apps", "notifications", "src")),
            new("org", "Org_", typeof(org::Cadence.Apps.Org.Program), Path.Combine(repoRoot, "apps", "org", "src")),
            new("payments", "Payments_", typeof(payments::Cadence.Apps.Payments.Program), Path.Combine(repoRoot, "apps", "payments", "src")),
            new("quality", "Quality_", typeof(quality::Cadence.Apps.Quality.Program), Path.Combine(repoRoot, "apps", "quality", "src")),
            new("safety", "Safety_", typeof(safety::Cadence.Apps.Safety.Program), Path.Combine(repoRoot, "apps", "safety", "src")),
            new("tickets", "Tickets_", typeof(tickets::Cadence.Apps.Tickets.Program), Path.Combine(repoRoot, "apps", "tickets", "src")),
            new("execution", "Execution_", typeof(execution::Cadence.Apps.Execution.Program), Path.Combine(repoRoot, "apps", "execution", "src")),
            new("designer", "Designer_", typeof(designer::Cadence.Apps.Designer.Program), Path.Combine(repoRoot, "apps", "designer", "src")),
            // The gateway is a YARP reverse proxy with no endpoints of its own and does not map an OpenAPI document.
        };

        var exportedSchemas = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);

        bool success = true;
        foreach (var svc in services)
        {
            Console.WriteLine($"\n[INFO] Compiling OpenAPI specification for .NET service: '{svc.Name}'...");
            try
            {
                var specObj = await ExportServiceOpenApiAsync(svc);
                if (specObj != null)
                {
                    exportedSchemas[svc.Name] = specObj;
                    string filePath = Path.Combine(outputDir, $"{svc.Name}_openapi.json");
                    await WriteJsonToFileAsync(specObj, filePath);
                    Console.WriteLine($"[SUCCESS] Exported namespaced schema for '{svc.Name}' -> {filePath}");
                }
                else
                {
                    await Console.Error.WriteLineAsync($"[ERROR] Failed to compile OpenAPI spec for '{svc.Name}'.");
                    success = false;
                }
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"[ERROR] Exception compiling '{svc.Name}': {ex.GetBaseException().Message}");
                success = false;
            }
        }

        if (!success)
        {
            await Console.Error.WriteLineAsync("\n[FAILURE] One or more .NET microservice OpenAPI exports failed.");
            return 1;
        }

        // Aggregate unified gateway specification (cdisc_openapi.json)
        Console.WriteLine("\n[INFO] Building aggregated gateway specification 'cdisc_openapi.json'...");
        try
        {
            var aggregatedSpec = AggregateOpenApiSpecs(outputDir, exportedSchemas);
            string aggregatedFilePath = Path.Combine(outputDir, "cdisc_openapi.json");
            await WriteJsonToFileAsync(aggregatedSpec, aggregatedFilePath);
            Console.WriteLine($"[SUCCESS] Exported aggregated gateway specification -> {aggregatedFilePath}");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[ERROR] Failed to generate aggregated cdisc_openapi.json: {ex.Message}");
            return 1;
        }

        Console.WriteLine("\n========================================================================");
        Console.WriteLine(" [SUCCESS] .NET OpenAPI Exporter completed successfully!");
        Console.WriteLine("========================================================================");
        return 0;
    }

    private static async Task<JsonObject?> ExportServiceOpenApiAsync(DotnetServiceRegistration svc)
    {
        var factoryType = typeof(WebApplicationFactory<>).MakeGenericType(svc.ProgramType);
        using var factory = (IDisposable)Activator.CreateInstance(factoryType)!;

        // Configure WebHost environment to "Development" so app.MapOpenApi() routes are enabled
        var withHostBuilderMethod = factoryType.GetMethod("WithWebHostBuilder");
        // WebApplicationFactory guesses the content root from the assembly name, which does not match
        // the apps/<service>/src layout, so set it explicitly.
        // The services refuse to start without auth and database settings and, for execution, a demographics
        // key. Export only reads route metadata and never opens a connection, so throwaway secrets, an
        // unreachable authority and a credential-free connection string are enough.
        Action<IWebHostBuilder> configureHostBuilder = builder => builder
            .UseEnvironment("Development")
            .UseContentRoot(svc.ContentRoot)
            .UseSetting("GATEWAY_SECRET", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("OIDC_AUTHORITY", "http://localhost/realms/openapi-export")
            .UseSetting("POSTGRES_CONNECTION_STRING", "Host=localhost;Database=cadence_openapi_export")
            .UseSetting("DemographicsEncryption:KeyBase64", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var configuredFactory = withHostBuilderMethod?.Invoke(factory, new object[] { configureHostBuilder }) ?? factory;

        var createClientMethod = configuredFactory.GetType().GetMethod("CreateClient", Type.EmptyTypes);
        using var client = (HttpClient)createClientMethod!.Invoke(configuredFactory, null)!;

        var response = await client.GetAsync("/openapi/v1.json");
        if (!response.IsSuccessStatusCode)
        {
            await Console.Error.WriteLineAsync($"HTTP GET /openapi/v1.json returned status {response.StatusCode}");
            return null;
        }

        string rawJson = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(rawJson);
        if (node is not JsonObject docObj)
        {
            return null;
        }

        ApplyNamespacing(docObj, svc.Prefix);
        return docObj;
    }

    public static void ApplyNamespacing(JsonObject docObj, string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return;

        // 1. Rename all schema definitions in components.schemas
        if (docObj["components"] is JsonObject components &&
            components["schemas"] is JsonObject schemas)
        {
            var schemaKeys = schemas.Select(kv => kv.Key).ToList();
            var renamedMap = new Dictionary<string, (string NewKey, JsonNode? Node)>();

            foreach (var key in schemaKeys)
            {
                string newKey = key.StartsWith(prefix, StringComparison.Ordinal) ? key : $"{prefix}{key}";
                var val = schemas[key];
                renamedMap[key] = (newKey, val?.DeepClone());
            }

            schemas.Clear();
            foreach (var item in renamedMap.Values)
            {
                schemas[item.NewKey] = item.Node;
            }
        }

        // 2. Recursively update all $ref strings pointing to #/components/schemas/
        RewriteRefPointers(docObj, prefix);
    }

    private static void RewriteRefPointers(JsonNode? node, string prefix)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("$ref") && obj["$ref"]?.GetValue<string>() is string refVal)
            {
                const string targetPrefix = "#/components/schemas/";
                if (refVal.StartsWith(targetPrefix, StringComparison.Ordinal))
                {
                    string schemaName = refVal.Substring(targetPrefix.Length);
                    if (!schemaName.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        obj["$ref"] = $"{targetPrefix}{prefix}{schemaName}";
                    }
                }
            }

            foreach (var kv in obj.ToList())
            {
                RewriteRefPointers(kv.Value, prefix);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                RewriteRefPointers(item, prefix);
            }
        }
    }

    private static JsonObject AggregateOpenApiSpecs(
        string outputDir,
        Dictionary<string, JsonObject> newlyExported)
    {
        var aggregatedPaths = new JsonObject();
        var aggregatedSchemas = new JsonObject();

        // Include all JSON spec files in outputDir
        var specFiles = Directory.GetFiles(outputDir, "*_openapi.json")
            .Where(f => !Path.GetFileName(f).Equals("cdisc_openapi.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        foreach (var file in specFiles)
        {
            string serviceName = Path.GetFileName(file).Replace("_openapi.json", "", StringComparison.OrdinalIgnoreCase);
            JsonObject? doc = null;

            if (newlyExported.TryGetValue(serviceName, out var exportedDoc))
            {
                doc = exportedDoc;
            }
            else
            {
                try
                {
                    string text = File.ReadAllText(file);
                    doc = JsonNode.Parse(text) as JsonObject;
                }
                catch
                {
                    // Ignore unparseable fallback files
                }
            }

            if (doc == null) continue;

            // Merge paths
            if (doc["paths"] is JsonObject paths)
            {
                foreach (var pathKv in paths)
                {
                    if (!aggregatedPaths.ContainsKey(pathKv.Key))
                    {
                        aggregatedPaths[pathKv.Key] = pathKv.Value?.DeepClone();
                    }
                }
            }

            // Merge schemas
            if (doc["components"] is JsonObject components && components["schemas"] is JsonObject schemas)
            {
                foreach (var schemaKv in schemas)
                {
                    if (!aggregatedSchemas.ContainsKey(schemaKv.Key))
                    {
                        aggregatedSchemas[schemaKv.Key] = schemaKv.Value?.DeepClone();
                    }
                }
            }
        }

        var aggregatedSpec = new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = new JsonObject
            {
                ["title"] = "Cadence Clinical Unified Gateway API",
                ["description"] = "Unified microservices API contract for Cadence Clinical Platform.\nEnforces OIDC/Keycloak authentication, RFC 7807 problem details, and ISO 14155:2020 regulatory compliance.",
                ["version"] = "1.0.0-PROD"
            },
            ["servers"] = new JsonArray
            {
                new JsonObject
                {
                    ["url"] = "https://api.ccrsoft.com/api/v1",
                    ["description"] = "Production API Gateway"
                },
                new JsonObject
                {
                    ["url"] = "http://localhost:8000/api/v1",
                    ["description"] = "Local Dev Gateway Proxy"
                }
            },
            ["paths"] = aggregatedPaths,
            ["components"] = new JsonObject
            {
                ["schemas"] = aggregatedSchemas
            }
        };

        return aggregatedSpec;
    }

    private static async Task WriteJsonToFileAsync(JsonObject obj, string filePath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        string json = obj.ToJsonString(options) + "\n";
        await File.WriteAllTextAsync(filePath, json);
    }

    private static string FindRepoRoot()
    {
        string current = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(current, "Cadence.sln")))
        {
            var parent = Directory.GetParent(current);
            if (parent == null) return Directory.GetCurrentDirectory();
            current = parent.FullName;
        }

        return current;
    }

    private sealed record DotnetServiceRegistration(string Name, string Prefix, Type ProgramType, string ContentRoot);
}
