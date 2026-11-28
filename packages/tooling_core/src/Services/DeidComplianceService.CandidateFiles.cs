using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static partial class DeidComplianceService
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Source, configuration, documentation, and asset text extensions across the .NET, Vue, and VitePress code base.
        ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".json", ".md", ".log", ".txt", ".yaml", ".yml", ".csv",
        ".toml", ".xml", ".html", ".css", ".cs", ".vue", ".sql", ".svg",
    };

    private static readonly HashSet<string> AlwaysScannedNames = new(StringComparer.Ordinal)
    {
        "Dockerfile", "LICENSE", "Makefile", "Pipfile", "pyproject.toml", "package.json", "pnpm-workspace.yaml",
        "pnpm-lock.yaml", "uv.lock",
    };

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.Ordinal)
    {
        "tests", "test", "testing", "node_modules", ".git", ".venv", "env", "build", "dist", ".github", ".ruff_cache",
        ".pytest_cache", "scripts", "docker", "verification", "alembic", "deid", "coverage", ".agents", ".jules",
        ".cadence", "bin", "obj",
    };

    private static readonly string[] ExcludedPathPrefixes =
    [
        "baselines/",
        "docs/cdisc/",
        "docs/nci/",
        "docs/sdlc/runs/",
        "docs/openapi/",
        "docs/schema/",
        "docs/.vitepress/.temp/",
        "docs/.vitepress/cache/",
        "docs/.vitepress/dist/",
    ];

    private static readonly HashSet<string> ExcludedRelativePaths = new(StringComparer.Ordinal)
    {
        "docs/sdlc/requirements_traceability_matrix.md",
        "docs/sdlc/iq_oq_pq_execution_report.md",
    };

    private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.Ordinal)
    {
        "package.json", "pyproject.toml", "pnpm-workspace.yaml", "pnpm-lock.yaml", "uv.lock", ".secrets.baseline",
        ".deid-baseline.txt", ".duplication-baseline.txt", "eslint.config.mjs",
    };

    /// <summary>Whether a repository-relative path is outside the scan: tests, vendored standards, GxP runs, tooling and generated folders.</summary>
    public static bool IsExcludedPath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        string lower = normalized.ToLowerInvariant();
        if (ExcludedRelativePaths.Contains(lower) ||
            ExcludedPathPrefixes.Any(prefix => lower.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return true;
        }

        string[] parts = lower.Split('/');
        if (parts.Any(ExcludedDirectories.Contains))
        {
            return true;
        }

        string name = parts[^1];
        return name.StartsWith("test_", StringComparison.Ordinal)
            || name.EndsWith(".test.js", StringComparison.Ordinal)
            || name.EndsWith(".test.ts", StringComparison.Ordinal)
            || name.EndsWith(".test.jsx", StringComparison.Ordinal)
            || name.EndsWith(".test.tsx", StringComparison.Ordinal)
            || name.EndsWith(".css", StringComparison.Ordinal)
            || ExcludedFileNames.Contains(name);
    }

    /// <summary>Whether a file is text worth scanning: a known text extension or name, or no extension and no NUL byte.</summary>
    public static bool ShouldScanFile(string path)
    {
        if (AlwaysScannedNames.Contains(Path.GetFileName(path)))
        {
            return true;
        }

        string extension = Path.GetExtension(path);
        if (extension.Length > 0)
        {
            return TextExtensions.Contains(extension);
        }

        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[1024];
            int read = stream.Read(buffer, 0, buffer.Length);
            return Array.IndexOf(buffer, (byte)0, 0, read) < 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Explicit files, GitRevisionScopeService diff scope, or every file git tracks (git ls-files -z), filtered through
    // IsExcludedPath and ShouldScanFile (TextExtensions, AlwaysScannedNames, and extensionless NUL-byte sniffing).
    public static IReadOnlyList<string> CandidateFiles(
        string repoRoot,
        string[]? files = null,
        bool preCommit = false,
        string? baseRef = null,
        string? pushBefore = null,
        bool forceTrackedScan = false,
        GitIndexReader? reader = null,
        string? eventName = null,
        string? gitRef = null)
    {
        IEnumerable<string> relative;
        if (files is not null)
        {
            relative = files.Select(f => Path.GetRelativePath(repoRoot, Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f)));
        }
        else if (forceTrackedScan)
        {
            relative = TrackedFiles(repoRoot);
        }
        else
        {
            var scope = GitRevisionScopeService.ResolveFileScanScope(
                repoRoot,
                explicitFiles: null,
                preCommit: preCommit,
                baseRef: baseRef,
                pushBefore: pushBefore,
                eventName: eventName,
                gitRef: gitRef);
            relative = scope.Kind == GitScopeKind.NonGitDirectory
                ? TrackedFiles(repoRoot)
                : scope.Files;
        }

        return [.. relative
            .Select(p => p.Replace('\\', '/'))
            .Where(p => !p.StartsWith("../", StringComparison.Ordinal) && p != ".." && !IsExcludedPath(p))
            .Where(p => reader is not null
                ? reader.FileExists(p) && ShouldScanStagedFile(p, reader)
                : File.Exists(Path.Combine(repoRoot, p)) && ShouldScanFile(Path.Combine(repoRoot, p)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static bool ShouldScanStagedFile(string relativePath, GitIndexReader reader)
    {
        if (AlwaysScannedNames.Contains(Path.GetFileName(relativePath)))
        {
            return true;
        }

        string extension = Path.GetExtension(relativePath);
        if (extension.Length > 0)
        {
            return TextExtensions.Contains(extension);
        }

        if (!reader.TryReadBytes(relativePath, out byte[] bytes))
        {
            return false;
        }

        int probeLen = Math.Min(bytes.Length, 1024);
        return Array.IndexOf(bytes, (byte)0, 0, probeLen) < 0;
    }

    private static IEnumerable<string> TrackedFiles(string repoRoot) =>
        GitRevisionScopeService.RunGitNullTerminated(repoRoot, "ls-files", "-z")
        ?? throw new InvalidOperationException($"git ls-files failed in {repoRoot}; the DEID scan needs git to list files.");
}
