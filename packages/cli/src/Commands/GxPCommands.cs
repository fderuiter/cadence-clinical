using System.CommandLine;

using Cadence.Cli.Services;

namespace Cadence.Cli.Commands;

public static class GxPSyncCommand
{
    public static void Configure(Command command)
    {
        var dryRun = new Option<bool>("--dry-run") { Description = "Validate requirement tags and stamps without running tests or writing files" };
        command.Options.Add(dryRun);
        command.SetAction(result => GxPComplianceService.SyncCompliance(result.GetValue(dryRun)));
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
