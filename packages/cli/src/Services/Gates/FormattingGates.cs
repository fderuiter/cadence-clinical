using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Services;
using Cadence.Topology;

namespace Cadence.Cli.Services.Gates;

/// <summary>Formatting and static workflow/Dockerfile linter gates.</summary>
internal static class FormattingGates
{
    public static IReadOnlyList<QualityGate> Entries { get; } =
    [
        new("dotnet-format", [], "Whitespace formatting against `.editorconfig` (`dotnet format whitespace`)",
            ctx => CheckDotnetFormat(ctx, "whitespace"))
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "checks staged C# blobs; fails closed when `dotnet` is missing",
        },
        new("dotnet-format-style", [], "Roslyn code-style rules (`dotnet format style`)",
            ctx => CheckDotnetFormat(ctx, "style"))
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "checks staged C# files and fails closed when `dotnet` is missing or unstaged edits overlap MSBuild inputs",
        },
        new("prettier", [], "Prettier formatting across JS, TS, Vue, CSS, JSON, Markdown and YAML files",
            ctx => FrontendQualityGateService.CheckPrettier(ctx.RepoRoot, ctx.TargetFiles, ctx.PreCommit, ctx.IndexReader) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "streams staged JS, TS, Vue, CSS, JSON, Markdown and YAML blobs over stdin; fails closed when `pnpm` or `node_modules` is missing",
        },
        new("eslint", [], "ESLint and duplicate store identifier checks across JS, TS and Vue files",
            ctx => FrontendQualityGateService.CheckEslint(ctx.RepoRoot, ctx.TargetFiles, ctx.PreCommit, ctx.IndexReader) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "lints staged JS, TS and Vue blobs over stdin; fails closed when `pnpm` or `node_modules` is missing",
        },
        new("actionlint", ["validate-workflows"],
            "GitHub Actions workflow and composite-action static analysis via `actionlint` from `.cadence/bin` or `PATH` (fails closed when missing)",
            ctx => CliLinterGateService.RunActionlint(ctx.RepoRoot, ctx.TargetFiles, ctx.PreCommit, ctx.IndexReader) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "validates staged `.github/workflows` and `.github/actions` blobs; fails closed when `actionlint` is missing",
        },
        new("hadolint", ["hadolint-docker", "validate-dockerfiles"],
            "Dockerfile best-practice and syntax linting (`hadolint --failure-threshold error`) from `.cadence/bin` or `PATH` (fails closed when missing)",
            ctx => CliLinterGateService.RunHadolint(ctx.RepoRoot, ctx.TargetFiles, ctx.PreCommit, ctx.IndexReader) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "lints staged `Dockerfile*` blobs; fails closed when `hadolint` is missing",
        },
        new("yaml", ["check-yaml", "validate-yaml"],
            "YAML 1.2 syntax, indentation, quoted scalars, flow brackets and duplicate mapping keys",
            ctx => YamlSyntaxValidatorService.ValidateYaml(ctx.TargetFiles, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
    ];

    private static bool CheckDotnetFormat(GateContext ctx, string subcommand)
    {
        if (ctx.TargetFiles is { Length: 0 })
        {
            return true;
        }

        if (!GateExecutionHelpers.IsDotnetAvailable())
        {
            Console.WriteLine("[ERROR] .NET SDK ('dotnet') was not found on PATH.");
            return false;
        }

        string solutionFile = RepositoryRoot.ResolveSolutionFileName(ctx.RepoRoot);

        if (ctx.PreCommit || ctx.TargetFiles is not null)
        {
            var rawFiles = ctx.PreCommit && ctx.TargetFiles is null
                ? PreCommitOpenApiService.GetStagedFiles(ctx.RepoRoot)
                : (IReadOnlyList<string>)ctx.TargetFiles!;
            if (rawFiles.Count == 0)
            {
                return true;
            }

            var reader = GitIndexReader.ResolvePreCommitReader(ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader, out var ownedReader);
            using (ownedReader)
            {
                var csFiles = rawFiles
                    .Select(f => GitIndexReader.TryNormalizeRelativePath(ctx.RepoRoot, f))
                    .Where(f => !string.IsNullOrEmpty(f) && f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    .Select(f => f!)
                    .Where(f => reader is { HasAuthoritativeIndex: true }
                        ? reader.FileExists(f)
                        : File.Exists(Path.Combine(ctx.RepoRoot, f)))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToList();

                if (csFiles.Count == 0)
                {
                    return true;
                }

                if (string.Equals(subcommand, "whitespace", StringComparison.Ordinal))
                {
                    if (reader is { HasAuthoritativeIndex: true })
                    {
                        var materializePaths = new List<string>(csFiles);
                        materializePaths.AddRange(reader.Paths.Where(IsEditorConfigPath));

                        using var stagedWorkspace = reader.Materialize(materializePaths);
                        var stagedWhitespaceArgs = new List<string> { "format", "whitespace", ".", "--folder", "--verify-no-changes", "--include" };
                        stagedWhitespaceArgs.AddRange(csFiles);
                        return GateExecutionHelpers.RunProcess(stagedWorkspace.RootPath, "dotnet", [.. stagedWhitespaceArgs]) == 0;
                    }

                    var whitespaceArgs = new List<string> { "format", "whitespace", ".", "--folder", "--verify-no-changes", "--include" };
                    whitespaceArgs.AddRange(csFiles);
                    return GateExecutionHelpers.RunProcess(ctx.RepoRoot, "dotnet", [.. whitespaceArgs]) == 0;
                }

                var groups = csFiles.GroupBy(f => PreCommitOpenApiService.FindNearestCsproj(ctx.RepoRoot, f), StringComparer.Ordinal).ToList();
                if (ctx.PreCommit && reader is { HasAuthoritativeIndex: true })
                {
                    List<string> overlappingUnstaged = groups.Any(g => g.Key is null)
                        ? GitIndexReader.GetOverlappingUnstagedPaths(ctx.RepoRoot, GitIndexReader.IsMsBuildInputPath)
                        : GitIndexReader.GetOverlappingUnstagedProjectClosurePaths(
                            ctx.RepoRoot,
                            groups.Select(g => g.Key!),
                            reader);
                    if (overlappingUnstaged.Count > 0)
                    {
                        string sample = string.Join(", ", overlappingUnstaged.Take(5));
                        Console.WriteLine($"[ERROR] Unstaged or untracked working-tree edits overlap MSBuild inputs ({sample}). Stage or stash them before committing so 'dotnet format {subcommand}' can verify the exact staged snapshot.");
                        return false;
                    }
                }

                if (!ctx.PreCommit && groups.Count >= 3 && File.Exists(Path.Combine(ctx.RepoRoot, solutionFile)))
                {
                    var slnArgs = new List<string> { "format", subcommand, solutionFile, "--verify-no-changes", "--no-restore", "--include" };
                    slnArgs.AddRange(csFiles);
                    return GateExecutionHelpers.RunProcess(ctx.RepoRoot, "dotnet", [.. slnArgs]) == 0;
                }

                foreach (var group in groups)
                {
                    string workspace = group.Key ?? solutionFile;
                    var args = new List<string> { "format", subcommand, workspace, "--verify-no-changes", "--no-restore", "--include" };
                    args.AddRange(group);
                    if (GateExecutionHelpers.RunProcess(ctx.RepoRoot, "dotnet", [.. args]) != 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        return GateExecutionHelpers.RunProcess(ctx.RepoRoot, "dotnet", "format", subcommand, solutionFile, "--verify-no-changes", "--no-restore") == 0;
    }

    private static bool IsEditorConfigPath(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        return string.Equals(fileName, ".editorconfig", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, ".globalconfig", StringComparison.OrdinalIgnoreCase);
    }
}
