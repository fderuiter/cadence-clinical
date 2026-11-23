using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Topology;

namespace Cadence.ToolingCore.Services;

public static partial class DevEnvironmentValidatorService
{
    public sealed record ServicePortMapping(
        string RawServiceName,
        string CanonicalName,
        int Port,
        string RelativeFilePath,
        int LineNumber,
        string Schema = ""
    );

    private static readonly string[] RequiredExecutionOptionCKeys =
    [
        "ConnectionStrings__cadence",
        "GATEWAY_SECRET",
        "AUDIT_LOG_SECRET_KEY",
        "DemographicsEncryption__KeyBase64",
        "RANDOMIZATION_ALLOCATION_KEY",
    ];

    [GeneratedRegex(@"GetServiceUrl\s*\(\s*""([^""]+)""\s*,\s*(\d+)\s*\)")]
    private static partial Regex GatewayServiceUrlRegex();

    [GeneratedRegex(@"Service\s*\(\s*""([^""]+)""\s*,\s*[^,]+\s*,\s*(\d+)")]
    private static partial Regex AppHostServiceRegex();

    [GeneratedRegex(@"\(\s*""([A-Z0-9_]+_SERVICE_URL)""\s*,\s*(\d+)\s*\)")]
    private static partial Regex AppHostTupleRegex();

    [GeneratedRegex(@"\.WithHttpEndpoint\s*\(\s*port:\s*(\d+)")]
    private static partial Regex AppHostHttpEndpointRegex();

    [GeneratedRegex(@"\b(?:5173|7687|8017|8019)\b")]
    private static partial Regex RetiredPortRegex();

    public static int ValidateDevEnvironment(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating local dev environment documentation against service configurations...");

        string docPath = Path.Combine(repoRoot, "docs", "LOCAL_DEV_ENVIRONMENT.md");
        string gatewayPath = Path.Combine(repoRoot, "apps", "gateway", "src", "Program.cs");
        string apphostPath = Path.Combine(repoRoot, "apps", "apphost", "src", "Program.cs");

        if (!File.Exists(docPath))
        {
            Console.WriteLine($"[ERROR] Dev environment documentation file not found: {docPath}");
            return 1;
        }

        if (!File.Exists(gatewayPath))
        {
            Console.WriteLine($"[ERROR] Gateway Program.cs file not found: {gatewayPath}");
            return 1;
        }

        if (!File.Exists(apphostPath))
        {
            Console.WriteLine($"[ERROR] AppHost Program.cs file not found: {apphostPath}");
            return 1;
        }

        var docMappings = ParseDocMappings(docPath, repoRoot);
        var gatewayMappings = ParseGatewayMappings(gatewayPath, repoRoot);
        var appHostMappings = ParseAppHostMappings(apphostPath, repoRoot);

        bool hasError = false;
        string relativeDocPath = Path.GetRelativePath(repoRoot, docPath);

        // Check 1: Mismatches/missing between docs and gateway
        foreach (var (canonical, gatewayInfo) in gatewayMappings)
        {
            if (!docMappings.TryGetValue(canonical, out var docInfo))
            {
                Console.WriteLine($"[ERROR] Service '{gatewayInfo.RawServiceName}' defined in {gatewayInfo.RelativeFilePath}:{gatewayInfo.LineNumber} (port {gatewayInfo.Port}) is missing from {relativeDocPath}.");
                hasError = true;
            }
            else if (docInfo.Port != gatewayInfo.Port)
            {
                Console.WriteLine($"[ERROR] Dev environment documentation drift detected between {docInfo.RelativeFilePath} and {gatewayInfo.RelativeFilePath}:");
                Console.WriteLine($"  - Service '{canonical}':");
                Console.WriteLine($"      {docInfo.RelativeFilePath}:{docInfo.LineNumber} specifies port {docInfo.Port} (expected)");
                Console.WriteLine($"      {gatewayInfo.RelativeFilePath}:{gatewayInfo.LineNumber} specifies port {gatewayInfo.Port} (actual)");
                hasError = true;
            }
        }

        // Check 2: Mismatches/missing between docs and apphost
        foreach (var (canonical, appHostInfo) in appHostMappings)
        {
            if (!docMappings.TryGetValue(canonical, out var docInfo))
            {
                Console.WriteLine($"[ERROR] Service '{appHostInfo.RawServiceName}' defined in {appHostInfo.RelativeFilePath}:{appHostInfo.LineNumber} (port {appHostInfo.Port}) is missing from {relativeDocPath}.");
                hasError = true;
            }
            else if (docInfo.Port != appHostInfo.Port)
            {
                Console.WriteLine($"[ERROR] Dev environment documentation drift detected between {docInfo.RelativeFilePath} and {appHostInfo.RelativeFilePath}:");
                Console.WriteLine($"  - Service '{canonical}':");
                Console.WriteLine($"      {docInfo.RelativeFilePath}:{docInfo.LineNumber} specifies port {docInfo.Port} (expected)");
                Console.WriteLine($"      {appHostInfo.RelativeFilePath}:{appHostInfo.LineNumber} specifies port {appHostInfo.Port} (actual)");
                hasError = true;
            }
        }

        // Check 3: Full ServiceTopology, Vite port, schema, profile, and standalone startup validation
        if (File.Exists(Path.Combine(repoRoot, "packages", "topology", "src", "ServiceTopology.cs")) ||
            File.Exists(Path.Combine(repoRoot, "apps", "web", "vite.config.js")))
        {
            hasError |= !ValidateAgainstTopology(repoRoot, docPath, relativeDocPath, docMappings);
        }

        if (hasError)
        {
            Console.WriteLine("❌ Local development environment documentation validation failed.");
            return 1;
        }

        Console.WriteLine("✔ Local development environment documentation and service configurations are synchronized.");
        return 0;
    }

    public static string NormalizeServiceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        string lower = name.Trim().ToLowerInvariant();

        if (lower.EndsWith("_service_url", StringComparison.Ordinal))
        {
            lower = lower[..^"_service_url".Length];
        }

        if (lower.Contains("gateway", StringComparison.Ordinal)) return "gateway";
        if (lower.Contains("kernel", StringComparison.Ordinal)) return "kernel";
        if (lower.Contains("keycloak", StringComparison.Ordinal)) return "keycloak";

        int paren = lower.IndexOf('(');
        if (paren >= 0) lower = lower[..paren].Trim();

        return lower.Replace('-', '_').Trim();
    }

    private static bool ValidateAgainstTopology(
        string repoRoot,
        string docPath,
        string relativeDocPath,
        Dictionary<string, ServicePortMapping> docMappings)
    {
        bool ok = true;
        string docContent = File.ReadAllText(docPath);
        var topology = CodeMetadataCatalog.GetTopology(repoRoot);
        var dbModules = CodeMetadataCatalog.GetDatabaseModules(repoRoot);

        var retiredMatch = RetiredPortRegex().Match(docContent);
        if (retiredMatch.Success)
        {
            Console.WriteLine($"[ERROR] {relativeDocPath} references retired port '{retiredMatch.Value}'.");
            ok = false;
        }

        CheckExpectedPortAndSchema(docMappings, "gateway", topology.GatewayPort, "none", relativeDocPath, ref ok);
        CheckExpectedPortAndSchema(docMappings, "postgresql", topology.PostgresPort, "`cadence` (database)", relativeDocPath, ref ok);
        CheckExpectedPortAndSchema(docMappings, "keycloak", topology.KeycloakPort, "(own store)", relativeDocPath, ref ok);
        CheckExpectedPortAndSchema(docMappings, "web", topology.WebPort, "none", relativeDocPath, ref ok);
        CheckExpectedPortAndSchema(docMappings, "subject_portal", topology.SubjectPortalPort, "none", relativeDocPath, ref ok);

        string expectedKernelSchemas = ResolveExpectedKernelSchemas(dbModules);
        foreach (var svc in topology.Services)
        {
            string canonical = NormalizeServiceName(svc.Name);
            string expectedSchema = ResolveExpectedServiceSchema(svc, dbModules, expectedKernelSchemas);
            CheckExpectedPortAndSchema(docMappings, canonical, svc.Port, expectedSchema, relativeDocPath, ref ok);
        }

        foreach (string coreHost in topology.CoreBackendHosts)
        {
            if (!docContent.Contains($"`{coreHost}`", StringComparison.Ordinal))
            {
                Console.WriteLine($"[ERROR] {relativeDocPath} does not document core AppHost backend host '{coreHost}'.");
                ok = false;
            }
        }

        foreach (string requiredKey in RequiredExecutionOptionCKeys)
        {
            if (!docContent.Contains(requiredKey, StringComparison.Ordinal))
            {
                Console.WriteLine($"[ERROR] {relativeDocPath} Option C standalone startup instructions omit required key '{requiredKey}'.");
                ok = false;
            }
        }

        return ok;
    }

    private static void CheckExpectedPortAndSchema(
        Dictionary<string, ServicePortMapping> docMappings,
        string canonical,
        int expectedPort,
        string expectedSchema,
        string relativeDocPath,
        ref bool ok)
    {
        if (!docMappings.TryGetValue(canonical, out var docInfo))
        {
            Console.WriteLine($"[ERROR] Expected entry '{canonical}' (port {expectedPort}) is missing from {relativeDocPath}.");
            ok = false;
            return;
        }

        if (docInfo.Port != expectedPort)
        {
            Console.WriteLine($"[ERROR] {relativeDocPath}:{docInfo.LineNumber} specifies port {docInfo.Port} for '{canonical}', expected {expectedPort}.");
            ok = false;
        }

        if (expectedSchema.Length > 0 && !string.Equals(docInfo.Schema, expectedSchema, StringComparison.Ordinal))
        {
            Console.WriteLine($"[ERROR] {relativeDocPath}:{docInfo.LineNumber} specifies schema '{docInfo.Schema}' for '{canonical}', expected '{expectedSchema}'.");
            ok = false;
        }
    }

    private static string ResolveExpectedKernelSchemas(IReadOnlyList<CodeMetadataCatalog.DatabaseModuleSchema> dbModules)
    {
        string[] schemas = dbModules
            .Where(m => string.Equals(m.Host, "kernel", StringComparison.Ordinal))
            .Select(m => m.Schema)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s switch
            {
                "kernel" => 0,
                "intelligence" => 1,
                "execution" => 2,
                _ => 3,
            })
            .ThenBy(s => s, StringComparer.Ordinal)
            .Select(s => $"`{s}`")
            .ToArray();
        return schemas.Length > 0 ? string.Join(", ", schemas) : "`kernel`, `intelligence`, `execution`";
    }

    private static string ResolveExpectedServiceSchema(
        CadenceService svc,
        IReadOnlyList<CodeMetadataCatalog.DatabaseModuleSchema> dbModules,
        string expectedKernelSchemas)
    {
        if (!svc.Database)
        {
            return "none";
        }

        if (string.Equals(svc.Name, "kernel", StringComparison.Ordinal))
        {
            return expectedKernelSchemas;
        }

        var module = dbModules.FirstOrDefault(m => string.Equals(m.Module, svc.Name, StringComparison.Ordinal));
        string schema = module?.Schema ?? (string.Equals(svc.Name, "execution", StringComparison.Ordinal) ? "edc" : svc.Name);
        return $"`{schema}`";
    }

    private static Dictionary<string, ServicePortMapping> ParseDocMappings(string filePath, string repoRoot)
    {
        var result = new Dictionary<string, ServicePortMapping>(StringComparer.OrdinalIgnoreCase);
        string relativePath = Path.GetRelativePath(repoRoot, filePath);
        string[] lines = File.ReadAllLines(filePath);

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNumber = i + 1;
            string line = lines[i].Trim();
            if (!line.StartsWith('|') || !line.EndsWith('|')) continue;

            if (line.Contains("---", StringComparison.Ordinal) ||
                (line.Contains("service", StringComparison.OrdinalIgnoreCase) && line.Contains("host port", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string[] parts = line.Split('|', StringSplitOptions.None);
            ExtractAndAddDocPair(parts, 1, 2, 3, relativePath, lineNumber, result);
            ExtractAndAddDocPair(parts, 4, 5, 6, relativePath, lineNumber, result);
        }

        return result;
    }

    private static void ExtractAndAddDocPair(
        string[] parts,
        int nameIdx,
        int portIdx,
        int schemaIdx,
        string relativePath,
        int lineNumber,
        Dictionary<string, ServicePortMapping> result)
    {
        if (nameIdx >= parts.Length || portIdx >= parts.Length) return;

        string nameCell = parts[nameIdx].Trim();
        string portCell = parts[portIdx].Trim().Trim('`');
        string schemaCell = schemaIdx < parts.Length ? parts[schemaIdx].Trim() : "";

        if (string.IsNullOrWhiteSpace(nameCell) || string.IsNullOrWhiteSpace(portCell)) return;

        if (int.TryParse(portCell, out int port))
        {
            string canonical = NormalizeServiceName(nameCell);
            if (!string.IsNullOrEmpty(canonical) && !result.ContainsKey(canonical))
            {
                result[canonical] = new ServicePortMapping(nameCell, canonical, port, relativePath, lineNumber, schemaCell);
            }
        }
    }

    private static Dictionary<string, ServicePortMapping> ParseGatewayMappings(string filePath, string repoRoot)
    {
        var result = new Dictionary<string, ServicePortMapping>(StringComparer.OrdinalIgnoreCase);
        string relativePath = Path.GetRelativePath(repoRoot, filePath);
        string[] lines = File.ReadAllLines(filePath);

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNumber = i + 1;
            string line = lines[i];

            var match = GatewayServiceUrlRegex().Match(line);
            if (match.Success)
            {
                string rawName = match.Groups[1].Value;
                if (int.TryParse(match.Groups[2].Value, out int port))
                {
                    string canonical = NormalizeServiceName(rawName);
                    if (!string.IsNullOrEmpty(canonical) && !result.ContainsKey(canonical))
                    {
                        result[canonical] = new ServicePortMapping(rawName, canonical, port, relativePath, lineNumber);
                    }
                }
            }
        }

        return result;
    }

    private static Dictionary<string, ServicePortMapping> ParseAppHostMappings(string filePath, string repoRoot)
    {
        var result = new Dictionary<string, ServicePortMapping>(StringComparer.OrdinalIgnoreCase);
        string relativePath = Path.GetRelativePath(repoRoot, filePath);
        string[] lines = File.ReadAllLines(filePath);

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNumber = i + 1;
            string line = lines[i];

            var serviceMatch = AppHostServiceRegex().Match(line);
            if (serviceMatch.Success)
            {
                string rawName = serviceMatch.Groups[1].Value;
                if (int.TryParse(serviceMatch.Groups[2].Value, out int port))
                {
                    string canonical = NormalizeServiceName(rawName);
                    if (!string.IsNullOrEmpty(canonical) && !result.ContainsKey(canonical))
                    {
                        result[canonical] = new ServicePortMapping(rawName, canonical, port, relativePath, lineNumber);
                    }
                }
            }

            var tupleMatch = AppHostTupleRegex().Match(line);
            if (tupleMatch.Success)
            {
                string rawName = tupleMatch.Groups[1].Value;
                if (int.TryParse(tupleMatch.Groups[2].Value, out int port))
                {
                    string canonical = NormalizeServiceName(rawName);
                    if (!string.IsNullOrEmpty(canonical) && !result.ContainsKey(canonical))
                    {
                        result[canonical] = new ServicePortMapping(rawName, canonical, port, relativePath, lineNumber);
                    }
                }
            }

            if (line.Contains("AddProject", StringComparison.Ordinal) && line.Contains("\"gateway\"", StringComparison.Ordinal))
            {
                for (int j = i; j < Math.Min(lines.Length, i + 3); j++)
                {
                    var endpointMatch = AppHostHttpEndpointRegex().Match(lines[j]);
                    if (endpointMatch.Success && int.TryParse(endpointMatch.Groups[1].Value, out int port))
                    {
                        const string canonical = "gateway";
                        if (!result.ContainsKey(canonical))
                        {
                            result[canonical] = new ServicePortMapping("gateway", canonical, port, relativePath, j + 1);
                        }
                        break;
                    }
                }
            }

            if (line.Contains("AddContainer", StringComparison.Ordinal) && line.Contains("\"keycloak\"", StringComparison.Ordinal))
            {
                for (int j = i; j < Math.Min(lines.Length, i + 3); j++)
                {
                    var endpointMatch = AppHostHttpEndpointRegex().Match(lines[j]);
                    if (endpointMatch.Success && int.TryParse(endpointMatch.Groups[1].Value, out int port))
                    {
                        const string canonical = "keycloak";
                        if (!result.ContainsKey(canonical))
                        {
                            result[canonical] = new ServicePortMapping("keycloak", canonical, port, relativePath, j + 1);
                        }
                        break;
                    }
                }
            }
        }

        return result;
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
        return Directory.GetCurrentDirectory();
    }
}
