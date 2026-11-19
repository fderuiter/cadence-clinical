using System.CommandLine;

using Cadence.Cli.Services;

namespace Cadence.Cli.Commands;

public static class GxPSyncCommand
{
    public static void Configure(Command command)
    {
        var dryRun = new Option<bool>("--dry-run") { Description = "Validate requirement tags and stamps without running tests or writing files" };
        var resultsDir = new Option<string?>("--results-dir") { Description = "Reuse existing TRX reports and database evidence under this directory instead of running dotnet test" };
        command.Options.Add(dryRun);
        command.Options.Add(resultsDir);
        command.SetAction(result => GxPComplianceService.SyncCompliance(result.GetValue(dryRun), result.GetValue(resultsDir)));
    }
}

public static class GxPSnapshotCommand
{
    public static void Configure(Command command)
    {
        var commit = new Option<bool>("--commit") { Description = "Commit the staged snapshot (with CADENCE_GXP_SNAPSHOT=1 for the snapshot guard)" };
        command.Options.Add(commit);
        command.SetAction(result => GxPComplianceService.Snapshot(result.GetValue(commit)));
    }
}
