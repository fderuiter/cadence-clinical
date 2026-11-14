using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static class PreCommitOpenApiService
{
    /// <summary>
    /// The <c>openapi</c> gate. When staged C# files exist, exports specs for affected services.
    /// When no C# files are staged, skips service host startup for sub-second execution.
    /// </summary>
    public static int ExecutePreCommitOpenApi(string? repoRoot = null, bool quick = false, IEnumerable<string>? targetFiles = null)
    {
        repoRoot ??= FindRepoRoot();

        if (!quick)
        {
            return OpenApiDriftValidatorService.ValidateDrift(repoRoot);
        }

        var files = targetFiles?.ToList() ?? GetStagedFiles(repoRoot);

        var csharpFiles = files
            .Where(IsCsharpFile)
            .ToList();

        if (csharpFiles.Count == 0)
        {
            Console.WriteLine("No staged C# backend changes detected. Skipping service startup (lightweight docs check)...");
            return ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot);
        }

        bool affectsSharedLibraries = csharpFiles.Any(IsSharedOrSolutionFile);
        List<string>? targetServices = null;

        if (!affectsSharedLibraries)
        {
            var detectedServices = csharpFiles
                .Select(ResolveServiceFromPath)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()!;

            if (detectedServices.Count == 0)
            {
                Console.WriteLine("Staged C# changes do not affect microservice API contracts. Skipping service startup...");
                return ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot);
            }

            targetServices = detectedServices!;
            Console.WriteLine($"Staged C# changes detected for microservice(s): {string.Join(", ", targetServices)}");
        }
        else
        {
            Console.WriteLine("Staged C# changes detected in shared packages/solution files. Triggering multi-service OpenAPI export...");
        }

        int syncResult = OpenApiSyncService.SyncOpenApiSpec(validate: true, repoRoot, targetServices);
        int docsResult = ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot);
        return syncResult != 0 || docsResult != 0 ? 1 : 0;
    }

    public static List<string> GetStagedFiles(string repoRoot)
    {
        var psi = new ProcessStartInfo("git")
        {
            Arguments = "diff --cached --name-only --diff-filter=ACMR",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return new List<string>();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) return new List<string>();

            return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static bool IsCsharpFile(string file)
    {
        string normalized = file.Replace('\\', '/');
        return normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSharedOrSolutionFile(string file)
    {
        string normalized = file.Replace('\\', '/');

        if (normalized.StartsWith("packages/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("apps/apphost/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("apps/gateway/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!normalized.Contains('/') ||
            normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("Directory.Packages.props", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static string? ResolveServiceFromPath(string file)
    {
        string normalized = file.Replace('\\', '/');
        if (!normalized.StartsWith("apps/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        string appFolder = parts[1].ToLowerInvariant();
        return appFolder switch
        {
            "ctms" => "ctms",
            "designer" => "designer",
            "econsent" => "econsent",
            "eisf" => "eisf",
            "etmf" => "etmf",
            "execution" => "execution",
            "fileshare" => "fileshare",
            "imaging" => "imaging",
            "interop" => "interop",
            "kernel-api" or "kernel" or "intelligence" => "kernel",
            "knowledge" => "knowledge",
            "notifications" => "notifications",
            "org" => "org",
            "payments" => "payments",
            "quality" => "quality",
            "safety" => "safety",
            "tickets" => "tickets",
            _ => null
        };
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
