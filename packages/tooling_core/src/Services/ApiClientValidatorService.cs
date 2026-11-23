using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Checks that the types generated from <c>docs/openapi</c> into <c>apps/web/src/api/generated</c> are current,
/// then typechecks the frontend workspace (`pnpm -r typecheck`), compiling every API client and Vue SFC against its OpenAPI types.
/// </summary>
public static class ApiClientValidatorService
{
    private static readonly string[] CanvasContractSourcePaths =
    [
        "scripts/generate-api-types.js",
        "scripts/generate-canvas-contracts.js",
        "packages/kernel/src/Cadence.Kernel.Abstractions/References/NodeKinds.cs",
        "packages/security/src/Rbac/RoleNormalization.cs",
        "packages/security/src/Rbac/PermissionMatrix.cs",
        "packages/security/src/Rbac/PermissionMatrix.ServiceRoles.cs",
        "packages/security/src/Rbac/PermissionMatrix.RolePermissions.cs",
        "apps/execution/src/Domain/Subjects/SubjectStateCodec.cs",
        "apps/execution/src/Domain/Subjects/SubjectTransitionGraph.cs",
        "apps/execution/src/Application/Subjects/SubjectAuthority.cs",
    ];

    public static int ValidateApiClients(
        string? repoRoot = null,
        IEnumerable<string>? targetFiles = null,
        bool preCommit = false)
    {
        var explicitFiles = targetFiles?.ToList();
        if (explicitFiles is { Count: 0 })
        {
            return 0;
        }

        repoRoot ??= FindRepoRoot();

        if (preCommit || explicitFiles is not null)
        {
            IReadOnlyList<string> candidateFiles = explicitFiles ?? PreCommitOpenApiService.GetStagedFiles(repoRoot);
            if (!candidateFiles.Any(f => AffectsApiClients(repoRoot, f)))
            {
                Console.WriteLine("Staged changes do not touch docs/openapi or frontend API client inputs. Skipping web API client typecheck...");
                return 0;
            }
        }

        Console.WriteLine("Typechecking the web API clients against their OpenAPI types...");

        string webDir = Path.Combine(repoRoot, "apps", "web");
        if (!File.Exists(Path.Combine(webDir, "tsconfig.json")))
        {
            Console.WriteLine("[ERROR] apps/web/tsconfig.json not found.");
            return 1;
        }

        const string pnpmCommand = "pnpm";

        // A spec change without regenerated web types would leave the typed clients compiling against the old API.
        if (RunCommand(repoRoot, pnpmCommand, ["--filter", "web", "run", "generate:api", "--", "--check"], "the generated web API types") != 0)
        {
            return 1;
        }

        if (RunCommand(repoRoot, pnpmCommand, ["-r", "typecheck"], "the frontend workspace and web API clients") != 0)
        {
            return 1;
        }

        Console.WriteLine("✔ Web API clients typecheck against their OpenAPI types.");
        return 0;
    }

    public static bool AffectsApiClients(string repoRoot, string file)
    {
        string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, file);
        if (string.IsNullOrEmpty(rel))
        {
            return false;
        }

        if (rel.StartsWith("docs/openapi/", StringComparison.OrdinalIgnoreCase) &&
            !rel.StartsWith("docs/openapi/legacy/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (rel.StartsWith("apps/web/", StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith("apps/subject-portal/", StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith("packages/ui/", StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith("packages/usdm-schemas/", StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith("packages/demo-fixtures/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(rel, "package.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rel, "pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rel, "pnpm-workspace.yaml", StringComparison.OrdinalIgnoreCase) ||
            (!rel.Contains('/', StringComparison.Ordinal) &&
             rel.StartsWith("tsconfig", StringComparison.OrdinalIgnoreCase) &&
             rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return CanvasContractSourcePaths.Any(p => string.Equals(rel, p, StringComparison.OrdinalIgnoreCase));
    }

    private static int RunCommand(string workingDir, string fileName, string[] arguments, string subject)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        Process? process;
        try
        {
            process = PosixProcessLauncher.Start(psi);
        }
        catch (Win32Exception ex)
        {
            Console.WriteLine($"[ERROR] Could not run {fileName} ({ex.Message}). Install Node.js and pnpm, then run 'pnpm install'.");
            return 1;
        }

        if (process == null)
        {
            Console.WriteLine($"[ERROR] Could not start '{fileName} {string.Join(' ', arguments)}'.");
            return 1;
        }

        using (process)
        {
            var stderrTask = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            string stderr = stderrTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"[ERROR] Check failed for {subject}:");
                Console.WriteLine(stdout);
                Console.WriteLine(stderr);
                return 1;
            }
        }

        return 0;
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
