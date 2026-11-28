using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class DevEnvironmentValidatorService
{
    [GeneratedRegex(@"GetServiceUrl\s*\(\s*""([^""]+)""\s*,\s*(\d+)\s*\)")]
    private static partial Regex GatewayServiceUrlRegex();

    [GeneratedRegex(@"Service\s*\(\s*""([^""]+)""\s*,\s*[^,]+\s*,\s*(\d+)")]
    private static partial Regex AppHostServiceRegex();

    [GeneratedRegex(@"\(\s*""([A-Z0-9_]+_SERVICE_URL)""\s*,\s*(\d+)\s*\)")]
    private static partial Regex AppHostTupleRegex();

    [GeneratedRegex(@"\.WithHttpEndpoint\s*\(\s*port:\s*(\d+)")]
    private static partial Regex AppHostHttpEndpointRegex();

    [GeneratedRegex(@"AddViteApp\s*\(\s*""([^""]+)""")]
    private static partial Regex AppHostViteAppRegex();

    [GeneratedRegex(@"endpoint\.Port\s*=\s*(\d+)")]
    private static partial Regex AppHostEndpointPortAssignmentRegex();

    [GeneratedRegex(@"\bDEV_PORT\s*=\s*(\d+)")]
    private static partial Regex ViteDevPortConstRegex();

    [GeneratedRegex(@"\bport\s*:\s*(\d+)")]
    private static partial Regex ViteServerPortLiteralRegex();

    private static readonly (string RawServiceName, string RelativeConfigPath)[] ViteApps =
    [
        ("web", Path.Combine("apps", "web", "vite.config.js")),
        ("subject-portal", Path.Combine("apps", "subject-portal", "vite.config.js")),
    ];

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

        if (portCell.Contains('/', StringComparison.Ordinal) && nameCell.Contains('/', StringComparison.Ordinal))
        {
            string[] nameParts = nameCell.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            string[] portParts = portCell.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (nameParts.Length == portParts.Length)
            {
                for (int i = 0; i < nameParts.Length; i++)
                {
                    if (int.TryParse(portParts[i].Trim('`'), out int splitPort))
                    {
                        string canonical = NormalizeServiceName(nameParts[i]);
                        if (!string.IsNullOrEmpty(canonical) && !result.ContainsKey(canonical))
                        {
                            result[canonical] = new ServicePortMapping(nameParts[i], canonical, splitPort, relativePath, lineNumber, schemaCell);
                        }
                    }
                }
                return;
            }
        }

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

            var viteMatch = AppHostViteAppRegex().Match(line);
            if (viteMatch.Success)
            {
                string rawName = viteMatch.Groups[1].Value;
                string canonical = NormalizeServiceName(rawName);
                if (!string.IsNullOrEmpty(canonical) && !result.ContainsKey(canonical))
                {
                    for (int j = i; j < Math.Min(lines.Length, i + 6); j++)
                    {
                        var assignMatch = AppHostEndpointPortAssignmentRegex().Match(lines[j]);
                        var httpMatch = assignMatch.Success ? assignMatch : AppHostHttpEndpointRegex().Match(lines[j]);
                        if (httpMatch.Success && int.TryParse(httpMatch.Groups[1].Value, out int port))
                        {
                            result[canonical] = new ServicePortMapping(rawName, canonical, port, relativePath, j + 1);
                            break;
                        }
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

    private static Dictionary<string, ServicePortMapping> ParseViteMappings(string repoRoot)
    {
        var result = new Dictionary<string, ServicePortMapping>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rawName, relativeConfigPath) in ViteApps)
        {
            string fullPath = Path.Combine(repoRoot, relativeConfigPath);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(repoRoot, fullPath);
            string[] lines = File.ReadAllLines(fullPath);
            string canonical = NormalizeServiceName(rawName);
            if (string.IsNullOrEmpty(canonical))
            {
                continue;
            }

            bool matched = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var constMatch = ViteDevPortConstRegex().Match(lines[i]);
                if (constMatch.Success && int.TryParse(constMatch.Groups[1].Value, out int constPort))
                {
                    result[canonical] = new ServicePortMapping(rawName, canonical, constPort, relativePath, i + 1);
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    var portMatch = ViteServerPortLiteralRegex().Match(lines[i]);
                    if (portMatch.Success && int.TryParse(portMatch.Groups[1].Value, out int literalPort))
                    {
                        result[canonical] = new ServicePortMapping(rawName, canonical, literalPort, relativePath, i + 1);
                        break;
                    }
                }
            }
        }

        return result;
    }
}
