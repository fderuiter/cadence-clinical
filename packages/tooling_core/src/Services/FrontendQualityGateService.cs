using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Fail-closed Prettier and ESLint quality gates for <c>cadence check</c> and <c>cadence check --pre-commit</c>.
/// </summary>
public static class FrontendQualityGateService
{
    public const string AccessibilityDisablesBaselinePath = "ArchitectureBaselines/eslint-a11y-disables.baseline.txt";
    public const string UpdateBaselineEnvVar = "CADENCE_UPDATE_BASELINE";

    private const int MaxFilesPerBatch = 400;

    private static readonly Regex HtmlEslintDisableRegex = new(
        @"<!--\s*eslint-disable(?:-next-line|-line)?\b([\s\S]*?)-->",
        RegexOptions.Compiled);

    private static readonly Regex BlockEslintDisableRegex = new(
        @"/\*\s*eslint-disable(?:-next-line|-line)?\b([\s\S]*?)\*/",
        RegexOptions.Compiled);

    private static readonly Regex LineEslintDisableRegex = new(
        @"//\s*eslint-disable(?:-next-line|-line)?\b([^\r\n]*)",
        RegexOptions.Compiled);

    private static readonly HashSet<string> PrettierExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".vue", ".json", ".css", ".md", ".yaml", ".yml",
    };

    private static readonly HashSet<string> EslintExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".vue",
    };

    public static int CheckPrettier(
        string? repoRoot = null,
        string[]? targetFiles = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null) =>
        RunPrettier(repoRoot, targetFiles, preCommit, write: false, indexReader);

    public static int FormatPrettier(string? repoRoot = null, string[]? targetFiles = null) =>
        RunPrettier(repoRoot, targetFiles, preCommit: false, write: true, indexReader: null);

    private static int RunPrettier(
        string? repoRoot,
        string[]? targetFiles,
        bool preCommit,
        bool write,
        GitIndexReader? indexReader)
    {
        if (targetFiles is { Length: 0 })
        {
            return 0;
        }

        repoRoot ??= FindRepoRoot();

        string? pnpm = ResolveUsablePnpm(repoRoot);
        if (pnpm is null)
        {
            return 1;
        }

        if (!EnsureNodeModules(repoRoot))
        {
            return 1;
        }

        GitIndexReader? ownedReader = null;
        GitIndexReader? reader = !write
            ? GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out ownedReader)
            : null;
        using (ownedReader)
        {
            IEnumerable<string> rawFiles;
            if (preCommit)
            {
                rawFiles = targetFiles is not null
                    ? targetFiles
                    : PreCommitOpenApiService.GetStagedFiles(repoRoot);
            }
            else if (targetFiles is not null)
            {
                rawFiles = targetFiles;
            }
            else
            {
                rawFiles = PreCommitOpenApiService.TryGetTrackedFiles(repoRoot)
                    ?? EnumerateRepositoryFiles(repoRoot);
            }

            var matching = SelectExistingFiles(repoRoot, rawFiles, PrettierExtensions, reader);
            if (matching.Count == 0)
            {
                return 0;
            }

            string ignorePath = Path.Combine(repoRoot, ".prettierignore");
            bool hasIgnoreFile = File.Exists(ignorePath);

            if (!write && reader is { HasAuthoritativeIndex: true })
            {
                foreach (string rel in matching)
                {
                    if (!reader.TryReadBytes(rel, out byte[] blobBytes))
                    {
                        continue;
                    }

                    var stdinArgs = new List<string> { "exec", "prettier", "--check" };
                    if (hasIgnoreFile)
                    {
                        stdinArgs.Add("--ignore-path");
                        stdinArgs.Add(".prettierignore");
                    }

                    stdinArgs.Add("--stdin-filepath");
                    stdinArgs.Add(rel);

                    if (RunProcessWithStdin(repoRoot, pnpm, stdinArgs, blobBytes, suppressStdoutOnSuccess: true) != 0)
                    {
                        Console.WriteLine($"[ERROR] Prettier formatting violations detected in staged '{rel}'. Run 'pnpm cadence fix' or 'pnpm format' to auto-fix.");
                        return 1;
                    }
                }

                return 0;
            }

            string modeFlag = write ? "--write" : "--check";

            foreach (string[] batch in matching.Chunk(MaxFilesPerBatch))
            {
                var args = new List<string> { "exec", "prettier", modeFlag };
                if (hasIgnoreFile)
                {
                    args.Add("--ignore-path");
                    args.Add(".prettierignore");
                }

                args.Add("--");
                args.AddRange(batch);

                if (RunProcess(repoRoot, pnpm, args) != 0)
                {
                    Console.WriteLine(write
                        ? "[ERROR] Prettier auto-formatting failed."
                        : "[ERROR] Prettier formatting violations detected. Run 'pnpm cadence fix' or 'pnpm format' to auto-fix.");
                    return 1;
                }
            }

            return 0;
        }
    }

    public static int CheckEslint(
        string? repoRoot = null,
        string[]? targetFiles = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        if (targetFiles is { Length: 0 })
        {
            return 0;
        }

        repoRoot ??= FindRepoRoot();

        string? pnpm = ResolveUsablePnpm(repoRoot);
        if (pnpm is null)
        {
            return 1;
        }

        if (!EnsureNodeModules(repoRoot))
        {
            return 1;
        }

        if (preCommit || targetFiles is not null)
        {
            var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
            using (ownedReader)
            {
                var rawFiles = preCommit && targetFiles is null
                    ? PreCommitOpenApiService.GetStagedFiles(repoRoot)
                    : (IReadOnlyList<string>)targetFiles!;

                var matching = SelectExistingFiles(repoRoot, rawFiles, EslintExtensions, reader);
                if (matching.Count == 0)
                {
                    return 0;
                }

                string duplicateCheckScript = Path.Combine(repoRoot, "scripts", "check_duplicate_identifiers.js");
                if (File.Exists(duplicateCheckScript) &&
                    matching.Any(f => f.Equals("apps/web/src/stores/clinical.ts", StringComparison.OrdinalIgnoreCase)))
                {
                    if (reader is { HasAuthoritativeIndex: true })
                    {
                        using var storeWorkspace = reader.Materialize(["apps/web/src/stores/clinical.ts"]);
                        string tempStorePath = Path.Combine(storeWorkspace.RootPath, "apps", "web", "src", "stores", "clinical.ts");
                        if (File.Exists(tempStorePath) &&
                            RunProcess(Path.Combine(repoRoot, "apps", "web"), "node", [duplicateCheckScript, tempStorePath]) != 0)
                        {
                            return 1;
                        }
                    }
                    else if (RunProcess(Path.Combine(repoRoot, "apps", "web"), "node", [duplicateCheckScript]) != 0)
                    {
                        return 1;
                    }
                }

                if (reader is { HasAuthoritativeIndex: true })
                {
                    foreach (string rel in matching)
                    {
                        if (!reader.TryReadBytes(rel, out byte[] blobBytes))
                        {
                            continue;
                        }

                        var stdinArgs = new List<string>
                        {
                            "exec",
                            "eslint",
                            "--no-warn-ignored",
                            "--stdin",
                            "--stdin-filename",
                            rel,
                        };

                        if (RunProcessWithStdin(repoRoot, pnpm, stdinArgs, blobBytes, suppressStdoutOnSuccess: false) != 0)
                        {
                            Console.WriteLine($"[ERROR] ESLint violations detected in staged '{rel}'. Fix the findings above or run 'pnpm -r lint'.");
                            return 1;
                        }
                    }

                    return ValidateAccessibilityDisablesIfConfigured(repoRoot, matching, reader);
                }

                foreach (string[] batch in matching.Chunk(MaxFilesPerBatch))
                {
                    var args = new List<string> { "exec", "eslint", "--no-warn-ignored", "--" };
                    args.AddRange(batch);
                    if (RunProcess(repoRoot, pnpm, args) != 0)
                    {
                        Console.WriteLine("[ERROR] ESLint violations detected in staged files. Fix the findings above or run 'pnpm -r lint'.");
                        return 1;
                    }
                }

                return ValidateAccessibilityDisablesIfConfigured(repoRoot, matching, reader);
            }
        }

        if (RunProcess(repoRoot, pnpm, ["-r", "lint"]) != 0)
        {
            Console.WriteLine("[ERROR] Workspace ESLint check ('pnpm -r lint') failed.");
            return 1;
        }

        return ValidateAccessibilityDisablesBaseline(repoRoot);
    }

    private static string? ResolveUsablePnpm(string repoRoot)
    {
        string[] candidates = OperatingSystem.IsWindows() ? ["pnpm.cmd", "pnpm"] : ["pnpm"];
        foreach (string candidate in candidates)
        {
            var psi = new ProcessStartInfo(candidate)
            {
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("--version");

            try
            {
                using var process = PosixProcessLauncher.Start(psi);
                if (process is null)
                {
                    continue;
                }

                var stderrTask = process.StandardError.ReadToEndAsync();
                _ = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                _ = stderrTask.GetAwaiter().GetResult();

                if (process.ExitCode == 0)
                {
                    return candidate;
                }

                Console.WriteLine("[ERROR] pnpm is installed on PATH but exited non-zero. Run 'corepack enable' so pnpm matches the packageManager pin in package.json.");
                return null;
            }
            catch (Win32Exception)
            {
                // Try next candidate.
            }
        }

        Console.WriteLine("[ERROR] 'pnpm' is required for frontend quality gates but was not found on PATH. Install Node.js and run 'corepack enable'.");
        return null;
    }

    private static bool EnsureNodeModules(string repoRoot)
    {
        if (Directory.Exists(Path.Combine(repoRoot, "node_modules")))
        {
            return true;
        }

        Console.WriteLine($"[ERROR] Frontend workspace dependencies ('node_modules') are missing in '{repoRoot}'. Run 'pnpm install --frozen-lockfile' first.");
        return false;
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string repoRoot)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        return Directory.EnumerateFiles(repoRoot, "*", options)
            .Where(fullPath =>
            {
                string rel = Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
                return !rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.StartsWith("node_modules/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.StartsWith(".venv/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.Contains("/.venv/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.StartsWith(".cadence/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.StartsWith("dist/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.Contains("/dist/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
            });
    }

    private static List<string> SelectExistingFiles(
        string repoRoot,
        IEnumerable<string> candidates,
        HashSet<string> extensions,
        GitIndexReader? reader)
    {
        var selected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string raw in candidates)
        {
            string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, raw);
            if (string.IsNullOrEmpty(rel))
            {
                continue;
            }

            if (reader is not null)
            {
                if (!reader.FileExists(rel))
                {
                    continue;
                }
            }
            else
            {
                string fullPath = Path.Combine(repoRoot, rel);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                try
                {
                    if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                }
                catch (IOException)
                {
                    continue;
                }
            }

            string ext = Path.GetExtension(rel);
            if (!extensions.Contains(ext))
            {
                continue;
            }

            selected.Add(rel);
        }

        return [.. selected];
    }

    private static int RunProcessWithStdin(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        byte[] stdinBytes,
        bool suppressStdoutOnSuccess)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = PosixProcessLauncher.Start(psi);
            if (process is null)
            {
                return 1;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            try
            {
                process.StandardInput.BaseStream.Write(stdinBytes, 0, stdinBytes.Length);
                process.StandardInput.BaseStream.Flush();
            }
            catch (IOException)
            {
                // Subprocess exited before consuming all stdin bytes.
            }
            finally
            {
                process.StandardInput.Close();
            }

            process.WaitForExit();
            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();

            if ((process.ExitCode != 0 || !suppressStdoutOnSuccess) && !string.IsNullOrWhiteSpace(stdout))
            {
                Console.WriteLine(stdout.TrimEnd());
            }

            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
            {
                Console.WriteLine(stderr.TrimEnd());
            }

            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            Console.WriteLine($"[ERROR] Could not start '{fileName}': {ex.Message}");
            return 1;
        }
    }

    internal static int RunProcess(string workingDirectory, string fileName, IReadOnlyList<string> arguments, bool suppressOutput = false)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = suppressOutput,
            RedirectStandardError = suppressOutput,
        };
        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = PosixProcessLauncher.Start(psi);
            if (process is null)
            {
                return 1;
            }

            if (suppressOutput)
            {
                var stderrTask = process.StandardError.ReadToEndAsync();
                _ = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                _ = stderrTask.GetAwaiter().GetResult();
            }
            else
            {
                process.WaitForExit();
            }

            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            Console.WriteLine($"[ERROR] Could not start '{fileName}': {ex.Message}");
            return 1;
        }
    }

    private static int ValidateAccessibilityDisablesIfConfigured(
        string repoRoot,
        IReadOnlyList<string> scopedFiles,
        GitIndexReader? reader)
    {
        bool baselineExists = reader is { HasAuthoritativeIndex: true }
            ? reader.FileExists(AccessibilityDisablesBaselinePath) ||
              File.Exists(Path.Combine(repoRoot, AccessibilityDisablesBaselinePath))
            : File.Exists(Path.Combine(repoRoot, AccessibilityDisablesBaselinePath));
        if (!baselineExists)
        {
            return 0;
        }

        return ValidateAccessibilityDisablesBaseline(repoRoot, scopedFiles, reader);
    }

    public static int ValidateAccessibilityDisablesBaseline(
        string repoRoot,
        IReadOnlyList<string>? scopedFiles = null,
        GitIndexReader? reader = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        bool updateBaseline = scopedFiles is null &&
                              string.Equals(getEnvironmentVariable(UpdateBaselineEnvVar), "1", StringComparison.Ordinal);

        IReadOnlyList<string> candidateFiles;
        if (scopedFiles is not null)
        {
            candidateFiles = scopedFiles
                .Select(f => GitIndexReader.TryNormalizeRelativePath(repoRoot, f))
                .Where(f => !string.IsNullOrEmpty(f) && IsAccessibilityScopedFile(f!))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();
            if (candidateFiles.Count == 0)
            {
                return 0;
            }
        }
        else
        {
            var rawFiles = PreCommitOpenApiService.TryGetTrackedFiles(repoRoot)
                           ?? EnumerateRepositoryFiles(repoRoot)
                               .Select(full => Path.GetRelativePath(repoRoot, full).Replace('\\', '/'));
            candidateFiles = rawFiles
                .Select(f => f.Replace('\\', '/'))
                .Where(IsAccessibilityScopedFile)
                .Where(rel => File.Exists(Path.Combine(repoRoot, rel)))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();
        }

        var actualEntries = new List<string>();
        var zeroToleranceViolations = new List<string>();
        foreach (string rel in candidateFiles)
        {
            string? source = TryReadSourceText(repoRoot, rel, reader);
            if (source is null)
            {
                continue;
            }

            foreach (string rule in ExtractAccessibilityDisableRules(source))
            {
                string entry = $"{rel}|{rule}";
                if (IsZeroToleranceAccessibilityPath(rel))
                {
                    zeroToleranceViolations.Add(entry);
                }

                actualEntries.Add(entry);
            }
        }

        actualEntries.Sort(StringComparer.Ordinal);

        if (zeroToleranceViolations.Count > 0)
        {
            Console.WriteLine("[ERROR] vuejs-accessibility ESLint suppressions are prohibited in shared UI and subject-portal packages:");
            foreach (string violation in zeroToleranceViolations.Take(20))
            {
                Console.WriteLine($"  + {violation}");
            }

            return 1;
        }

        string baselineFullPath = Path.Combine(repoRoot, AccessibilityDisablesBaselinePath);
        if (updateBaseline)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselineFullPath)!);
            var sb = new StringBuilder();
            sb.AppendLine("# Ratcheted vuejs-accessibility ESLint suppressions (<path>|<rule>, one line per occurrence).");
            sb.AppendLine("# Shrink-only: live routes, packages/ui, and apps/subject-portal must retain 0 suppressions.");
            foreach (string entry in actualEntries)
            {
                sb.AppendLine(entry);
            }

            File.WriteAllText(baselineFullPath, sb.ToString());
            return 0;
        }

        string? baselineContent = TryReadSourceText(repoRoot, AccessibilityDisablesBaselinePath, reader);
        if (baselineContent is null)
        {
            Console.WriteLine($"[ERROR] Missing accessibility ESLint suppression baseline '{AccessibilityDisablesBaselinePath}'.");
            return 1;
        }

        var baselineEntries = ParseBaselineEntries(baselineContent);
        if (scopedFiles is not null)
        {
            var scopedSet = new HashSet<string>(candidateFiles, StringComparer.Ordinal);
            baselineEntries = baselineEntries
                .Where(entry =>
                {
                    int sep = entry.IndexOf('|');
                    return sep > 0 && scopedSet.Contains(entry[..sep]);
                })
                .ToList();
        }

        var unapprovedAdditions = MultisetDiff(baselineEntries, actualEntries);
        if (unapprovedAdditions.Count > 0)
        {
            Console.WriteLine($"[ERROR] Unapproved vuejs-accessibility ESLint suppression(s) detected relative to '{AccessibilityDisablesBaselinePath}':");
            foreach (string added in unapprovedAdditions.Take(20))
            {
                Console.WriteLine($"  + {added}");
            }

            Console.WriteLine("  Remediate the underlying accessibility violation instead of suppressing eslint-plugin-vuejs-accessibility.");
            return 1;
        }

        if (scopedFiles is null)
        {
            var staleEntries = MultisetDiff(actualEntries, baselineEntries);
            if (staleEntries.Count > 0)
            {
                Console.WriteLine($"[ERROR] Stale entries remain in '{AccessibilityDisablesBaselinePath}' after accessibility fixes (shrink the baseline):");
                foreach (string stale in staleEntries.Take(20))
                {
                    Console.WriteLine($"  - {stale}");
                }

                Console.WriteLine($"  Remove the resolved entries or run '{UpdateBaselineEnvVar}=1 pnpm cadence check eslint'.");
                return 1;
            }
        }

        return 0;
    }

    public static IReadOnlyList<string> ExtractAccessibilityDisableRules(string sourceText)
    {
        var rules = new List<string>();
        if (string.IsNullOrEmpty(sourceText) || !sourceText.Contains("eslint-disable", StringComparison.Ordinal))
        {
            return rules;
        }

        CollectRulesFromMatches(HtmlEslintDisableRegex.Matches(sourceText), rules);
        CollectRulesFromMatches(BlockEslintDisableRegex.Matches(sourceText), rules);
        CollectRulesFromMatches(LineEslintDisableRegex.Matches(sourceText), rules);
        return rules;
    }

    private static void CollectRulesFromMatches(MatchCollection matches, List<string> output)
    {
        foreach (Match match in matches)
        {
            string rawBody = match.Groups[1].Value;
            int justificationIndex = rawBody.IndexOf(" --", StringComparison.Ordinal);
            if (justificationIndex >= 0)
            {
                rawBody = rawBody[..justificationIndex];
            }

            string trimmed = rawBody.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                output.Add("vuejs-accessibility/*");
                continue;
            }

            foreach (string part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Equals("vuejs-accessibility", StringComparison.Ordinal) ||
                    part.StartsWith("vuejs-accessibility/", StringComparison.Ordinal))
                {
                    output.Add(part);
                }
            }
        }
    }

    private static bool IsAccessibilityScopedFile(string relPath)
    {
        if (!relPath.StartsWith("apps/", StringComparison.Ordinal) &&
            !relPath.StartsWith("packages/", StringComparison.Ordinal))
        {
            return false;
        }

        if (!EslintExtensions.Contains(Path.GetExtension(relPath)))
        {
            return false;
        }

        string fileName = Path.GetFileName(relPath);
        if (fileName.Equals("mockServiceWorker.js", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(".test.", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(".spec.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !relPath.Contains("/tests/", StringComparison.OrdinalIgnoreCase) &&
               !relPath.Contains("/__tests__/", StringComparison.OrdinalIgnoreCase) &&
               !relPath.Contains("/dist/", StringComparison.OrdinalIgnoreCase) &&
               !relPath.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) &&
               !relPath.Contains("/coverage/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsZeroToleranceAccessibilityPath(string relPath) =>
        relPath.StartsWith("packages/ui/", StringComparison.Ordinal) ||
        relPath.StartsWith("apps/subject-portal/", StringComparison.Ordinal);

    private static string? TryReadSourceText(string repoRoot, string relPath, GitIndexReader? reader)
    {
        if (reader is { HasAuthoritativeIndex: true } && reader.TryReadBytes(relPath, out byte[] blobBytes))
        {
            return Encoding.UTF8.GetString(blobBytes);
        }

        string fullPath = Path.Combine(repoRoot, relPath);
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
    }

    private static List<string> ParseBaselineEntries(string content) =>
        content
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

    private static List<string> MultisetDiff(IEnumerable<string> baseline, IEnumerable<string> target)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string item in baseline)
        {
            counts[item] = counts.GetValueOrDefault(item) + 1;
        }

        var diff = new List<string>();
        foreach (string item in target)
        {
            if (counts.TryGetValue(item, out int remaining) && remaining > 0)
            {
                counts[item] = remaining - 1;
            }
            else
            {
                diff.Add(item);
            }
        }

        diff.Sort(StringComparer.Ordinal);
        return diff;
    }

    public static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();
}
