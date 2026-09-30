using System;
using System.Diagnostics;
using System.IO;

using Cadence.Cli.Services;

using Spectre.Console;

namespace Cadence.Cli.Commands;

public static class WizardCommands
{
    /// <summary>Runs <c>scripts/&lt;scriptBaseName&gt;.sh</c>, or the <c>.ps1</c> variant on Windows.</summary>
    public static int Run(string scriptBaseName)
    {
        bool isWindows = OperatingSystem.IsWindows();
        string scriptName = isWindows ? $"{scriptBaseName}.ps1" : $"{scriptBaseName}.sh";
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string scriptPath = Path.Combine(repoRoot, "scripts", scriptName);

        if (!File.Exists(scriptPath))
        {
            AnsiConsole.MarkupLine($"[red]Wizard script not found:[/] {Markup.Escape(scriptPath)}");
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
