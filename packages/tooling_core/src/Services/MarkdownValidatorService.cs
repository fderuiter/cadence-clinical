using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Markdown;

namespace Cadence.ToolingCore.Services;

public static class MarkdownValidatorService
{
    private static readonly Regex MarkdownLinkRegex = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);

    public static int ValidateMarkdown(string[]? files = null, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating Markdown documentation formatting, workspace links, sidebar reachability, and index coverage...");

        string[] excludedDirs = [
            $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.agents{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.system_generated{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.tmp{Path.DirectorySeparatorChar}"
        ];

        string[] mdFiles;
        if (files != null && files.Length > 0)
        {
            mdFiles = files.Select(f => Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f)).Where(File.Exists).ToArray();
        }
        else
        {
            mdFiles = Directory.EnumerateFiles(repoRoot, "*.md", SearchOption.AllDirectories)
                .Where(f => !excludedDirs.Any(d => f.Contains(d)))
                .ToArray();
        }

        int issuesCount = 0;
        string docsDir = Path.Combine(repoRoot, "docs");

        // Rule 1: Workspace Link Validation via MarkdownParser
        var linkIssues = MarkdownParser.ValidateWorkspaceLinks(repoRoot, mdFiles);
        foreach (var issue in linkIssues)
        {
            Console.WriteLine($"[MARKDOWN LINK] {issue.Message}");
            issuesCount++;
        }

        // Rule 2: Paths into .NET test project folders must exist
        foreach (var issue in MarkdownParser.ValidateTestProjectPaths(repoRoot, mdFiles))
        {
            Console.WriteLine($"[MARKDOWN TEST PATH] {issue.Message}");
            issuesCount++;
        }

        // Rule 3: Heading space validation
        foreach (var file in mdFiles)
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); } catch (Exception ex) { _ = ex; continue; }

            bool inFencedBlock = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    inFencedBlock = !inFencedBlock;
                    continue;
                }

                if (inFencedBlock)
                {
                    continue;
                }

                if (trimmed.StartsWith('#'))
                {
                    int hashCount = 0;
                    while (hashCount < trimmed.Length && trimmed[hashCount] == '#')
                    {
                        hashCount++;
                    }

                    if (hashCount >= 1 && hashCount <= 6 && hashCount < trimmed.Length)
                    {
                        char nextChar = trimmed[hashCount];
                        if (nextChar != ' ' && nextChar != '\t' && nextChar != '#' && !char.IsDigit(nextChar))
                        {
                            string relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                            Console.WriteLine($"[MARKDOWN FORMAT] {relPath}:{i + 1} - Heading missing space after '#': '{line.Trim()}'");
                            issuesCount++;
                        }
                    }
                }
            }
        }

        // Rule 4 & 5: VitePress Sidebar Reachability and Index Coverage for non-execution docs
        if (Directory.Exists(docsDir))
        {
            string[] nonExecutionDocs = Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}SDLC{Path.DirectorySeparatorChar}runs{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}runs{Path.DirectorySeparatorChar}"))
                .ToArray();

            HashSet<string> sidebarLinks = GetVitePressReachableLinks(repoRoot);
            HashSet<string> indexLinks = GetDocIndexLinks(docsDir);

            foreach (string docFile in nonExecutionDocs)
            {
                string relToDocs = Path.GetRelativePath(docsDir, docFile).Replace('\\', '/');
                string linkKey = relToDocs.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                    ? relToDocs[..^3]
                    : relToDocs;

                if (linkKey.Equals("index", StringComparison.OrdinalIgnoreCase) || linkKey.Equals("README", StringComparison.OrdinalIgnoreCase))
                {
                    linkKey = "";
                }

                // Rule 4: Sidebar reachability check
                if (!IsExcludedFromSidebar(docFile))
                {
                    bool isReachable = sidebarLinks.Contains(linkKey) ||
                                       sidebarLinks.Contains("/" + linkKey) ||
                                       sidebarLinks.Contains(relToDocs) ||
                                       (linkKey == "" && (sidebarLinks.Contains("/") || sidebarLinks.Contains("")));

                    if (!isReachable)
                    {
                        Console.WriteLine($"[MARKDOWN SIDEBAR] docs/{relToDocs}: File is not reachable in VitePress sidebar navigation config.");
                        issuesCount++;
                    }
                }

                // Rule 5: Documentation Index coverage check
                bool isIndexed = indexLinks.Contains(relToDocs) ||
                                 indexLinks.Contains("docs/" + relToDocs) ||
                                 indexLinks.Contains("../" + relToDocs) ||
                                 indexLinks.Contains("../docs/" + relToDocs) ||
                                 (relToDocs.Equals("DOCUMENTATION_INDEX.md", StringComparison.OrdinalIgnoreCase) && indexLinks.Contains("DOCUMENTATION_INDEX.md")) ||
                                 ((relToDocs.Equals("index.md", StringComparison.OrdinalIgnoreCase) || relToDocs.Equals("README.md", StringComparison.OrdinalIgnoreCase)) &&
                                  (indexLinks.Contains("README.md") || indexLinks.Contains("../README.md") || indexLinks.Contains("index.md")));

                if (!isIndexed)
                {
                    Console.WriteLine($"[MARKDOWN INDEX] docs/{relToDocs}: File is not listed in docs/DOCUMENTATION_INDEX.md.");
                    issuesCount++;
                }
            }
        }

        if (issuesCount > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {issuesCount} markdown issues.");
            return 1;
        }

        Console.WriteLine("✔ Markdown files, workspace links, VitePress sidebar reachability, and index coverage validated cleanly.");
        return 0;
    }

    private static bool IsExcludedFromSidebar(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                string content = File.ReadAllText(filePath);
                return Regex.IsMatch(content, @"exclude_from_sidebar:\s*true", RegexOptions.IgnoreCase);
            }
        }
        catch (Exception ex)
        {
            _ = ex;
        }
        return false;
    }

    private static HashSet<string> GetVitePressReachableLinks(string repoRoot)
    {
        HashSet<string> links = new(StringComparer.OrdinalIgnoreCase);
        string configPath = Path.Combine(repoRoot, "docs", ".vitepress", "config.mjs");
        if (!File.Exists(configPath))
        {
            return links;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = FindNodeExecutable(),
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--input-type=module");
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add("import config from './docs/.vitepress/config.mjs'; const s = config.themeConfig.sidebar; const n = config.themeConfig.nav; const links = []; function c(items) { if (!items) return; if (Array.isArray(items)) items.forEach(c); else { if (items.link) links.push(items.link); if (items.items) c(items.items); } } c(s); c(n); console.log(JSON.stringify(links));");

            string nodeModules = Path.Combine(repoRoot, "node_modules");
            psi.EnvironmentVariables["NODE_PATH"] = nodeModules;

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10000);
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    using var doc = JsonDocument.Parse(output.Trim());
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        string? rawLink = element.GetString();
                        if (string.IsNullOrEmpty(rawLink)) continue;

                        string clean = rawLink.Split('#')[0].Split('?')[0];
                        if (clean.StartsWith("http://") || clean.StartsWith("https://") || clean.StartsWith("mailto:")) continue;
                        if (clean.StartsWith('/')) clean = clean[1..];
                        if (clean.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) clean = clean[..^3];
                        if (clean.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) clean = clean[..^5];
                        links.Add(clean);
                    }
                    return links;
                }
            }
        }
        catch (Exception ex)
        {
            _ = ex;
        }

        return links;
    }

    private static HashSet<string> GetDocIndexLinks(string docsDir)
    {
        HashSet<string> links = new(StringComparer.OrdinalIgnoreCase);
        string indexPath = Path.Combine(docsDir, "DOCUMENTATION_INDEX.md");
        if (!File.Exists(indexPath))
        {
            return links;
        }

        try
        {
            string content = File.ReadAllText(indexPath);
            foreach (Match match in MarkdownLinkRegex.Matches(content))
            {
                string rawLink = match.Groups[2].Value.Trim().Split('#')[0].Split('?')[0];
                if (string.IsNullOrWhiteSpace(rawLink) || rawLink.StartsWith("http://") || rawLink.StartsWith("https://") || rawLink.StartsWith("mailto:"))
                {
                    continue;
                }

                string normalized = rawLink;
                if (normalized.StartsWith("../docs/"))
                {
                    normalized = normalized["../docs/".Length..];
                }
                else if (normalized.StartsWith("../"))
                {
                    normalized = normalized["../".Length..];
                }
                else if (normalized.StartsWith("./"))
                {
                    normalized = normalized["./".Length..];
                }

                links.Add(normalized);
            }
        }
        catch (Exception ex)
        {
            _ = ex;
        }

        return links;
    }

    private static string FindNodeExecutable()
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string pathDir in pathEnv.Split(Path.PathSeparator))
            {
                string candidate = Path.Combine(pathDir, "node");
                if (File.Exists(candidate)) return candidate;
            }
        }
        if (File.Exists("/home/jules/.nvm/versions/node/v22.22.3/bin/node"))
        {
            return "/home/jules/.nvm/versions/node/v22.22.3/bin/node";
        }
        return "node";
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
