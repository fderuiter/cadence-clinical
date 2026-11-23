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
        int LineNumber
    );

    [GeneratedRegex(@"GetServiceUrl\s*\(\s*""([^""]+)""\s*,\s*(\d+)\s*\)")]
    private static partial Regex GatewayServiceUrlRegex();

    [GeneratedRegex(@"Service\s*\(\s*""([^""]+)""\s*,\s*[^,]+\s*,\s*(\d+)")]
    private static partial Regex AppHostServiceRegex();

    [GeneratedRegex(@"\(\s*""([A-Z0-9_]+_SERVICE_URL)""\s*,\s*(\d+)\s*\)")]
    private static partial Regex AppHostTupleRegex();

    [GeneratedRegex(@"\.WithHttpEndpoint\s*\(\s*port:\s*(\d+)")]
    private static partial Regex AppHostHttpEndpointRegex();

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

        // Check 1: Mismatches/missing between docs and gateway
        foreach (var (canonical, gatewayInfo) in gatewayMappings)
        {
            if (!docMappings.TryGetValue(canonical, out var docInfo))
            {
                Console.WriteLine($"[ERROR] Service '{gatewayInfo.RawServiceName}' defined in {gatewayInfo.RelativeFilePath}:{gatewayInfo.LineNumber} (port {gatewayInfo.Port}) is missing from {Path.GetRelativePath(repoRoot, docPath)}.");
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
                Console.WriteLine($"[ERROR] Service '{appHostInfo.RawServiceName}' defined in {appHostInfo.RelativeFilePath}:{appHostInfo.LineNumber} (port {appHostInfo.Port}) is missing from {Path.GetRelativePath(repoRoot, docPath)}.");
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

        if (lower.EndsWith("_service_url"))
        {
            lower = lower[..^"_service_url".Length];
        }

        if (lower.Contains("gateway")) return "gateway";
        if (lower.Contains("kernel")) return "kernel";
        if (lower.Contains("keycloak")) return "keycloak";

        int paren = lower.IndexOf('(');
        if (paren >= 0) lower = lower[..paren].Trim();

        return lower.Replace('-', '_').Trim();
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

            if (line.Contains("---") || (line.ToLowerInvariant().Contains("service") && line.ToLowerInvariant().Contains("host port")))
            {
                continue;
            }

            string[] parts = line.Split('|', StringSplitOptions.None);
            ExtractAndAddDocPair(parts, 1, 2, relativePath, lineNumber, result);
            ExtractAndAddDocPair(parts, 4, 5, relativePath, lineNumber, result);
        }

        return result;
    }

    private static void ExtractAndAddDocPair(string[] parts, int nameIdx, int portIdx, string relativePath, int lineNumber, Dictionary<string, ServicePortMapping> result)
    {
        if (nameIdx >= parts.Length || portIdx >= parts.Length) return;

        string nameCell = parts[nameIdx].Trim();
        string portCell = parts[portIdx].Trim().Trim('`');

        if (string.IsNullOrWhiteSpace(nameCell) || string.IsNullOrWhiteSpace(portCell)) return;

        if (int.TryParse(portCell, out int port))
        {
            string canonical = NormalizeServiceName(nameCell);
            if (!string.IsNullOrEmpty(canonical) && !result.ContainsKey(canonical))
            {
                result[canonical] = new ServicePortMapping(nameCell, canonical, port, relativePath, lineNumber);
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

            if (line.Contains("AddProject") && line.Contains("\"gateway\""))
            {
                for (int j = i; j < Math.Min(lines.Length, i + 3); j++)
                {
                    var endpointMatch = AppHostHttpEndpointRegex().Match(lines[j]);
                    if (endpointMatch.Success && int.TryParse(endpointMatch.Groups[1].Value, out int port))
                    {
                        string canonical = "gateway";
                        if (!result.ContainsKey(canonical))
                        {
                            result[canonical] = new ServicePortMapping("gateway", canonical, port, relativePath, j + 1);
                        }
                        break;
                    }
                }
            }

            if (line.Contains("AddContainer") && line.Contains("\"keycloak\""))
            {
                for (int j = i; j < Math.Min(lines.Length, i + 3); j++)
                {
                    var endpointMatch = AppHostHttpEndpointRegex().Match(lines[j]);
                    if (endpointMatch.Success && int.TryParse(endpointMatch.Groups[1].Value, out int port))
                    {
                        string canonical = "keycloak";
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

    private static string FindRepoRoot() => RepositoryRoot.Find();
}
