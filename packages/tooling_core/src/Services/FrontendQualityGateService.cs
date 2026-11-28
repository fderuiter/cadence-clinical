using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Fail-closed Prettier, ESLint, and TypeScript/Vue SFC typecheck quality gates for <c>cadence check</c> and <c>cadence check --pre-commit</c>.
/// </summary>
public static partial class FrontendQualityGateService
{
    private const int MaxFilesPerBatch = 400;

    private static readonly HashSet<string> PrettierExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".mts", ".cts", ".vue", ".json", ".css", ".md", ".yaml", ".yml",
    };

    public static IReadOnlyCollection<string> SupportedPrettierExtensions => PrettierExtensions;

    private static readonly HashSet<string> EslintExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".mts", ".cts", ".vue", ".css",
    };

    public static IReadOnlyCollection<string> SupportedEslintExtensions => EslintExtensions;

    private static readonly HashSet<string> TypecheckExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".mts", ".cts", ".vue", ".json", ".yaml", ".yml",
    };

    public static IReadOnlyCollection<string> SupportedTypecheckExtensions => TypecheckExtensions;

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
        GitIndexReader? indexReader = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        if (targetFiles is { Length: 0 })
        {
            return 0;
        }

        repoRoot ??= FindRepoRoot();

        string? pnpm = ResolveUsablePnpm(repoRoot, getEnvironmentVariable);
        if (pnpm is null)
        {
            return 1;
        }

        if (!EnsureNodeModules(repoRoot))
        {
            return 1;
        }

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
                rawFiles = PreCommitOpenApiService.TryGetTrackedFiles(repoRoot)
                    ?? EnumerateRepositoryFiles(repoRoot);
            }

            var matching = SelectExistingFiles(repoRoot, rawFiles, EslintExtensions, reader);
            if (matching.Count == 0)
            {
                return 0;
            }

            const string EcrfStoreRelPath = "apps/web/src/stores/demo/clinicalEcrf.ts";
            string duplicateCheckScript = Path.Combine(repoRoot, "scripts", "check_duplicate_identifiers.js");
            if (File.Exists(duplicateCheckScript) &&
                matching.Any(f => f.Equals(EcrfStoreRelPath, StringComparison.OrdinalIgnoreCase)))
            {
                if (reader is { HasAuthoritativeIndex: true })
                {
                    using var storeWorkspace = reader.Materialize([EcrfStoreRelPath]);
                    string tempStorePath = Path.Combine(storeWorkspace.RootPath, "apps", "web", "src", "stores", "demo", "clinicalEcrf.ts");
                    if (File.Exists(tempStorePath) &&
                        RunProcess(Path.Combine(repoRoot, "apps", "web"), "node", [duplicateCheckScript, tempStorePath], getEnvironmentVariable: getEnvironmentVariable) != 0)
                    {
                        return 1;
                    }
                }
                else
                {
                    string diskStorePath = Path.Combine(repoRoot, "apps", "web", "src", "stores", "demo", "clinicalEcrf.ts");
                    if (RunProcess(Path.Combine(repoRoot, "apps", "web"), "node", [duplicateCheckScript, diskStorePath], getEnvironmentVariable: getEnvironmentVariable) != 0)
                    {
                        return 1;
                    }
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

                    var stdinArgs = BuildEslintArgs(["--stdin", "--stdin-filename", rel]);

                    if (RunProcessWithStdin(repoRoot, pnpm, stdinArgs, blobBytes, suppressStdoutOnSuccess: false) != 0)
                    {
                        Console.WriteLine($"[ERROR] ESLint violations detected in staged '{rel}'. Fix the findings above or run 'pnpm lint'.");
                        return 1;
                    }
                }

                return 0;
            }

            foreach (string[] batch in matching.Chunk(MaxFilesPerBatch))
            {
                var args = BuildEslintArgs(["--", .. batch]);
                if (RunProcess(repoRoot, pnpm, args, getEnvironmentVariable: getEnvironmentVariable) != 0)
                {
                    Console.WriteLine(preCommit
                        ? "[ERROR] ESLint violations detected in staged files. Fix the findings above or run 'pnpm lint'."
                        : "[ERROR] Workspace ESLint check ('pnpm cadence check eslint') failed. Fix the findings above or run 'pnpm lint'.");
                    return 1;
                }
            }

            return 0;
        }
    }

    public static int CheckTypecheck(
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

        if (preCommit || targetFiles is not null)
        {
            var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
            using (ownedReader)
            {
                var rawFiles = preCommit && targetFiles is null
                    ? PreCommitOpenApiService.GetStagedFiles(repoRoot)
                    : (IReadOnlyList<string>)targetFiles!;

                var matching = SelectExistingFiles(repoRoot, rawFiles, TypecheckExtensions, reader);
                if (matching.Count == 0)
                {
                    return 0;
                }
            }
        }

        string? pnpm = ResolveUsablePnpm(repoRoot);
        if (pnpm is null)
        {
            return 1;
        }

        if (!EnsureNodeModules(repoRoot))
        {
            return 1;
        }

        if (RunProcess(repoRoot, pnpm, ["-r", "typecheck"]) != 0)
        {
            Console.WriteLine("[ERROR] Workspace TypeScript and Vue SFC typecheck ('pnpm -r typecheck') failed.");
            return 1;
        }

        return 0;
    }

    public static List<string> BuildEslintArgs(IEnumerable<string> additionalArgs) =>
        ["exec", "eslint", "--no-warn-ignored", "--max-warnings", "0", .. additionalArgs];

    public static string FindRepoRoot() => WorkspacePrimitives.FindRepoRoot();
}
