using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Fail-closed Prettier and ESLint quality gates for <c>cadence check</c> and <c>cadence check --pre-commit</c>.
/// </summary>
public static class FrontendQualityGateService
{
    public const string EslintWarningsBaselineFile = "eslint-warnings.baseline.txt";

    private const int MaxFilesPerBatch = 400;
    private const int PosixEslintBatchSize = 800;
    private const int WindowsEslintBatchSize = 250;

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

    public sealed record EslintFinding(
        string File,
        int Line,
        int Column,
        int Severity,
        string RuleId,
        string Message);

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

        bool fullScan = targetFiles is null && !preCommit;
        bool updateBaseline = fullScan &&
                              string.Equals(Environment.GetEnvironmentVariable("CADENCE_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
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
                rawFiles = GitRevisionScopeService.TryGetTrackedFiles(repoRoot)
                    ?? EnumerateRepositoryFiles(repoRoot);
            }

            var matching = SelectExistingFiles(repoRoot, rawFiles, EslintExtensions, reader)
                .Where(rel => !IsGeneratedEslintPath(rel))
                .ToList();
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

            var lineCache = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var errors = new List<EslintFinding>();
            var warnings = new List<(EslintFinding Finding, string Key)>();

            if (reader is { HasAuthoritativeIndex: true })
            {
                foreach (string rel in matching)
                {
                    if (!reader.TryReadBytes(rel, out byte[] blobBytes))
                    {
                        continue;
                    }

                    lineCache[rel] = Encoding.UTF8.GetString(blobBytes).Split('\n');
                    var stdinArgs = new List<string>
                    {
                        "exec",
                        "eslint",
                        "--no-warn-ignored",
                        "--format",
                        "json",
                        "--stdin",
                        "--stdin-filename",
                        rel,
                    };

                    var (exitCode, stdout, stderr) = RunProcessCapture(repoRoot, pnpm, stdinArgs, blobBytes);
                    if (!TryCollectEslintResults(repoRoot, stdout, stderr, exitCode, rel, reader, lineCache, errors, warnings))
                    {
                        return 1;
                    }
                }
            }
            else
            {
                int batchSize = OperatingSystem.IsWindows() ? WindowsEslintBatchSize : PosixEslintBatchSize;
                foreach (string[] batch in matching.Chunk(batchSize))
                {
                    var args = new List<string> { "exec", "eslint", "--no-warn-ignored", "--format", "json", "--" };
                    args.AddRange(batch);

                    var (exitCode, stdout, stderr) = RunProcessCapture(repoRoot, pnpm, args, stdinBytes: null);
                    if (!TryCollectEslintResults(repoRoot, stdout, stderr, exitCode, fallbackRelativePath: null, reader, lineCache, errors, warnings))
                    {
                        return 1;
                    }
                }
            }

            foreach (var error in errors)
            {
                Console.WriteLine($"{error.File}:{error.Line}:{error.Column}: [ESLINT ERROR] ({error.RuleId}) {error.Message}");
            }

            if (errors.Count > 0)
            {
                Console.WriteLine($"[ERROR] Found {errors.Count} ESLint error(s). Fix all ESLint errors above.");
                return 1;
            }

            string baselinePath = Path.Combine(repoRoot, EslintWarningsBaselineFile);
            if (updateBaseline)
            {
                WriteEslintBaseline(baselinePath, warnings.Select(w => w.Key));
                Console.WriteLine($"✔ Wrote {warnings.Count} entries to {EslintWarningsBaselineFile} ({matching.Count} files linted).");
                return 0;
            }

            var allBaseline = ReadEslintBaseline(baselinePath, reader);
            List<string> scopedBaseline;
            if (fullScan)
            {
                scopedBaseline = allBaseline;
            }
            else
            {
                var scannedSet = matching.ToHashSet(StringComparer.Ordinal);
                scopedBaseline = allBaseline.Where(entry =>
                {
                    int pipeIdx = entry.IndexOf('|', StringComparison.Ordinal);
                    return pipeIdx > 0 && scannedSet.Contains(entry[..pipeIdx]);
                }).ToList();
            }

            var (newWarnings, staleEntries) = CompareEslintWarningsWithBaseline(warnings, scopedBaseline);

            foreach (var warning in newWarnings)
            {
                Console.WriteLine($"{warning.File}:{warning.Line}:{warning.Column}: [ESLINT WARNING] ({warning.RuleId}) {warning.Message}");
            }

            foreach (string stale in staleEntries.Take(25))
            {
                Console.WriteLine($"[ESLINT STALE BASELINE] {stale}");
            }

            if (staleEntries.Count > 25)
            {
                Console.WriteLine($"[ESLINT STALE BASELINE] ... and {staleEntries.Count - 25} more stale entries.");
            }

            if (newWarnings.Count > 0 || staleEntries.Count > 0)
            {
                if (newWarnings.Count > 0)
                {
                    Console.WriteLine($"[ERROR] Found {newWarnings.Count} unbaselined ESLint warning(s). Fix the warnings above ({EslintWarningsBaselineFile} may only shrink).");
                }

                if (staleEntries.Count > 0)
                {
                    Console.WriteLine($"[ERROR] {staleEntries.Count} {EslintWarningsBaselineFile} entr(y/ies) no longer match; the baseline may only shrink. Run 'CADENCE_UPDATE_BASELINE=1 pnpm cadence check eslint' to drop them.");
                }

                return 1;
            }

            Console.WriteLine($"✔ ESLint check passed cleanly ({matching.Count} files linted; {scopedBaseline.Count} baselined warnings).");
            return 0;
        }
    }

    public static string EslintWarningBaselineKey(string relativePath, string ruleId, string lineContent)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(lineContent.Trim()));
        return $"{relativePath}|{ruleId}|{Convert.ToHexStringLower(hash)[..16]}";
    }

    public static (IReadOnlyList<EslintFinding> NewWarnings, IReadOnlyList<string> StaleEntries) CompareEslintWarningsWithBaseline(
        IReadOnlyList<(EslintFinding Finding, string Key)> warnings,
        IReadOnlyList<string> baseline)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        ArgumentNullException.ThrowIfNull(baseline);

        var remaining = baseline
            .GroupBy(k => k, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var newWarnings = new List<EslintFinding>();

        foreach (var (finding, key) in warnings)
        {
            if (remaining.TryGetValue(key, out int count) && count > 0)
            {
                remaining[key] = count - 1;
            }
            else
            {
                newWarnings.Add(finding);
            }
        }

        var stale = remaining
            .Where(kv => kv.Value > 0)
            .SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value))
            .Order(StringComparer.Ordinal)
            .ToList();

        return (newWarnings, stale);
    }

    private static bool IsGeneratedEslintPath(string relativePath) =>
        relativePath.Equals("mockServiceWorker.js", StringComparison.OrdinalIgnoreCase) ||
        relativePath.EndsWith("/mockServiceWorker.js", StringComparison.OrdinalIgnoreCase);

    private static bool TryCollectEslintResults(
        string repoRoot,
        string stdout,
        string stderr,
        int exitCode,
        string? fallbackRelativePath,
        GitIndexReader? reader,
        Dictionary<string, string[]> lineCache,
        List<EslintFinding> errors,
        List<(EslintFinding Finding, string Key)> warnings)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            if (exitCode != 0)
            {
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    Console.WriteLine(stderr.TrimEnd());
                }

                Console.WriteLine("[ERROR] ESLint execution failed without JSON output.");
                return false;
            }

            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    Console.WriteLine(stderr.TrimEnd());
                }

                Console.WriteLine("[ERROR] Unexpected ESLint JSON output shape.");
                return false;
            }

            int parsedErrorsInBatch = 0;
            foreach (JsonElement fileElem in doc.RootElement.EnumerateArray())
            {
                string? rawFilePath = fileElem.TryGetProperty("filePath", out JsonElement fpElem)
                    ? fpElem.GetString()
                    : null;
                string rel = (!string.IsNullOrWhiteSpace(rawFilePath)
                        ? GitIndexReader.TryNormalizeRelativePath(repoRoot, rawFilePath)
                        : null)
                    ?? fallbackRelativePath
                    ?? (rawFilePath?.Replace('\\', '/') ?? "<unknown>");

                if (!fileElem.TryGetProperty("messages", out JsonElement messagesElem) ||
                    messagesElem.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement msgElem in messagesElem.EnumerateArray())
                {
                    int severity = msgElem.TryGetProperty("severity", out JsonElement sevElem) && sevElem.TryGetInt32(out int sevVal)
                        ? sevVal
                        : 2;
                    string ruleId = (msgElem.TryGetProperty("ruleId", out JsonElement ruleElem) && ruleElem.ValueKind == JsonValueKind.String
                        ? ruleElem.GetString()
                        : null) ?? "<syntax>";
                    string message = (msgElem.TryGetProperty("message", out JsonElement textElem) && textElem.ValueKind == JsonValueKind.String
                        ? textElem.GetString()
                        : null) ?? "ESLint violation";
                    int line = msgElem.TryGetProperty("line", out JsonElement lineElem) && lineElem.TryGetInt32(out int lineVal)
                        ? lineVal
                        : 1;
                    int column = msgElem.TryGetProperty("column", out JsonElement colElem) && colElem.TryGetInt32(out int colVal)
                        ? colVal
                        : 1;

                    var finding = new EslintFinding(rel, line, column, severity, ruleId, message);
                    if (severity >= 2)
                    {
                        parsedErrorsInBatch++;
                        errors.Add(finding);
                    }
                    else if (severity == 1)
                    {
                        string lineContent = GetSourceLine(repoRoot, rel, line, reader, lineCache);
                        warnings.Add((finding, EslintWarningBaselineKey(rel, ruleId, lineContent)));
                    }
                }
            }

            if (exitCode != 0 && parsedErrorsInBatch == 0)
            {
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    Console.WriteLine(stderr.TrimEnd());
                }

                Console.WriteLine($"[ERROR] ESLint exited with code {exitCode}.");
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                Console.WriteLine(stdout.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.WriteLine(stderr.TrimEnd());
            }

            Console.WriteLine("[ERROR] Failed to parse ESLint JSON output.");
            return false;
        }
    }

    private static string GetSourceLine(
        string repoRoot,
        string relativePath,
        int oneBasedLine,
        GitIndexReader? reader,
        Dictionary<string, string[]> lineCache)
    {
        if (!lineCache.TryGetValue(relativePath, out string[]? lines))
        {
            if (reader is not null && reader.TryReadLines(relativePath, out string[] indexedLines))
            {
                lines = indexedLines;
            }
            else
            {
                string fullPath = Path.Combine(repoRoot, relativePath);
                lines = File.Exists(fullPath) ? File.ReadAllLines(fullPath) : [];
            }

            lineCache[relativePath] = lines;
        }

        return oneBasedLine >= 1 && oneBasedLine <= lines.Length
            ? lines[oneBasedLine - 1]
            : string.Empty;
    }

    private static List<string> ReadEslintBaseline(string baselinePath, GitIndexReader? reader)
    {
        if (reader is not null)
        {
            return reader.TryReadLines(EslintWarningsBaselineFile, out string[] indexedLines)
                ? [.. indexedLines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))]
                : [];
        }

        return File.Exists(baselinePath)
            ? [.. File.ReadAllLines(baselinePath).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))]
            : [];
    }

    private static void WriteEslintBaseline(string baselinePath, IEnumerable<string> keys)
    {
        var lines = new List<string>
        {
            "# ESLint warnings ratchet baseline (pnpm cadence check eslint). Pre-existing warnings frozen behind a shrink-only baseline.",
            "# One per line: path|ruleId|first 16 hex digits of SHA-256(trimmed line). Sorted ordinally. The list may only shrink.",
            "# Regenerate with CADENCE_UPDATE_BASELINE=1 pnpm cadence check eslint (docs/cli.md).",
        };
        lines.AddRange(keys.Order(StringComparer.Ordinal));
        File.WriteAllText(baselinePath, string.Join('\n', lines) + "\n");
    }

    public static List<string> BuildEslintArgs(IEnumerable<string> additionalArgs) =>
        ["exec", "eslint", "--no-warn-ignored", "--max-warnings", "0", .. additionalArgs];

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

    private static (int ExitCode, string Stdout, string Stderr) RunProcessCapture(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        byte[]? stdinBytes)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = stdinBytes is not null,
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
                return (1, string.Empty, $"Could not start '{fileName}'.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (stdinBytes is not null)
            {
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
            }

            process.WaitForExit();
            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();
            return (process.ExitCode, stdout, stderr);
        }
        catch (Win32Exception ex)
        {
            return (1, string.Empty, $"Could not start '{fileName}': {ex.Message}");
        }
    }

    public static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();
}
