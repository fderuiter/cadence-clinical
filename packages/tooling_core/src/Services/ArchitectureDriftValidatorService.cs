using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class ArchitectureDriftValidatorService
{
    [GeneratedRegex(@"^\s\s([a-zA-Z0-9_\-]+):\s*$")]
    private static partial Regex ComposeServiceRegex();

    [GeneratedRegex(@"^\s*APP_NAME:\s*(?<app>[a-z0-9-]+)\s*$", RegexOptions.Multiline)]
    private static partial Regex ComposeAppNameRegex();

    [GeneratedRegex(@"\b(?:5173|7687|8017|8019)\b")]
    private static partial Regex RetiredPortRegex();

    public static int ValidateArchitectureDrift(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating system architecture topology and document parity...");

        string composePath = Path.Combine(repoRoot, "docker", "docker-compose.yml");
        string archPath = Path.Combine(repoRoot, "ARCHITECTURE.md");
        string opDbPath = Path.Combine(repoRoot, "docs", "OPERATIONAL_DATABASE.md");
        string sigContractPath = Path.Combine(repoRoot, "docs", "SDLC", "Signature_Token_Cryptographic_Contract.md");
        bool hasTopology = File.Exists(Path.Combine(repoRoot, "packages", "topology", "src", "ServiceTopology.cs"));

        if (!File.Exists(composePath))
        {
            Console.WriteLine($"[ERROR] docker-compose.yml not found at {composePath}");
            return 1;
        }

        var composeServices = ExtractDockerComposeServices(composePath);
        if (composeServices.Count == 0)
        {
            Console.WriteLine("[ERROR] No services found in docker-compose.yml.");
            return 1;
        }

        bool hasError = false;
        hasError |= !ValidateComposeAppTargets(repoRoot, composePath);
        hasError |= !ValidateDockerComposeConfig(repoRoot, composePath);

        if (!File.Exists(archPath))
        {
            if (hasTopology)
            {
                Console.WriteLine($"[ERROR] ARCHITECTURE.md not found at {archPath}");
                return 1;
            }
        }
        else
        {
            string archText = File.ReadAllText(archPath);
            foreach (string svc in composeServices.Order(StringComparer.Ordinal))
            {
                if (!archText.Contains(svc, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[ERROR] Compose service '{svc}' is missing from ARCHITECTURE.md.");
                    hasError = true;
                }
            }

            if (hasTopology || Directory.Exists(Path.Combine(repoRoot, "apps")))
            {
                hasError |= !ValidateArchitectureDoc(repoRoot, archText);
            }
        }

        if (hasTopology || File.Exists(opDbPath))
        {
            hasError |= !ValidateOperationalDatabaseDoc(repoRoot, opDbPath);
        }

        if (hasTopology || File.Exists(sigContractPath))
        {
            hasError |= !ValidateSignatureContractDoc(repoRoot, sigContractPath);
        }

        if (hasError)
        {
            Console.WriteLine("❌ Architecture drift validation failed.");
            return 1;
        }

        Console.WriteLine($"✔ Architecture drift validation completed cleanly across {composeServices.Count} services.");
        return 0;
    }

    private static bool ValidateArchitectureDoc(string repoRoot, string archText)
    {
        bool ok = true;
        var topology = CodeMetadataCatalog.GetTopology(repoRoot);
        var signedEndpoints = CodeMetadataCatalog.GetSignedEndpoints(repoRoot);
        var durableStores = CodeMetadataCatalog.GetDurableSigTokenStores(repoRoot);
        var auditHosts = CodeMetadataCatalog.GetAuditInterceptorHosts(repoRoot);

        var retiredMatch = RetiredPortRegex().Match(archText);
        if (retiredMatch.Success)
        {
            Console.WriteLine($"[ERROR] ARCHITECTURE.md references retired port '{retiredMatch.Value}'.");
            ok = false;
        }

        if (archText.Contains("falls back to the local database in Development", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[ERROR] ARCHITECTURE.md falsely states that database resolution falls back to a local database in Development.");
            ok = false;
        }

        string[] lines = archText.Split('\n');
        CheckArchitectureServiceRow(lines, "gateway", "apps/gateway/README.md", topology.GatewayPort, [], ref ok);

        foreach (var svc in topology.Services)
        {
            CheckArchitectureServiceRow(lines, svc.Directory, $"apps/{svc.Directory}/README.md", svc.Port, svc.RoutePrefixes, ref ok);
        }

        if (!HasTableRowContaining(lines, "`apps/web`", $"port {topology.WebPort.ToString(CultureInfo.InvariantCulture)}"))
        {
            Console.WriteLine($"[ERROR] ARCHITECTURE.md runtime components table must document apps/web on port {topology.WebPort}.");
            ok = false;
        }

        if (!HasTableRowContaining(lines, "`apps/subject-portal`", $"port {topology.SubjectPortalPort.ToString(CultureInfo.InvariantCulture)}"))
        {
            Console.WriteLine($"[ERROR] ARCHITECTURE.md runtime components table must document apps/subject-portal on port {topology.SubjectPortalPort}.");
            ok = false;
        }

        var appHostRow = lines.FirstOrDefault(l => l.TrimStart().StartsWith('|') && l.Contains("`apps/apphost`", StringComparison.Ordinal));
        if (appHostRow is null)
        {
            Console.WriteLine("[ERROR] ARCHITECTURE.md runtime components table is missing the apps/apphost row.");
            ok = false;
        }
        else
        {
            foreach (string coreHost in topology.CoreBackendHosts)
            {
                if (!appHostRow.Contains($"`{coreHost}`", StringComparison.Ordinal))
                {
                    Console.WriteLine($"[ERROR] ARCHITECTURE.md apps/apphost row omits core backend host '{coreHost}'.");
                    ok = false;
                }
            }
        }

        ok &= ValidateSignedEndpointRows("ARCHITECTURE.md", lines, signedEndpoints);

        foreach (var store in durableStores)
        {
            if (!archText.Contains($"`{store.Host}` (`{store.DbContext}` → `{store.Table}`)", StringComparison.Ordinal))
            {
                Console.WriteLine($"[ERROR] ARCHITECTURE.md omits durable signature token store '{store.Host} ({store.DbContext} -> {store.Table})'.");
                ok = false;
            }
        }

        ok &= ValidateAuditHostRows("ARCHITECTURE.md", lines, auditHosts);
        return ok;
    }

    private static void CheckArchitectureServiceRow(
        string[] lines,
        string label,
        string readmePath,
        int expectedPort,
        IReadOnlyList<string> expectedRoutes,
        ref bool ok)
    {
        string? row = lines.FirstOrDefault(l => l.TrimStart().StartsWith('|') && l.Contains($"({readmePath})", StringComparison.Ordinal));
        if (row is null)
        {
            Console.WriteLine($"[ERROR] ARCHITECTURE.md §3 service table is missing row for '{label}' ({readmePath}).");
            ok = false;
            return;
        }

        string[] cells = row.Split('|');
        if (cells.Length < 5)
        {
            Console.WriteLine($"[ERROR] ARCHITECTURE.md row for '{label}' has too few columns.");
            ok = false;
            return;
        }

        string portCell = cells[3].Trim();
        string portPrefix = expectedPort.ToString(CultureInfo.InvariantCulture);
        if (!portCell.Equals(portPrefix, StringComparison.Ordinal) &&
            !portCell.StartsWith(portPrefix + " ", StringComparison.Ordinal))
        {
            Console.WriteLine($"[ERROR] ARCHITECTURE.md specifies port '{portCell}' for '{label}', expected '{portPrefix}'.");
            ok = false;
        }

        string routesCell = cells[4];
        foreach (string prefix in expectedRoutes)
        {
            if (!routesCell.Contains($"`{prefix}`", StringComparison.Ordinal))
            {
                Console.WriteLine($"[ERROR] ARCHITECTURE.md omits gateway route prefix '{prefix}' for service '{label}'.");
                ok = false;
            }
        }
    }

    private static bool ValidateOperationalDatabaseDoc(string repoRoot, string opDbPath)
    {
        if (!File.Exists(opDbPath))
        {
            Console.WriteLine($"[ERROR] docs/OPERATIONAL_DATABASE.md not found at {opDbPath}");
            return false;
        }

        var expectedModules = CodeMetadataCatalog.GetDatabaseModules(repoRoot);
        if (expectedModules.Count == 0)
        {
            Console.WriteLine("[ERROR] docs/schema/README.md yielded zero database modules.");
            return false;
        }

        string[] lines = File.ReadAllLines(opDbPath);
        var docRows = new Dictionary<string, (string Host, string Schema, string DbContext, string ProjectRelativePath)>(StringComparer.Ordinal);
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("| [", StringComparison.Ordinal) || !line.Contains("](schema/", StringComparison.Ordinal))
            {
                continue;
            }

            string[] cells = line.Split('|');
            if (cells.Length < 6)
            {
                continue;
            }

            string moduleCell = cells[1].Trim();
            int closeBracket = moduleCell.IndexOf(']');
            if (closeBracket <= 1)
            {
                continue;
            }

            string moduleName = moduleCell[1..closeBracket];
            string hostCell = cells[2].Trim();
            string schemaCell = cells[3].Trim().Trim('`');
            string dbContextCell = cells[4].Trim().Trim('`');
            string projectCell = cells[5].Trim().Trim('`');

            docRows[moduleName] = (hostCell, schemaCell, dbContextCell, projectCell);
        }

        bool ok = true;
        foreach (var expected in expectedModules)
        {
            string expectedHost = string.Equals(expected.Host, "kernel", StringComparison.Ordinal)
                ? "`kernel` (`apps/kernel-api`)"
                : $"`{expected.Host}`";
            if (!docRows.TryGetValue(expected.Module, out var actual))
            {
                Console.WriteLine($"[ERROR] docs/OPERATIONAL_DATABASE.md omits database module '{expected.Module}' (schema '{expected.Schema}', project '{expected.ProjectRelativePath}').");
                ok = false;
                continue;
            }

            if (!string.Equals(actual.Host, expectedHost, StringComparison.Ordinal) ||
                !string.Equals(actual.Schema, expected.Schema, StringComparison.Ordinal) ||
                !string.Equals(actual.DbContext, expected.DbContext, StringComparison.Ordinal) ||
                !string.Equals(actual.ProjectRelativePath, expected.ProjectRelativePath, StringComparison.Ordinal))
            {
                Console.WriteLine(
                    $"[ERROR] docs/OPERATIONAL_DATABASE.md drift for module '{expected.Module}': " +
                    $"found ({actual.Host}, {actual.Schema}, {actual.DbContext}, {actual.ProjectRelativePath}), " +
                    $"expected ({expectedHost}, {expected.Schema}, {expected.DbContext}, {expected.ProjectRelativePath}).");
                ok = false;
            }
        }

        foreach (string extra in docRows.Keys.Except(expectedModules.Select(m => m.Module), StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            Console.WriteLine($"[ERROR] docs/OPERATIONAL_DATABASE.md lists unknown database module '{extra}' not present in docs/schema/README.md.");
            ok = false;
        }

        return ok;
    }

    private static bool ValidateSignatureContractDoc(string repoRoot, string sigContractPath)
    {
        if (!File.Exists(sigContractPath))
        {
            Console.WriteLine($"[ERROR] Signature_Token_Cryptographic_Contract.md not found at {sigContractPath}");
            return false;
        }

        string text = File.ReadAllText(sigContractPath);
        string[] lines = text.Split('\n');
        bool ok = true;

        if (text.Contains("session.begin_nested()", StringComparison.Ordinal) ||
            text.Contains("PermissionError", StringComparison.Ordinal) ||
            text.Contains("/api/v1/execution/batch-sign-off", StringComparison.Ordinal))
        {
            Console.WriteLine("[ERROR] docs/SDLC/Signature_Token_Cryptographic_Contract.md contains stale Python batch-sign-off claims ('session.begin_nested()', 'PermissionError', or '/api/v1/execution/batch-sign-off').");
            ok = false;
        }

        if (!text.Contains("Trace-14 & Trace-15", StringComparison.Ordinal))
        {
            Console.WriteLine("[ERROR] docs/SDLC/Signature_Token_Cryptographic_Contract.md must preserve the 'Trace-14 & Trace-15' heading.");
            ok = false;
        }

        var expectedEndpoints = CodeMetadataCatalog.GetSignedEndpoints(repoRoot);
        var expectedStores = CodeMetadataCatalog.GetDurableSigTokenStores(repoRoot);

        ok &= ValidateSignedEndpointRows("docs/SDLC/Signature_Token_Cryptographic_Contract.md", lines, expectedEndpoints);
        ok &= ValidateDurableStoreRows("docs/SDLC/Signature_Token_Cryptographic_Contract.md", lines, expectedStores);
        return ok;
    }

    private static bool ValidateSignedEndpointRows(
        string docLabel,
        string[] lines,
        IReadOnlyList<CodeMetadataCatalog.SignedEndpointMetadata> expectedEndpoints)
    {
        var actualRows = new List<(string Host, string EndpointCell, string SemanticAction, string ActionCell)>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (!line.StartsWith('|') || line.Contains("---", StringComparison.Ordinal))
            {
                continue;
            }

            string[] cells = line.Split('|');
            if (cells.Length < 6)
            {
                continue;
            }

            string endpointCell = cells[2].Trim();
            if (!endpointCell.StartsWith("`POST /", StringComparison.Ordinal) &&
                !endpointCell.StartsWith("`PUT /", StringComparison.Ordinal) &&
                !endpointCell.StartsWith("`PATCH /", StringComparison.Ordinal) &&
                !endpointCell.StartsWith("`DELETE /", StringComparison.Ordinal))
            {
                continue;
            }

            actualRows.Add((
                cells[1].Trim().Trim('`'),
                endpointCell,
                cells[3].Trim().Trim('`'),
                cells[4].Trim()));
        }

        bool ok = true;
        foreach (var expected in expectedEndpoints)
        {
            string expectedPrimary = $"`{expected.Method} {expected.Route}`";
            var match = actualRows.FirstOrDefault(r =>
                string.Equals(r.Host, expected.Host, StringComparison.Ordinal) &&
                r.EndpointCell.Contains(expectedPrimary, StringComparison.Ordinal));
            if (match.Host is null)
            {
                Console.WriteLine($"[ERROR] {docLabel} omits RequireElectronicSignature endpoint '{expected.Host}: {expected.Method} {expected.Route}'.");
                ok = false;
                continue;
            }

            if (!string.Equals(match.SemanticAction, expected.SemanticAction, StringComparison.Ordinal) ||
                !match.ActionCell.Contains($"`{expected.ActionTemplate}`", StringComparison.Ordinal))
            {
                Console.WriteLine($"[ERROR] {docLabel} signature metadata mismatch for '{expected.Method} {expected.Route}' (expected semantic_action '{expected.SemanticAction}', action '{expected.ActionTemplate}').");
                ok = false;
            }
        }

        if (actualRows.Count != expectedEndpoints.Count)
        {
            Console.WriteLine($"[ERROR] {docLabel} documents {actualRows.Count} RequireElectronicSignature endpoint rows, expected {expectedEndpoints.Count}.");
            ok = false;
        }

        return ok;
    }

    private static bool ValidateDurableStoreRows(
        string docLabel,
        string[] lines,
        IReadOnlyList<CodeMetadataCatalog.DurableSigTokenStoreMetadata> expectedStores)
    {
        var actualStores = new List<(string Host, string DbContext, string Table, string FilePath)>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (!line.StartsWith('|') || !line.Contains("consumed_signature_tokens", StringComparison.Ordinal))
            {
                continue;
            }

            string[] cells = line.Split('|');
            if (cells.Length < 6)
            {
                continue;
            }

            actualStores.Add((
                cells[1].Trim().Trim('`'),
                cells[2].Trim().Trim('`'),
                cells[3].Trim().Trim('`'),
                cells[4].Trim().Trim('`')));
        }

        bool ok = true;
        foreach (var expected in expectedStores)
        {
            bool found = actualStores.Any(a =>
                string.Equals(a.Host, expected.Host, StringComparison.Ordinal) &&
                string.Equals(a.DbContext, expected.DbContext, StringComparison.Ordinal) &&
                string.Equals(a.Table, expected.Table, StringComparison.Ordinal) &&
                string.Equals(a.FilePath, expected.RelativeFilePath, StringComparison.Ordinal));
            if (!found)
            {
                Console.WriteLine($"[ERROR] {docLabel} omits durable signature token store '{expected.Host} ({expected.DbContext} -> {expected.Table})'.");
                ok = false;
            }
        }

        if (actualStores.Count != expectedStores.Count)
        {
            Console.WriteLine($"[ERROR] {docLabel} documents {actualStores.Count} durable signature token store rows, expected {expectedStores.Count}.");
            ok = false;
        }

        return ok;
    }

    private static bool ValidateAuditHostRows(
        string docLabel,
        string[] lines,
        IReadOnlyList<CodeMetadataCatalog.AuditHostMetadata> expectedHosts)
    {
        var actualHosts = new List<(string Host, string DbContext, string Schema, string FilePath)>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (!line.StartsWith('|') || !line.Contains("DbContext`", StringComparison.Ordinal) || !line.Contains("Program.cs`", StringComparison.Ordinal))
            {
                continue;
            }

            string[] cells = line.Split('|');
            if (cells.Length < 6)
            {
                continue;
            }

            actualHosts.Add((
                cells[1].Trim().Trim('`'),
                cells[2].Trim().Trim('`'),
                cells[3].Trim().Trim('`'),
                cells[4].Trim().Trim('`')));
        }

        bool ok = true;
        foreach (var expected in expectedHosts)
        {
            bool found = actualHosts.Any(a =>
                string.Equals(a.Host, expected.Host, StringComparison.Ordinal) &&
                string.Equals(a.DbContext, expected.DbContext, StringComparison.Ordinal) &&
                string.Equals(a.Schema, expected.Schema, StringComparison.Ordinal) &&
                string.Equals(a.FilePath, expected.RelativeFilePath, StringComparison.Ordinal));
            if (!found)
            {
                Console.WriteLine($"[ERROR] {docLabel} omits GxPAuditInterceptor host '{expected.Host} ({expected.DbContext}, schema {expected.Schema})'.");
                ok = false;
            }
        }

        if (actualHosts.Count != expectedHosts.Count)
        {
            Console.WriteLine($"[ERROR] {docLabel} documents {actualHosts.Count} GxPAuditInterceptor host rows, expected {expectedHosts.Count}.");
            ok = false;
        }

        return ok;
    }

    private static bool HasTableRowContaining(string[] lines, string tokenA, string tokenB) =>
        lines.Any(l => l.TrimStart().StartsWith('|') &&
                       l.Contains(tokenA, StringComparison.Ordinal) &&
                       l.Contains(tokenB, StringComparison.Ordinal));

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

        var versionCheck = RunDockerProcess(repoRoot, ["compose", "version"]);
        if (versionCheck.ExitCode != 0)
        {
            return true;
        }

        var configCheck = RunDockerProcess(repoRoot, ["compose", "-f", composePath, "config", "-q"]);
        if (configCheck.ExitCode != 0)
        {
            string details = string.IsNullOrWhiteSpace(configCheck.Error) ? configCheck.Output : configCheck.Error;
            Console.WriteLine($"[ERROR] 'docker compose -f docker/docker-compose.yml config -q' failed: {details.Trim()}");
            return false;
        }

        return true;
    }

    private static (int ExitCode, string Output, string Error) RunDockerProcess(string workingDirectory, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo("docker")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = PosixProcessLauncher.Start(psi);
        if (process is null)
        {
            return (1, string.Empty, "Failed to start docker process.");
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
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
