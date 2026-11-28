using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class ArchitectureDriftValidatorService
{
    [GeneratedRegex(@"^\s\s([a-zA-Z0-9_\-]+):\s*$")]
    private static partial Regex ComposeServiceRegex();

    [GeneratedRegex(@"^\s*APP_NAME:\s*(?<app>[a-z0-9-]+)\s*$", RegexOptions.Multiline)]
    private static partial Regex ComposeAppNameRegex();

    private static bool ValidateComposeAppTargets(string repoRoot, string composePath)
    {
        string composeContent = File.ReadAllText(composePath);
        var composeApps = ComposeAppNameRegex()
            .Matches(composeContent)
            .Select(m => m.Groups["app"].Value)
            .ToHashSet(StringComparer.Ordinal);

        bool valid = true;
        foreach (string appName in composeApps.Order(StringComparer.Ordinal))
        {
            string serviceSrcDir = Path.Combine(repoRoot, "apps", appName, "src");
            if (!Directory.Exists(serviceSrcDir) || Directory.GetFiles(serviceSrcDir, "*.csproj").Length == 0)
            {
                Console.WriteLine($"[ERROR] docker-compose.yml APP_NAME '{appName}' does not resolve to a .csproj under apps/{appName}/src.");
                valid = false;
            }
        }

        string appsRoot = Path.Combine(repoRoot, "apps");
        if (composeApps.Count > 0 && Directory.Exists(appsRoot))
        {
            var discoveredServiceApps = Directory.GetDirectories(appsRoot)
                .Select(dir => (Name: Path.GetFileName(dir), SrcDir: Path.Combine(dir, "src")))
                .Where(entry => Directory.Exists(entry.SrcDir) &&
                                Directory.GetFiles(entry.SrcDir, "*.csproj").Any(csproj =>
                                {
                                    string fileName = Path.GetFileName(csproj);
                                    return fileName.StartsWith("Cadence.Apps.", StringComparison.Ordinal) ||
                                           string.Equals(fileName, "Cadence.Kernel.Host.csproj", StringComparison.Ordinal);
                                }))
                .Select(entry => entry.Name)
                .Order(StringComparer.Ordinal)
                .ToList();

            foreach (string serviceApp in discoveredServiceApps.Where(app => !composeApps.Contains(app)))
            {
                Console.WriteLine($"[ERROR] .NET service 'apps/{serviceApp}/src' is missing an APP_NAME build target in docker/docker-compose.yml.");
                valid = false;
            }
        }

        return valid;
    }

    private static bool ValidateDockerComposeConfig(string repoRoot, string composePath)
    {
        if (PosixProcessLauncher.ResolveExecutablePath("docker", repoRoot) is null)
        {
            return true;
        }

        if (!ProcessPrimitives.TryRunCaptured("docker", repoRoot, ["compose", "version"], out var versionCheck) ||
            versionCheck.ExitCode != 0)
        {
            return true;
        }

        if (!ProcessPrimitives.TryRunCaptured("docker", repoRoot, ["compose", "-f", composePath, "config", "-q"], out var configCheck) ||
            configCheck.ExitCode != 0)
        {
            string details = string.IsNullOrWhiteSpace(configCheck.Stderr) ? configCheck.Stdout : configCheck.Stderr;
            Console.WriteLine($"[ERROR] 'docker compose -f docker/docker-compose.yml config -q' failed: {details.Trim()}");
            return false;
        }

        return true;
    }

    private static HashSet<string> ExtractDockerComposeServices(string composePath)
    {
        var services = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool inServicesBlock = false;

        foreach (var line in File.ReadLines(composePath))
        {
            if (line.TrimStart().StartsWith("services:", StringComparison.Ordinal))
            {
                inServicesBlock = true;
                continue;
            }

            if (inServicesBlock)
            {
                if (line.Length > 0 && !line.StartsWith(' ') && !line.StartsWith('\t') && !line.StartsWith('#'))
                {
                    inServicesBlock = false;
                    continue;
                }

                var match = ComposeServiceRegex().Match(line);
                if (match.Success)
                {
                    services.Add(match.Groups[1].Value);
                }
            }
        }

        return services;
    }
}
