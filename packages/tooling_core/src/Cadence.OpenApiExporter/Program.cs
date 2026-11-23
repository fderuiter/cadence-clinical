using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Cadence.Kernel.Modules;
using Cadence.Topology;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

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

        string repoRoot = RepositoryRoot.Find();
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            outputDir = Path.Combine(repoRoot, "docs", "openapi");
        }

        var targetServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            bool isServiceFlag = string.Equals(args[i], "--service", StringComparison.OrdinalIgnoreCase)
                || string.Equals(args[i], "--services", StringComparison.OrdinalIgnoreCase)
                || string.Equals(args[i], "-s", StringComparison.OrdinalIgnoreCase);

            if (isServiceFlag && i + 1 < args.Length)
            {
                var parts = args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var p in parts)
                {
                    targetServices.Add(p);
                }
            }
        }

        Directory.CreateDirectory(outputDir);
        Console.WriteLine($"[INFO] Target output directory: {outputDir}");

        // The gateway is a YARP reverse proxy with no endpoints of its own and does not map an OpenAPI document.
        var standaloneServices = ServiceTopology.Services
            .Where(s => !string.Equals(s.Name, "kernel", StringComparison.Ordinal))
            .Select(s => new DotnetServiceRegistration(
                s.Name,
                s.SchemaPrefix,
                ResolveProgramType(s),
                CombineRepoRelativePath(repoRoot, s.SourceDirectory)))
            .ToList();

        var selectedStandalone = targetServices.Count > 0
            ? standaloneServices.Where(s => targetServices.Contains(s.Name)).ToList()
            : standaloneServices;
        bool shouldInspectKernelHost = targetServices.Count == 0
            || targetServices.Any(target => standaloneServices.All(s => !s.Name.Equals(target, StringComparison.OrdinalIgnoreCase)));

        if (targetServices.Count > 0)
        {
            Console.WriteLine($"[INFO] Target contract filter requested: {string.Join(", ", targetServices.OrderBy(s => s, StringComparer.Ordinal))}");
        }

        var exportedSchemas = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);

        bool success = true;
        foreach (var svc in selectedStandalone)
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

        if (shouldInspectKernelHost)
        {
            CadenceService kernelService = ServiceTopology.Get("kernel");
            string kernelContentRoot = CombineRepoRelativePath(repoRoot, kernelService.SourceDirectory);
            bool kernelSuccess = await ExportKernelHostOpenApiAsync(
                kernelService,
                kernelContentRoot,
                outputDir,
                targetServices,
                selectedStandalone.Select(s => s.Name),
                exportedSchemas);
            if (!kernelSuccess)
            {
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
            var aggregatedSpec = AggregateOpenApiSpecs(outputDir, repoRoot, exportedSchemas);
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

    public static string ToSchemaPrefix(string contractName) =>
        ServiceTopology.ToSchemaPrefix(contractName);

    private static Type ResolveProgramType(CadenceService service) =>
        Assembly.Load(service.AssemblyName).GetType(service.ProgramTypeName, throwOnError: true)!;

    private static string CombineRepoRelativePath(string repoRoot, string relativePath) =>
        Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static Action<IWebHostBuilder> CreateExportHostConfiguration(string contentRoot) =>
        builder => builder
            .UseEnvironment("Development")
            .UseContentRoot(contentRoot)
            .UseSetting("GATEWAY_SECRET", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("OIDC_AUTHORITY", "http://localhost/realms/openapi-export")
            .UseSetting("POSTGRES_CONNECTION_STRING", "Host=localhost;Database=cadence_openapi_export")
            .UseSetting("DemographicsEncryption:KeyBase64", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("RANDOMIZATION_ALLOCATION_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("FIELD_ENCRYPTION_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("FIELD_LOOKUP_HASH_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("CADENCE_SCE_SIGNING_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("CADENCE_IMAGING_DEID_SIGNING_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("Storage:Provider", "memory");

    private static async Task<bool> ExportKernelHostOpenApiAsync(
        CadenceService kernelService,
        string contentRoot,
        string outputDir,
        HashSet<string> targetServices,
        IEnumerable<string> matchedStandaloneNames,
        Dictionary<string, JsonObject> exportedSchemas)
    {
        Console.WriteLine("\n[INFO] Booting Cadence.Kernel.Host once for kernel and module OpenAPI export...");
        try
        {
            Type kernelProgramType = ResolveProgramType(kernelService);
            var factoryType = typeof(WebApplicationFactory<>).MakeGenericType(kernelProgramType);
            using var factory = (IDisposable)Activator.CreateInstance(factoryType)!;
            var withHostBuilderMethod = factoryType.GetMethod("WithWebHostBuilder")!;
            using var configuredFactory = (IDisposable)withHostBuilderMethod.Invoke(
                factory,
                [CreateExportHostConfiguration(contentRoot)])!;
            var servicesProperty = configuredFactory.GetType().GetProperty("Services")!;
            var serviceProvider = (IServiceProvider)servicesProperty.GetValue(configuredFactory)!;
            var createClientMethod = configuredFactory.GetType().GetMethod("CreateClient", Type.EmptyTypes)!;
            using var client = (HttpClient)createClientMethod.Invoke(configuredFactory, null)!;

            var availableDocuments = new List<string> { kernelService.Name };
            availableDocuments.AddRange(
                serviceProvider
                    .GetServices<CadenceModuleRegistration>()
                    .Select(m => m.EffectiveOpenApiDocument)
                    .OrderBy(d => d, StringComparer.Ordinal));

            var documentsToExport = targetServices.Count > 0
                ? availableDocuments.Where(targetServices.Contains).ToList()
                : availableDocuments;

            if (targetServices.Count > 0)
            {
                var knownContracts = new HashSet<string>(matchedStandaloneNames, StringComparer.OrdinalIgnoreCase);
                foreach (string doc in availableDocuments)
                {
                    knownContracts.Add(doc);
                }

                var unknown = targetServices.Where(t => !knownContracts.Contains(t)).OrderBy(t => t, StringComparer.Ordinal).ToList();
                if (unknown.Count > 0)
                {
                    Console.WriteLine($"[WARNING] Unknown target contract(s) ignored: {string.Join(", ", unknown)}");
                }
            }

            bool allSucceeded = true;
            foreach (string documentName in documentsToExport)
            {
                string prefix = string.Equals(documentName, kernelService.Name, StringComparison.OrdinalIgnoreCase)
                    ? kernelService.SchemaPrefix
                    : kernelService.Modules.FirstOrDefault(m => string.Equals(m.ContractName, documentName, StringComparison.OrdinalIgnoreCase))?.SchemaPrefix
                      ?? ToSchemaPrefix(documentName);
                Console.WriteLine($"[INFO] Exporting Cadence.Kernel.Host OpenAPI document '{documentName}' (prefix '{prefix}')...");
                var docObj = await FetchAndNamespaceDocumentAsync(client, $"/openapi/{documentName}.json", prefix);
                if (docObj == null)
                {
                    await Console.Error.WriteLineAsync($"[ERROR] Failed to compile OpenAPI document '{documentName}' on Cadence.Kernel.Host.");
                    allSucceeded = false;
                    continue;
                }

                exportedSchemas[documentName] = docObj;
                string filePath = Path.Combine(outputDir, $"{documentName}_openapi.json");
                await WriteJsonToFileAsync(docObj, filePath);
                Console.WriteLine($"[SUCCESS] Exported namespaced schema for '{documentName}' -> {filePath}");
            }

            return allSucceeded;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[ERROR] Exception booting Cadence.Kernel.Host: {ex.GetBaseException().Message}");
            return false;
        }
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
        // key, and for payments, eConsent and interop the field-protection keys (ADR-2494), and for interop the SCE signing key (ADR-2515). Export only reads route metadata and never opens a connection, so throwaway secrets, an
        // unreachable authority and a credential-free connection string are enough.
        Action<IWebHostBuilder> configureHostBuilder = CreateExportHostConfiguration(svc.ContentRoot);
        using var configuredFactory = (IDisposable)(withHostBuilderMethod?.Invoke(factory, [configureHostBuilder]) ?? factory);

        var createClientMethod = configuredFactory.GetType().GetMethod("CreateClient", Type.EmptyTypes);
        using var client = (HttpClient)createClientMethod!.Invoke(configuredFactory, null)!;

        return await FetchAndNamespaceDocumentAsync(client, "/openapi/v1.json", svc.Prefix);
    }

    private static async Task<JsonObject?> FetchAndNamespaceDocumentAsync(HttpClient client, string route, string prefix)
    {
        var response = await client.GetAsync(route);
        if (!response.IsSuccessStatusCode)
        {
            await Console.Error.WriteLineAsync($"HTTP GET {route} returned status {response.StatusCode}");
            return null;
        }

        string rawJson = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(rawJson);
        if (node is not JsonObject docObj)
        {
            return null;
        }

        ApplyNamespacing(docObj, prefix);
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
                obj["$ref"] = PrefixedRef(refVal, prefix);
            }

            // A polymorphic schema's discriminator maps each type value to a schema by reference too.
            if (obj["discriminator"] is JsonObject { } discriminator && discriminator["mapping"] is JsonObject mapping)
            {
                foreach (var (key, value) in mapping.ToList())
                {
                    if (value?.GetValue<string>() is string mapped)
                    {
                        mapping[key] = PrefixedRef(mapped, prefix);
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

    private static string PrefixedRef(string refVal, string prefix)
    {
        const string targetPrefix = "#/components/schemas/";
        if (!refVal.StartsWith(targetPrefix, StringComparison.Ordinal))
        {
            return refVal;
        }

        string schemaName = refVal.Substring(targetPrefix.Length);
        return schemaName.StartsWith(prefix, StringComparison.Ordinal) ? refVal : $"{targetPrefix}{prefix}{schemaName}";
    }

    private static JsonObject AggregateOpenApiSpecs(
        string outputDir,
        string repoRoot,
        Dictionary<string, JsonObject> newlyExported)
    {
        var aggregatedPaths = new JsonObject();
        var aggregatedSchemas = new JsonObject();

        string fallbackDir = Path.Combine(repoRoot, "docs", "openapi");

        // Collect all file names ending in _openapi.json from outputDir and fallbackDir
        var specFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(outputDir))
        {
            foreach (var f in Directory.GetFiles(outputDir, "*_openapi.json"))
            {
                specFiles.Add(Path.GetFileName(f));
            }
        }
        if (Directory.Exists(fallbackDir))
        {
            foreach (var f in Directory.GetFiles(fallbackDir, "*_openapi.json"))
            {
                specFiles.Add(Path.GetFileName(f));
            }
        }

        var orderedFiles = specFiles
            .Where(f => !f.Equals("cdisc_openapi.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        foreach (var fileName in orderedFiles)
        {
            string serviceName = fileName.Replace("_openapi.json", "", StringComparison.OrdinalIgnoreCase);
            JsonObject? doc = null;

            if (newlyExported.TryGetValue(serviceName, out var exportedDoc))
            {
                doc = exportedDoc;
            }
            else
            {
                string outputFilePath = Path.Combine(outputDir, fileName);
                string fallbackFilePath = Path.Combine(fallbackDir, fileName);
                string? targetReadPath = null;
                if (File.Exists(outputFilePath))
                {
                    targetReadPath = outputFilePath;
                }
                else if (File.Exists(fallbackFilePath))
                {
                    targetReadPath = fallbackFilePath;
                }

                if (targetReadPath != null)
                {
                    try
                    {
                        string text = File.ReadAllText(targetReadPath);
                        doc = JsonNode.Parse(text) as JsonObject;
                    }
                    catch
                    {
                        // Ignore unparseable fallback files
                    }
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

    private sealed record DotnetServiceRegistration(string Name, string Prefix, Type ProgramType, string ContentRoot);
}
