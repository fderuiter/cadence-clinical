using System.Diagnostics;
using System.IO;

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
        string scriptPath = Path.Combine("scripts", scriptName);

        if (!File.Exists(scriptPath))
        {
            AnsiConsole.MarkupLine($"[red]Wizard script not found:[/] {scriptPath}");
            return 1;
        }

        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "powershell.exe" : "bash",
            Arguments = isWindows ? $"-ExecutionPolicy Bypass -File \"{scriptPath}\"" : $"\"{scriptPath}\"",
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
        string scriptPath = Path.Combine("scripts", scriptName);

        if (!File.Exists(scriptPath))
        {
            AnsiConsole.MarkupLine($"[red]Wizard script not found:[/] {scriptPath}");
            return 1;
        }

        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "powershell.exe" : "bash",
            Arguments = isWindows ? $"-ExecutionPolicy Bypass -File \"{scriptPath}\"" : $"\"{scriptPath}\"",
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
        string scriptPath = Path.Combine("scripts", scriptName);

        if (!File.Exists(scriptPath))
        {
            AnsiConsole.MarkupLine($"[red]Wizard script not found:[/] {scriptPath}");
            return 1;
        }

        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "powershell.exe" : "bash",
            Arguments = isWindows ? $"-ExecutionPolicy Bypass -File \"{scriptPath}\"" : $"\"{scriptPath}\"",
            UseShellExecute = false
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit();
        return proc?.ExitCode ?? 0;
    }
}

public class WizardDecommissionCommand : Command
{
    public override int Execute(CommandContext context)
    {
        bool isWindows = OperatingSystem.IsWindows();
        string scriptName = isWindows ? "decommission_service_wizard.ps1" : "decommission_service_wizard.sh";
        string scriptPath = Path.Combine("scripts", scriptName);

        if (!File.Exists(scriptPath))
        {
            AnsiConsole.MarkupLine($"[red]Wizard script not found:[/] {scriptPath}");
            return 1;
        }

        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "powershell.exe" : "bash",
            Arguments = isWindows ? $"-ExecutionPolicy Bypass -File \"{scriptPath}\"" : $"\"{scriptPath}\"",
            UseShellExecute = false
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit();
        return proc?.ExitCode ?? 0;
    }
}
