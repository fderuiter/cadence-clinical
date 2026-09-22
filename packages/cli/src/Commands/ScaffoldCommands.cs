using System.ComponentModel;
using System.Diagnostics;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class ScaffoldServiceCommand : Command<ScaffoldServiceCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<name>")]
        public string Name { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        AnsiConsole.MarkupLine($"[cyan]Scaffolding Clean Architecture service: {settings.Name}[/]");
        var psi = new ProcessStartInfo("bash", $"scripts/migrate_service_wizard.sh -ServiceName {settings.Name}") { UseShellExecute = false };
        using var p = Process.Start(psi);
        p?.WaitForExit();
        return p?.ExitCode ?? 0;
    }
}

public class ScaffoldAdrCommand : Command<ScaffoldAdrCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<title>")]
        public string Title { get; set; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        AnsiConsole.MarkupLine($"[cyan]Scaffolding new ADR: {settings.Title}[/]");
        return 0;
    }
}
