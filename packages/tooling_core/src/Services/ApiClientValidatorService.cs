using System;
using System.Diagnostics;
using System.IO;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Typechecks the web app, which compiles every <c>apps/web/src/api</c> client against the
/// OpenAPI-generated types in <c>apps/web/src/api/types.ts</c>.
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

        var psi = new ProcessStartInfo("pnpm")
        {
            WorkingDirectory = webDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string arg in new[] { "exec", "tsc", "--noEmit" })
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
            Console.WriteLine($"[ERROR] Could not run pnpm ({ex.Message}). Install Node.js and pnpm, then run 'pnpm install'.");
            return 1;
        }

        if (process == null)
        {
            Console.WriteLine("[ERROR] Could not start 'pnpm exec tsc'.");
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
                Console.WriteLine("[ERROR] The web API clients don't typecheck:");
                Console.WriteLine(stdout);
                Console.WriteLine(stderr);
                return 1;
            }
        }

        Console.WriteLine("✔ Web API clients typecheck against their OpenAPI types.");
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
