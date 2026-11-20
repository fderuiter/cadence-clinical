using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;

using Cadence.Cli.Services;

namespace Cadence.Cli.Commands;

public static class GxPSyncCommand
{
    public static void Configure(Command command)
    {
        var dryRun = new Option<bool>("--dry-run") { Description = "Validate requirement tags and stamps without running tests or writing files" };
        var resultsDir = CreateResultsDirOption();
        command.Options.Add(dryRun);
        command.Options.Add(resultsDir);
        command.SetAction(result => GxPComplianceService.SyncCompliance(result.GetValue(dryRun), result.GetValue(resultsDir)));
    }

    internal static Option<string?> CreateResultsDirOption()
    {
        var option = new Option<string?>("--results-dir")
        {
            Description = "Reuse existing TRX reports and database evidence under this directory instead of running dotnet test",
        };
        option.Validators.Add(ValidateResultsDir);
        return option;
    }

    private static void ValidateResultsDir(OptionResult result)
    {
        foreach (string value in result.Tokens.Select(t => t.Value))
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError("Option '--results-dir' requires a non-empty directory path.");
            }
            else if (value.StartsWith('-'))
            {
                result.AddError($"Unrecognized option '{value}'.");
            }
        }
    }
}

public static class GxPSnapshotCommand
{
    public static void Configure(Command command)
    {
        var commit = new Option<bool>("--commit") { Description = "Commit the staged snapshot (with CADENCE_GXP_SNAPSHOT=1 for the snapshot guard)" };
        var resultsDir = GxPSyncCommand.CreateResultsDirOption();
        command.Options.Add(commit);
        command.Options.Add(resultsDir);
        command.SetAction(result => GxPComplianceService.Snapshot(result.GetValue(commit), result.GetValue(resultsDir)));
    }
}
