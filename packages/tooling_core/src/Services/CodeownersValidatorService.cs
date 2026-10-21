using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static class CodeownersValidatorService
{
    private static readonly Regex GitHubHandleRegex = new(@"^@[a-zA-Z0-9_.-]+(/[\w.-]+)?$", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", RegexOptions.Compiled);

    public static int ValidateCodeowners(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating .github/CODEOWNERS file presence and configuration...");

        string codeownersPath = Path.Combine(repoRoot, ".github", "CODEOWNERS");
        if (!File.Exists(codeownersPath))
        {
            Console.WriteLine("[ERROR] .github/CODEOWNERS file is missing!");
            return 1;
        }

        string content = File.ReadAllText(codeownersPath);
        if (string.IsNullOrWhiteSpace(content))
        {
            Console.WriteLine("[ERROR] .github/CODEOWNERS file is empty!");
            return 1;
        }

        string[] lines = File.ReadAllLines(codeownersPath);
        List<string> workspacePaths = GetWorkspaceRelativePaths(repoRoot);
        bool isValid = true;

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNumber = i + 1;
            string rawLine = lines[i];

            List<string> tokens = TokenizeLine(rawLine);
            if (tokens.Count == 0)
            {
                continue;
            }

            string pattern = tokens[0];
            List<string> owners = tokens.Skip(1).ToList();

            if (owners.Count == 0)
            {
                Console.WriteLine($"[ERROR] .github/CODEOWNERS: Line {lineNumber}: Path pattern '{pattern}' has no owner handles specified.");
                isValid = false;
            }

            if (!MatchesAnyPath(pattern, workspacePaths, repoRoot))
            {
                Console.WriteLine($"[ERROR] .github/CODEOWNERS: Line {lineNumber}: Path pattern '{pattern}' does not match any file or directory.");
                isValid = false;
            }

            foreach (string owner in owners)
            {
                if (!IsValidOwnerHandle(owner))
                {
                    Console.WriteLine($"[ERROR] .github/CODEOWNERS: Line {lineNumber}: Invalid owner handle '{owner}'. Handles must start with '@' (e.g. @username, @org/team-name) or be a valid email address.");
                    isValid = false;
                }
            }
        }

        if (!isValid)
        {
            return 1;
        }

        Console.WriteLine("✔ .github/CODEOWNERS validated cleanly.");
        return 0;
    }

    private static List<string> TokenizeLine(string line)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool escaped = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                sb.Append(c);
                escaped = true;
                continue;
            }

            if (c == ' ' || c == '\t')
            {
                if (sb.Length > 0)
                {
                    string token = sb.ToString();
                    if (token.StartsWith("#", StringComparison.Ordinal))
                    {
                        sb.Clear();
                        break;
                    }

                    tokens.Add(token);
                    sb.Clear();
                }

                continue;
            }

            sb.Append(c);
        }

        if (sb.Length > 0)
        {
            string token = sb.ToString();
            if (!token.StartsWith("#", StringComparison.Ordinal))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    private static bool MatchesAnyPath(string pattern, List<string> workspacePaths, string repoRoot)
    {
        string unescapedPattern = pattern.Replace(@"\ ", " ", StringComparison.Ordinal);

        if (!unescapedPattern.Contains('*') && !unescapedPattern.Contains('?'))
        {
            string relPath = unescapedPattern.TrimStart('/');
            if (string.IsNullOrEmpty(relPath))
            {
                return true;
            }

            string target = relPath.TrimEnd('/');
            string fullPath = Path.Combine(repoRoot, target);
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                return true;
            }

            if (!pattern.StartsWith('/'))
            {
                return workspacePaths.Any(path =>
                    path.Equals(target, StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("/" + target, StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("/" + target + "/", StringComparison.OrdinalIgnoreCase));
            }

            return false;
        }

        Regex regex = ConvertGlobToRegex(pattern);
        return workspacePaths.Any(path => regex.IsMatch(path));
    }

    private static Regex ConvertGlobToRegex(string pattern)
    {
        string unescaped = pattern.Replace(@"\ ", " ", StringComparison.Ordinal);
        bool isAnchored = unescaped.StartsWith('/');
        bool isDirectoryOnly = unescaped.EndsWith('/');

        string trimmed = unescaped;
        if (isAnchored)
        {
            trimmed = trimmed.Substring(1);
        }

        if (isDirectoryOnly && trimmed.Length > 0)
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1);
        }

        var sb = new StringBuilder();
        if (isAnchored)
        {
            sb.Append('^');
        }
        else
        {
            sb.Append("(?:^|/)");
        }

        int i = 0;
        while (i < trimmed.Length)
        {
            if (trimmed[i] == '*' && i + 1 < trimmed.Length && trimmed[i + 1] == '*')
            {
                sb.Append(".*");
                i += 2;
                if (i < trimmed.Length && trimmed[i] == '/')
                {
                    i++;
                }
            }
            else if (trimmed[i] == '*')
            {
                sb.Append("[^/]*");
                i++;
            }
            else if (trimmed[i] == '?')
            {
                sb.Append("[^/]");
                i++;
            }
            else if (".+{}()[]^$|\\".Contains(trimmed[i]))
            {
                sb.Append('\\').Append(trimmed[i]);
                i++;
            }
            else
            {
                sb.Append(trimmed[i]);
                i++;
            }
        }

        sb.Append("(?:/.*)?$");

        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static bool IsValidOwnerHandle(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return false;
        }

        if (owner.StartsWith('@'))
        {
            return GitHubHandleRegex.IsMatch(owner);
        }

        return EmailRegex.IsMatch(owner);
    }

    private static List<string> GetWorkspaceRelativePaths(string repoRoot)
    {
        var relativePaths = new List<string>();
        if (!Directory.Exists(repoRoot))
        {
            return relativePaths;
        }

        var stack = new Stack<string>();
        stack.Push(repoRoot);

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                {
                    string name = Path.GetFileName(entry);
                    if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string relPath = Path.GetRelativePath(repoRoot, entry).Replace('\\', '/');
                    relativePaths.Add(relPath);

                    if (Directory.Exists(entry))
                    {
                        stack.Push(entry);
                    }
                }
            }
            catch
            {
                // Ignore unreadable entries
            }
        }

        return relativePaths;
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current)
            {
                break;
            }

            current = parent ?? "";
        }

        return Directory.GetCurrentDirectory();
    }
}
