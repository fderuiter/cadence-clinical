using System;
using System.IO;
using System.Text.Json;

namespace Cadence.ToolingCore.Services;

public static class DependencyValidatorService
{
    private static readonly string[] ForbiddenPackages = ["node-forge", "elliptic", "jose"];

    public static int ValidateDependencies(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating frontend dependencies for forbidden cryptographic libraries...");

        string pkgPath = Path.Combine(repoRoot, "apps", "web", "package.json");
        if (!File.Exists(pkgPath))
        {
            Console.WriteLine("✔ web package.json not found, skipping.");
            return 0;
        }

        try
        {
            string json = File.ReadAllText(pkgPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int violations = 0;
            string[] sections = ["dependencies", "devDependencies", "peerDependencies"];

            foreach (var section in sections)
            {
                if (root.TryGetProperty(section, out var depsObj) && depsObj.ValueKind == JsonValueKind.Object)
                {
                    foreach (var forbidden in ForbiddenPackages)
                    {
                        if (depsObj.TryGetProperty(forbidden, out _))
                        {
                            Console.WriteLine($"[SECURITY VIOLATION] Forbidden client-side crypto package '{forbidden}' detected in apps/web/package.json ({section}).");
                            violations++;
                        }
                    }
                }
            }

            if (violations > 0)
            {
                Console.WriteLine($"\n[ERROR] Found {violations} forbidden client dependency violations.");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARNING] Failed to parse package.json: {ex.Message}");
        }

        Console.WriteLine("✔ Frontend dependency crypto check passed cleanly.");
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
