using System;
using System.IO;

using Cadence.ToolingCore.Contracts;

namespace Cadence.ToolingCore.Services;

public static class ContractVerifierService
{
    public static int VerifyContracts(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Verifying backend hexagonal port definitions and contracts...");

        var result = ContractVerifier.VerifyBackendContracts(repoRoot);

        foreach (var err in result.Errors)
        {
            Console.WriteLine($"[ERROR] {err}");
        }

        if (!result.IsValid)
        {
            return 1;
        }

        Console.WriteLine("✔ Hexagonal contract static verification passed cleanly.");
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
