using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Checks that the types generated from <c>docs/openapi</c> into <c>apps/web/src/api/generated</c> are current,
/// then runs workspace typechecking (<c>pnpm run typecheck</c>) across shared frontend packages and Vue single-file components.
/// </summary>
public static class ApiClientValidatorService
{
    public static int ValidateApiClients(string? repoRoot = null, Func<string, string?>? getEnvironmentVariable = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Typechecking web OpenAPI types and the frontend workspace...");

        string webDir = Path.Combine(repoRoot, "apps", "web");
        if (!File.Exists(Path.Combine(webDir, "tsconfig.json")))
        {
            Console.WriteLine("[ERROR] apps/web/tsconfig.json not found.");
            return 1;
        }

        string pnpmCommand = OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm";
        string resolvedPnpm = FrontendQualityGateService.ResolveCommandPath(pnpmCommand, getEnvironmentVariable);

        // A spec change without regenerated web types would leave the typed clients compiling against the old API.
        if (RunCommand(repoRoot, resolvedPnpm, ["--filter", "web", "run", "generate:api", "--", "--check"], "the generated web API types", getEnvironmentVariable) != 0)
        {
            return 1;
        }

        if (RunCommand(repoRoot, resolvedPnpm, ["run", "typecheck"], "the frontend workspace TypeScript and Vue types", getEnvironmentVariable) != 0)
        {
            return 1;
        }

        Console.WriteLine("✔ Web OpenAPI types and frontend workspace typecheck passed.");
        return 0;
    }

    private static int RunCommand(
        string workingDir,
        string fileName,
        string[] arguments,
        string subject,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (getEnvironmentVariable is not null && getEnvironmentVariable("PATH") is { } pathOverride)
        {
            psi.Environment["PATH"] = pathOverride;
        }
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
