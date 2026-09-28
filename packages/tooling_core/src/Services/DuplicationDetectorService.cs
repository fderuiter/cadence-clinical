using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.ToolingCore.Services;

public static class DuplicationDetectorService
{
    private const int WindowSize = 15;

    public static int DetectDuplication(string[]? files = null, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Scanning codebase for duplicated code blocks (>15 lines)...");

        IEnumerable<string> filePaths;
        if (files != null && files.Length > 0)
        {
            filePaths = files.Select(f => Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f))
                             .Where(File.Exists);
        }
        else
        {
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".py", ".js", ".vue", ".css", ".ts" };
            filePaths = Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories)
                .Where(f => exts.Contains(Path.GetExtension(f)))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}"));
        }

        var hashes = new Dictionary<string, List<(string File, int StartLine)>>();
        int duplicateCount = 0;

        foreach (var file in filePaths)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch
            {
                continue;
            }

            var normalized = lines.Select(NormalizeLine).ToList();
            if (normalized.Count < WindowSize) continue;

            for (int i = 0; i <= normalized.Count - WindowSize; i++)
            {
                var block = normalized.Skip(i).Take(WindowSize).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (block.Count < WindowSize / 2) continue;

                string blockText = string.Join("\n", block);
                string hash = ComputeHash(blockText);

                if (!hashes.TryGetValue(hash, out var value))
                {
                    value = [];
                    hashes[hash] = value;
                }

                string relPath = Path.GetRelativePath(repoRoot, file);
                value.Add((relPath, i + 1));
            }
        }

        foreach (var list in hashes.Values)
        {
            if (list.Count > 1)
            {
                var distinctFiles = list.Select(v => v.File).Distinct().ToList();
                if (distinctFiles.Count > 1)
                {
                    duplicateCount++;
                    Console.WriteLine($"[DUPLICATION] Duplicate {WindowSize}-line block found across:");
                    foreach (var loc in list)
                    {
                        Console.WriteLine($"  - {loc.File}:{loc.StartLine}");
                    }
                }
            }
        }

        if (duplicateCount > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {duplicateCount} duplicated code blocks exceeding threshold.");
            return 1;
        }

        Console.WriteLine("✔ Code duplication check passed cleanly.");
        return 0;
    }

    private static string NormalizeLine(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.StartsWith('#') ||
            trimmed.StartsWith("using ", StringComparison.Ordinal) ||
            trimmed.StartsWith("import ", StringComparison.Ordinal) ||
            trimmed.StartsWith("namespace ", StringComparison.Ordinal))
        {
            return string.Empty;
        }
        return trimmed;
    }

    private static string ComputeHash(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
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
