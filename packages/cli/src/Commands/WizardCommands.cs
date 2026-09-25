using System.Diagnostics;
using System.IO;

using Cadence.Cli.Services;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class WizardOnboardingCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return RunWizard("setup_wizard");
    }

    private static int RunWizard(string scriptBaseName)
    {
        bool isWindows = OperatingSystem.IsWindows();
        string scriptName = isWindows ? $"{scriptBaseName}.ps1" : $"{scriptBaseName}.sh";
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", scriptName);

        if (!File.Exists(scriptPath))
        {
            AnsiConsole.MarkupLine($"[red]Wizard script not found:[/] {scriptPath}");
            return 1;
        }

        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "powershell.exe" : "bash",
            Arguments = isWindows ? $"-ExecutionPolicy Bypass -File \"{scriptPath}\"" : $"\"{scriptPath}\"",
            WorkingDirectory = repoRoot,
            UseShellExecute = false
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit();
        return proc?.ExitCode ?? 0;
    }
}

public class WizardDotnetCommand : Command
{
    public override int Execute(CommandContext context)
    {
        bool isWindows = OperatingSystem.IsWindows();
        string scriptName = isWindows ? "setup_dotnet_wizard.ps1" : "setup_dotnet_wizard.sh";
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", scriptName);

        if (!File.Exists(scriptPath))
        {
            AnsiConsole.MarkupLine($"[red]Wizard script not found:[/] {scriptPath}");
            return 1;
        }

        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "powershell.exe" : "bash",
            Arguments = isWindows ? $"-ExecutionPolicy Bypass -File \"{scriptPath}\"" : $"\"{scriptPath}\"",
            WorkingDirectory = repoRoot,
            UseShellExecute = false
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit();
        return proc?.ExitCode ?? 0;
    }
}

public class WizardMigrateCommand : Command
{
    public override int Execute(CommandContext context)
    {
        bool isWindows = OperatingSystem.IsWindows();
        string scriptName = isWindows ? "migrate_service_wizard.ps1" : "migrate_service_wizard.sh";
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", scriptName);

        if (!File.Exists(scriptPath))
        {
            AnsiConsole.MarkupLine($"[red]Wizard script not found:[/] {scriptPath}");
            return 1;
        }

        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "powershell.exe" : "bash",
            Arguments = isWindows ? $"-ExecutionPolicy Bypass -File \"{scriptPath}\"" : $"\"{scriptPath}\"",
            WorkingDirectory = repoRoot,
            UseShellExecute = false
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit();
        return proc?.ExitCode ?? 0;
    }
}

public class WizardDecommissionSettings : CommandSettings
{
    [CommandArgument(0, "[service]")]
    public string? ServiceName { get; set; }
}

public class WizardDecommissionCommand : Command<WizardDecommissionSettings>
{
    public override int Execute(CommandContext context, WizardDecommissionSettings settings)
    {
        bool isWindows = OperatingSystem.IsWindows();
        string scriptName = isWindows ? "decommission_service_wizard.ps1" : "decommission_service_wizard.sh";
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", scriptName);

        if (!File.Exists(scriptPath))
        {
            AnsiConsole.MarkupLine($"[red]Wizard script not found:[/] {scriptPath}");
            return 1;
        }

        string arg = string.IsNullOrWhiteSpace(settings.ServiceName) ? "" : $"\"{settings.ServiceName}\"";

        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "powershell.exe" : "bash",
            Arguments = isWindows ? $"-ExecutionPolicy Bypass -File \"{scriptPath}\" {arg}" : $"\"{scriptPath}\" {arg}",
            WorkingDirectory = repoRoot,
            UseShellExecute = false
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit();
        return proc?.ExitCode ?? 0;
    }
}
