using System;
using System.Diagnostics;
using System.IO;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Checks that the types generated from <c>docs/openapi</c> into <c>apps/web/src/api/generated</c> are current,
/// then typechecks the web app, which compiles every <c>apps/web/src/api</c> client against its OpenAPI types.
/// </summary>
public static class ApiClientValidatorService
{
    public static int ValidateApiClients(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Typechecking the web API clients against their OpenAPI types...");

        string webDir = Path.Combine(repoRoot, "apps", "web");
        if (!File.Exists(Path.Combine(webDir, "tsconfig.json")))
        {
            Console.WriteLine("[ERROR] apps/web/tsconfig.json not found.");
            return 1;
        }

        // A spec change without regenerated web types would leave the typed clients compiling against the old API.
        if (RunInWebDir(webDir, "node", ["scripts/generate-api-types.js", "--check"], "the generated web API types") != 0)
        {
            return 1;
        }

        if (RunInWebDir(webDir, "pnpm", ["exec", "tsc", "--noEmit"], "the web API clients") != 0)
        {
            return 1;
        }

        Console.WriteLine("✔ Web API clients typecheck against their OpenAPI types.");
        return 0;
    }

    private static int RunInWebDir(string webDir, string fileName, string[] arguments, string subject)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = webDir,
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
            process = Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception ex)
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
