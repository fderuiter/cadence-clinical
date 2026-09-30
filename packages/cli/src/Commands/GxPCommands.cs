using System.CommandLine;

using Cadence.Cli.Services;

namespace Cadence.Cli.Commands;

public static class GxPSyncCommand
{
    public static void Configure(Command command)
    {
        var dryRun = new Option<bool>("--dry-run") { Description = "Validate GxP compliance without making changes" };
        var commit = new Option<bool>("--commit") { Description = "Automatically stage updated compliance docs" };
        command.Options.Add(dryRun);
        command.Options.Add(commit);
        command.SetAction(result => GxPComplianceService.SyncCompliance(result.GetValue(dryRun), result.GetValue(commit)));
    }
}
