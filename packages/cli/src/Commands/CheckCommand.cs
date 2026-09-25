using System.ComponentModel;

using Cadence.Cli.Services;

using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class CheckCommand : Command<CheckCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[gate]")]
        [Description("Specific quality gate to execute (e.g., clean-secrets-baseline, duplication, adrs).")]
        public string? Gate { get; set; }

        [CommandArgument(1, "[files]")]
        [Description("Optional list of target files to validate.")]
        public string[]? Files { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return QualityGateOrchestrator.RunQualityGates(settings.Gate, settings.Files);
    }
}
