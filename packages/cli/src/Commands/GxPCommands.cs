using System.ComponentModel;

using Cadence.Cli.Services;

using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class GxPSyncCommand : Command<GxPSyncCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--dry-run")]
        [Description("Validate GxP compliance without making changes")]
        public bool DryRun { get; set; }

        [CommandOption("--commit")]
        [Description("Automatically stage updated compliance docs")]
        public bool Commit { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return GxPComplianceService.SyncCompliance(settings.DryRun, settings.Commit);
    }
}

public class GxPValidateCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return GxPComplianceService.ValidateCompliance();
    }
}
