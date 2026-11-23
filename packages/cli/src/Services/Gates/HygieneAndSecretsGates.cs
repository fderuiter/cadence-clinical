using System.Collections.Generic;

using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services.Gates;

/// <summary>Text hygiene, secrets baseline, and code duplication quality gates.</summary>
internal static class HygieneAndSecretsGates
{
    public static IReadOnlyList<QualityGate> Entries { get; } =
    [
        new("trailing-whitespace", ["check-trailing-whitespace"],
            "No trailing spaces or tabs at the end of lines in text files",
            ctx => FileHygieneValidatorService.ValidateTrailingWhitespace(ctx.TargetFiles, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
        new("end-of-file", ["end-of-file-fixer", "eof-newline"],
            "Non-empty UTF-8 text files end with a newline character",
            ctx => FileHygieneValidatorService.ValidateEndOfFileNewline(ctx.TargetFiles, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
        new("clean-secrets", ["clean-secrets-baseline"],
            "Verifies `.secrets.baseline` is valid, normalized JSON with the canonical exclusion filter without mutating it",
            ctx => SecretsBaselineCleanerService.CleanSecretsBaseline(repoRoot: ctx.RepoRoot, check: true, preCommit: ctx.PreCommit, indexReader: ctx.IndexReader) == 0)
        {
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "validates staged `.secrets.baseline` JSON without mutating the working tree",
        },
        new("detect-secrets", ["secrets-scan"],
            "Scans changed or tracked files against `.secrets.baseline` using `detect-secrets-hook` from `.cadence/venv`, `PATH` or `uvx` (fails closed when missing)",
            ctx => DetectSecretsGateService.Scan(ctx.RepoRoot, ctx.TargetFiles, ctx.PreCommit, indexReader: ctx.IndexReader, baseRef: ctx.BaseRef, pushBefore: ctx.PushBefore) == 0)
        {
            TakesFiles = true,
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "scans materialized staged blobs instead of the working tree",
        },
        new("duplication", ["detect-code-duplication"], "Duplicated code blocks over 15 normalized lines involving a C# or JavaScript file",
            ctx => DuplicationDetectorService.DetectDuplication(ctx.Files, ctx.RepoRoot, ctx.PreCommit, ctx.IndexReader) == 0)
        { TakesFiles = true },
    ];
}
