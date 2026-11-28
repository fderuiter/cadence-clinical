using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Validates JSON, XML, and YAML dependency manifests, lockstep MSBuild and <c>pnpm</c> catalog cohorts,
/// <c>.github/dependabot.yml</c> target/ignore uniqueness, cohort and trailing catch-all group ordering,
/// <c>open-pull-requests-limit</c> caps, synchronized <c>PostgresTestImage</c> <c>@sha256</c> digests, and forbidden
/// client-side crypto or runtime test-harness packages across the workspace or staged Git index.
/// </summary>
public static partial class DependencyValidatorService
{
    private static readonly string[] RequiredSolutionFiles =
    [
        "Directory.Build.props",
        "Directory.Packages.props",
        ".config/dotnet-tools.json",
        "pnpm-workspace.yaml",
        DependabotRelativePath,
        "packages/testing/src/Persistence/PostgresTestImage.cs",
        "docker/docker-compose.yml",
        ".github/workflows/ci.yml",
        ".github/workflows/production-pipeline.yml",
        "apps/apphost/src/Program.cs",
    ];

    private sealed record RepoSnapshot(string RepoRoot, GitIndexReader? Reader)
    {
        public bool Exists(string relativePath) =>
            Reader is { HasAuthoritativeIndex: true }
                ? Reader.FileExists(relativePath)
                : File.Exists(Path.Combine(RepoRoot, relativePath));

        public bool TryReadBytes(string relativePath, out byte[] bytes, out string? error) =>
            WorkspacePrimitives.TryReadBytes(
                RepoRoot,
                relativePath,
                Reader is { HasAuthoritativeIndex: true } ? Reader : null,
                out bytes,
                out error);

        public bool TryReadText(string relativePath, out string text, out string? error)
        {
            text = string.Empty;
            if (!TryReadBytes(relativePath, out byte[] bytes, out error))
            {
                return false;
            }

            try
            {
                text = WorkspacePrimitives.StrictUtf8.GetString(WorkspacePrimitives.StripBom(bytes));
                return true;
            }
            catch (DecoderFallbackException ex)
            {
                error = $"Invalid UTF-8 byte sequence ({ex.Message}).";
                return false;
            }
        }
    }

    public static int ValidateDependencies(
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
        Console.WriteLine("Validating dependency manifests, MSBuild/pnpm cohorts, Dependabot rules, and PostgreSQL digest parity...");

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            var snapshot = new RepoSnapshot(repoRoot, reader);
            var packageManifests = DiscoverPackageManifests(snapshot);
            bool hasSolution = snapshot.Exists("Cadence.sln");
            bool hasPnpmWorkspace = snapshot.Exists("pnpm-workspace.yaml");
            bool hasDotnetTools = snapshot.Exists(".config/dotnet-tools.json");
            bool hasBuildProps = snapshot.Exists("Directory.Build.props");
            bool hasPackagesProps = snapshot.Exists("Directory.Packages.props");
            string? dependabotPath = ResolveDependabotPath(snapshot);
            bool hasDependabot = dependabotPath is not null;
            bool hasPostgresImageSource = snapshot.Exists("packages/testing/src/Persistence/PostgresTestImage.cs");
            bool hasDockerCompose = snapshot.Exists("docker/docker-compose.yml");

            if (!hasSolution &&
                packageManifests.Count == 0 &&
                !hasPnpmWorkspace &&
                !hasDotnetTools &&
                !hasBuildProps &&
                !hasPackagesProps &&
                !hasDependabot &&
                !hasPostgresImageSource &&
                !hasDockerCompose)
            {
                Console.WriteLine("✔ No dependency manifests found, skipping.");
                return 0;
            }

            int errors = 0;
            if (hasSolution)
            {
                errors += ValidateRequiredSolutionFiles(snapshot);
            }

            Dictionary<string, string>? catalog = null;
            if (hasPnpmWorkspace)
            {
                errors += ValidatePnpmWorkspaceCatalog(snapshot, out catalog);
            }

            errors += ValidateWorkspacePackageManifests(snapshot, packageManifests, catalog);

            Dictionary<string, string>? msBuildProps = null;
            if (hasBuildProps)
            {
                errors += ValidateDirectoryBuildProps(snapshot, out msBuildProps);
            }

            if (hasPackagesProps)
            {
                errors += ValidateDirectoryPackagesProps(snapshot, msBuildProps);
            }

            if (hasDotnetTools)
            {
                errors += ValidateDotnetToolsManifest(snapshot, msBuildProps);
            }

            if (dependabotPath is not null)
            {
                errors += ValidateDependabotConfig(snapshot, dependabotPath);
            }

            if (hasPostgresImageSource || hasDockerCompose)
            {
                errors += ValidatePostgresDigestParity(snapshot, hasSolution);
            }

            if (errors > 0)
            {
                Console.WriteLine($"\n[ERROR] Found {errors} dependency manifest, cohort, Dependabot, or container digest violation(s).");
                return 1;
            }

            Console.WriteLine("✔ Dependency manifests, cohort pins, Dependabot rules, and PostgreSQL digest checks passed cleanly.");
            return 0;
        }
    }

    private static int ValidateRequiredSolutionFiles(RepoSnapshot snapshot)
    {
        int errors = 0;
        foreach (string requiredPath in RequiredSolutionFiles)
        {
            if (!snapshot.Exists(requiredPath))
            {
                Console.WriteLine($"[ERROR] Missing required dependency manifest or guardrail file '{requiredPath}'.");
                errors++;
            }
        }

        return errors;
    }
}
