extern alias ctms;
extern alias designer;
extern alias econsent;
extern alias eisf;
extern alias etmf;
extern alias execution;
extern alias fileshare;
extern alias imaging;
extern alias interop;
extern alias kernel;
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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Modules;
using Cadence.ToolingCore.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.OpenApiExporter;

public static class Program
{
    public const int DefaultMaxParallelism = 4;

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("========================================================================");
        Console.WriteLine(" Cadence.OpenApiExporter - Standalone .NET OpenAPI Specification CLI");
        Console.WriteLine("========================================================================");

        if (!TryParseMaxParallelism(args, out int maxParallelism, out string? parallelismError))
        {
            await Console.Error.WriteLineAsync($"[ERROR] {parallelismError}");
            return 1;
        }

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
        Console.WriteLine($"[INFO] Max host parallelism: {maxParallelism}");

        var standaloneServices = new List<DotnetServiceRegistration>
        {
            new("ctms", ToSchemaPrefix("ctms"), typeof(ctms::Cadence.Apps.Ctms.Program), Path.Combine(repoRoot, "apps", "ctms", "src")),
            new("econsent", ToSchemaPrefix("econsent"), typeof(econsent::Cadence.Apps.Econsent.Program), Path.Combine(repoRoot, "apps", "econsent", "src")),
            new("eisf", ToSchemaPrefix("eisf"), typeof(eisf::Cadence.Apps.Eisf.Program), Path.Combine(repoRoot, "apps", "eisf", "src")),
            new("etmf", ToSchemaPrefix("etmf"), typeof(etmf::Cadence.Apps.Etmf.Program), Path.Combine(repoRoot, "apps", "etmf", "src")),
            new("fileshare", ToSchemaPrefix("fileshare"), typeof(fileshare::Cadence.Apps.Fileshare.Program), Path.Combine(repoRoot, "apps", "fileshare", "src")),
            new("imaging", ToSchemaPrefix("imaging"), typeof(imaging::Cadence.Apps.Imaging.Program), Path.Combine(repoRoot, "apps", "imaging", "src")),
            new("interop", ToSchemaPrefix("interop"), typeof(interop::Cadence.Apps.Interop.Program), Path.Combine(repoRoot, "apps", "interop", "src")),
            new("knowledge", ToSchemaPrefix("knowledge"), typeof(knowledge::Cadence.Apps.Knowledge.Program), Path.Combine(repoRoot, "apps", "knowledge", "src")),
            new("notifications", ToSchemaPrefix("notifications"), typeof(notifications::Cadence.Apps.Notifications.Program), Path.Combine(repoRoot, "apps", "notifications", "src")),
            new("org", ToSchemaPrefix("org"), typeof(org::Cadence.Apps.Org.Program), Path.Combine(repoRoot, "apps", "org", "src")),
            new("payments", ToSchemaPrefix("payments"), typeof(payments::Cadence.Apps.Payments.Program), Path.Combine(repoRoot, "apps", "payments", "src")),
            new("quality", ToSchemaPrefix("quality"), typeof(quality::Cadence.Apps.Quality.Program), Path.Combine(repoRoot, "apps", "quality", "src")),
            new("safety", ToSchemaPrefix("safety"), typeof(safety::Cadence.Apps.Safety.Program), Path.Combine(repoRoot, "apps", "safety", "src")),
            new("tickets", ToSchemaPrefix("tickets"), typeof(tickets::Cadence.Apps.Tickets.Program), Path.Combine(repoRoot, "apps", "tickets", "src")),
            new("execution", ToSchemaPrefix("execution"), typeof(execution::Cadence.Apps.Execution.Program), Path.Combine(repoRoot, "apps", "execution", "src")),
            new("designer", ToSchemaPrefix("designer"), typeof(designer::Cadence.Apps.Designer.Program), Path.Combine(repoRoot, "apps", "designer", "src")),
            // The gateway is a YARP reverse proxy with no endpoints of its own and does not map an OpenAPI document.
        };

        var selectedStandalone = targetServices.Count > 0
            ? [.. standaloneServices.Where(s => targetServices.Contains(s.Name))]
            : standaloneServices;
        bool shouldInspectKernelHost = targetServices.Count == 0
            || targetServices.Any(target => standaloneServices.All(s => !s.Name.Equals(target, StringComparison.OrdinalIgnoreCase)));

        if (targetServices.Count > 0)
        {
            Console.WriteLine($"[INFO] Target contract filter requested: {string.Join(", ", targetServices.OrderBy(s => s, StringComparer.Ordinal))}");
        }

        var matchedStandaloneNames = selectedStandalone.Select(s => s.Name).ToArray();
        var hostJobs = new List<Func<Task<HostExportResult>>>(selectedStandalone.Count + (shouldInspectKernelHost ? 1 : 0));

        foreach (var svc in selectedStandalone)
        {
            var capturedService = svc;
            hostJobs.Add(() => ExportStandaloneHostJobAsync(capturedService, outputDir));
        }

        if (shouldInspectKernelHost)
        {
            string kernelContentRoot = Path.Combine(repoRoot, "apps", "kernel-api", "src");
            hostJobs.Add(() => ExportKernelHostJobAsync(
                kernelContentRoot,
                outputDir,
                targetServices,
                matchedStandaloneNames));
        }

        using var throttle = new SemaphoreSlim(maxParallelism, maxParallelism);
        var runningTasks = hostJobs
            .Select(async job =>
            {
                await throttle.WaitAsync();
                try
                {
                    return await job();
                }
                finally
                {
                    throttle.Release();
                }
            })
            .ToArray();

        HostExportResult[] results = await Task.WhenAll(runningTasks);

        var exportedSchemas = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        bool success = true;

        foreach (HostExportResult result in results)
        {
            foreach (string line in result.StdoutLines)
            {
                Console.WriteLine(line);
            }

            foreach (string errorLine in result.StderrLines)
            {
                await Console.Error.WriteLineAsync(errorLine);
            }

            foreach (var (name, spec) in result.ExportedSpecs)
            {
                exportedSchemas[name] = spec;
            }

            if (!result.Succeeded)
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

    public static bool TryParseMaxParallelism(string[] args, out int maxParallelism, out string? error) =>
        OpenApiSyncService.TryParseMaxParallelism(args, out maxParallelism, out error);

    public static string ToSchemaPrefix(string contractName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        var sb = new StringBuilder(contractName.Length + 1);
        foreach (string segment in contractName.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append(char.ToUpperInvariant(segment[0]));
            if (segment.Length > 1)
            {
                sb.Append(segment.AsSpan(1));
            }
        }

        sb.Append('_');
        return sb.ToString();
    }

    private static Action<IWebHostBuilder> CreateExportHostConfiguration(string contentRoot) =>
        builder => builder
            .UseEnvironment("Development")
            .UseContentRoot(contentRoot)
            .UseSetting("GATEWAY_SECRET", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("OIDC_AUTHORITY", "http://localhost/realms/openapi-export")
            .UseSetting("POSTGRES_CONNECTION_STRING", "Host=localhost;Database=cadence_openapi_export")
            .UseSetting("DemographicsEncryption:KeyBase64", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("RANDOMIZATION_ALLOCATION_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("CGT_BARCODE_HMAC_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("FIELD_ENCRYPTION_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("FIELD_LOOKUP_HASH_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("CADENCE_BIOMETRIC_LIVENESS_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("CADENCE_SCE_SIGNING_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("CADENCE_IMAGING_DEID_SIGNING_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("CADENCE_IMAGING_DEID_PSEUDONYMIZATION_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("Storage:Provider", "memory");

    private static async Task<HostExportResult> ExportStandaloneHostJobAsync(
        DotnetServiceRegistration svc,
        string outputDir)
    {
        var result = new HostExportResult();
        result.StdoutLines.Add($"\n[INFO] Compiling OpenAPI specification for .NET service: '{svc.Name}'...");
        try
        {
            var specObj = await ExportServiceOpenApiAsync(svc, result.StderrLines);
            if (specObj != null)
            {
                result.ExportedSpecs.Add((svc.Name, specObj));
                string filePath = Path.Combine(outputDir, $"{svc.Name}_openapi.json");
                await WriteJsonToFileAsync(specObj, filePath);
                result.StdoutLines.Add($"[SUCCESS] Exported namespaced schema for '{svc.Name}' -> {filePath}");
                result.Succeeded = true;
            }
            else
            {
                result.StderrLines.Add($"[ERROR] Failed to compile OpenAPI spec for '{svc.Name}'.");
                result.Succeeded = false;
            }
        }
        catch (Exception ex)
        {
            result.StderrLines.Add($"[ERROR] Exception compiling '{svc.Name}': {ex.GetBaseException().Message}");
            result.Succeeded = false;
        }

        return result;
    }

    private static async Task<HostExportResult> ExportKernelHostJobAsync(
        string contentRoot,
        string outputDir,
        HashSet<string> targetServices,
        IReadOnlyCollection<string> matchedStandaloneNames)
    {
        var result = new HostExportResult();
        result.StdoutLines.Add("\n[INFO] Booting Cadence.Kernel.Host once for kernel and module OpenAPI export...");
        try
        {
            using var factory = new WebApplicationFactory<kernel::Cadence.Kernel.Host.Program>();
            using var configuredFactory = factory.WithWebHostBuilder(CreateExportHostConfiguration(contentRoot));
            using var client = configuredFactory.CreateClient();

            var availableDocuments = new List<string> { "kernel" };
            availableDocuments.AddRange(
                configuredFactory.Services
                    .GetServices<CadenceModuleRegistration>()
                    .Select(m => m.EffectiveOpenApiDocument)
                    .OrderBy(d => d, StringComparer.Ordinal));

            var documentsToExport = targetServices.Count > 0
                ? [.. availableDocuments.Where(targetServices.Contains)]
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
                    result.StdoutLines.Add($"[WARNING] Unknown target contract(s) ignored: {string.Join(", ", unknown)}");
                }
            }

            bool allSucceeded = true;
            foreach (string documentName in documentsToExport)
            {
                string prefix = ToSchemaPrefix(documentName);
                result.StdoutLines.Add($"[INFO] Exporting Cadence.Kernel.Host OpenAPI document '{documentName}' (prefix '{prefix}')...");
                var docObj = await FetchAndNamespaceDocumentAsync(client, $"/openapi/{documentName}.json", prefix, result.StderrLines);
                if (docObj == null)
                {
                    result.StderrLines.Add($"[ERROR] Failed to compile OpenAPI document '{documentName}' on Cadence.Kernel.Host.");
                    allSucceeded = false;
                    continue;
                }

                result.ExportedSpecs.Add((documentName, docObj));
                string filePath = Path.Combine(outputDir, $"{documentName}_openapi.json");
                await WriteJsonToFileAsync(docObj, filePath);
                result.StdoutLines.Add($"[SUCCESS] Exported namespaced schema for '{documentName}' -> {filePath}");
            }

            result.Succeeded = allSucceeded;
            return result;
        }
        catch (Exception ex)
        {
            result.StderrLines.Add($"[ERROR] Exception booting Cadence.Kernel.Host: {ex.GetBaseException().Message}");
            result.Succeeded = false;
            return result;
        }
    }

    private static async Task<JsonObject?> ExportServiceOpenApiAsync(DotnetServiceRegistration svc, List<string> stderrLines)
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
        var configuredFactory = withHostBuilderMethod?.Invoke(factory, [configureHostBuilder]) ?? factory;

        var createClientMethod = configuredFactory.GetType().GetMethod("CreateClient", Type.EmptyTypes);
        using var client = (HttpClient)createClientMethod!.Invoke(configuredFactory, null)!;

        return await FetchAndNamespaceDocumentAsync(client, "/openapi/v1.json", svc.Prefix, stderrLines);
    }

    private static async Task<JsonObject?> FetchAndNamespaceDocumentAsync(
        HttpClient client,
        string route,
        string prefix,
        List<string> stderrLines)
    {
        var response = await client.GetAsync(route);
        if (!response.IsSuccessStatusCode)
        {
            stderrLines.Add($"HTTP GET {route} returned status {response.StatusCode}");
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
            foreach (var (NewKey, Node) in renamedMap.Values)
            {
                schemas[NewKey] = Node;
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

        string schemaName = refVal[targetPrefix.Length..];
        return schemaName.StartsWith(prefix, StringComparison.Ordinal) ? refVal : $"{targetPrefix}{prefix}{schemaName}";
    }

    public static JsonObject AggregateOpenApiSpecs(
        string outputDir,
        string repoRoot,
        IReadOnlyDictionary<string, JsonObject> newlyExported) =>
        OpenApiSyncService.AggregateOpenApiSpecs(outputDir, repoRoot, newlyExported);

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
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln"))) return current;
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }

        current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln"))) return current;
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }

        return Directory.GetCurrentDirectory();
    }

    private sealed record DotnetServiceRegistration(string Name, string Prefix, Type ProgramType, string ContentRoot);

    private sealed class HostExportResult
    {
        public List<string> StdoutLines { get; } = [];

        public List<string> StderrLines { get; } = [];

        public List<(string Name, JsonObject Spec)> ExportedSpecs { get; } = [];

        public bool Succeeded { get; set; }
    }
}
