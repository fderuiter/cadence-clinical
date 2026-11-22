using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;

using Cadence.Cli.Services;

namespace Cadence.Cli.Commands;

public static class CheckCommand
{
    public static void Configure(Command command)
    {
        var gate = new Argument<string?>("gate")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = $"One of the {QualityGates.All.Count} quality gates to run, by name or alias: {QualityGates.NameList}. Without it, all of them run in this order.",
        };
        var files = new Argument<string[]>("files")
        {
            Arity = ArgumentArity.ZeroOrMore,
            Description = "Optional list of target files to validate.",
        };
        var preCommit = new Option<bool>("--pre-commit")
        {
            Description = "Run the pre-commit guardrails against staged files (used by .githooks/pre-commit).",
        };
        var exclude = new Option<string[]>("--exclude")
        {
            Description = "Skip a quality gate by name or alias when running all gates; repeat for more.",
            AllowMultipleArgumentsPerToken = false,
        };
        var baseRef = new Option<string?>("--base")
        {
            Description = "Base Git ref to compare branch changes against (default: origin/main).",
        };
        var pushBefore = new Option<string?>("--push-before")
        {
            Description = "Prior commit SHA on a push event (<before>..HEAD); falls back to tracked files on zero SHA, tags, or non-ancestors.",
        };

        // A free positional argument would otherwise swallow a mistyped option such as --pre-comit as a gate name.
        gate.Validators.Add(RejectOptionLikeTokens);
        files.Validators.Add(RejectOptionLikeTokens);
        exclude.Validators.Add(RejectOptionLikeTokens);
        baseRef.Validators.Add(RejectOptionLikeTokens);
        pushBefore.Validators.Add(RejectOptionLikeTokens);

        command.Arguments.Add(gate);
        command.Arguments.Add(files);
        command.Options.Add(preCommit);
        command.Options.Add(exclude);
        command.Options.Add(baseRef);
        command.Options.Add(pushBefore);
        command.Validators.Add(commandResult =>
        {
            bool hasPreCommit = commandResult.GetResult(preCommit) is { Implicit: false } pc
                && pc.Tokens.All(t => !string.Equals(t.Value, "false", System.StringComparison.OrdinalIgnoreCase));
            bool hasBase = commandResult.GetResult(baseRef) is { Implicit: false, Tokens.Count: > 0 } b
                && b.Tokens.Any(t => !string.IsNullOrWhiteSpace(t.Value));
            bool hasPushBefore = commandResult.GetResult(pushBefore) is { Implicit: false, Tokens.Count: > 0 } pb
                && pb.Tokens.Any(t => !string.IsNullOrWhiteSpace(t.Value));
            if (hasBase && hasPushBefore)
            {
                commandResult.AddError("Options '--base' and '--push-before' cannot be combined.");
            }

            if (hasPreCommit && (hasBase || hasPushBefore))
            {
                commandResult.AddError("Option '--pre-commit' cannot be combined with '--base' or '--push-before'.");
            }
        });
        command.SetAction(result =>
        {
            string[] targets = result.GetValue(files) ?? [];
            string[] excluded = result.GetValue(exclude) ?? [];
            return QualityGateOrchestrator.RunQualityGates(
                result.GetValue(gate),
                targets.Length == 0 ? null : targets,
                result.GetValue(preCommit),
                excluded.Length == 0 ? null : excluded,
                baseRef: result.GetValue(baseRef),
                pushBefore: result.GetValue(pushBefore));
        });
    }

    private static void RejectOptionLikeTokens(ArgumentResult result)
    {
        foreach (string value in result.Tokens.Select(t => t.Value).Where(v => v.StartsWith('-')))
        {
            result.AddError($"Unrecognized option '{value}'.");
        }
    }

    private static void RejectOptionLikeTokens(OptionResult result)
    {
        foreach (string value in result.Tokens.Select(t => t.Value).Where(v => v.StartsWith('-')))
        {
            result.AddError($"Unrecognized option '{value}'.");
        }
    }
}
