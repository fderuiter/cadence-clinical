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
            Description = "Run the quick variants used by the pre-commit hook (the openapi gate skips exporting specs from the services).",
        };

        // A free positional argument would otherwise swallow a mistyped option such as --pre-comit as a gate name.
        gate.Validators.Add(RejectOptionLikeTokens);
        files.Validators.Add(RejectOptionLikeTokens);

        command.Arguments.Add(gate);
        command.Arguments.Add(files);
        command.Options.Add(preCommit);
        command.SetAction(result =>
        {
            string[] targets = result.GetValue(files) ?? [];
            return QualityGateOrchestrator.RunQualityGates(result.GetValue(gate), targets.Length == 0 ? null : targets, result.GetValue(preCommit));
        });
    }

    private static void RejectOptionLikeTokens(ArgumentResult result)
    {
        foreach (var token in result.Tokens.Where(t => t.Value.StartsWith('-')))
        {
            result.AddError($"Unrecognized option '{token.Value}'.");
        }
    }
}
