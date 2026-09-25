using System;
using System.Diagnostics;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class GxPComplianceService
{
    public static int SyncCompliance(bool dryRun = false, bool commit = false)
    {
        AnsiConsole.MarkupLine("[cyan]Synchronizing GxP Compliance Documentation (PRD-SYS-053)...[/]\n");

        var args = new System.Collections.Generic.List<string>();
        if (dryRun)
        {
            args.Add("--dry-run");
        }
        if (commit)
        {
            args.Add("--commit");
        }

        var argStr = string.Join(" ", args);
        var pythonExe = "python3";
        if (System.IO.File.Exists(".venv/bin/python3"))
        {
            pythonExe = ".venv/bin/python3";
        }
        else if (System.IO.File.Exists(".venv/bin/python"))
        {
            pythonExe = ".venv/bin/python";
        }

        var psi = new ProcessStartInfo(pythonExe, string.IsNullOrEmpty(argStr) ? "scripts/sync_gxp.py" : $"scripts/sync_gxp.py {argStr}")
        {
            UseShellExecute = false
        };

        using var proc = Process.Start(psi);
        if (proc != null)
        {
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                AnsiConsole.MarkupLine("[red]GxP compliance synchronization failed![/]");
                return proc.ExitCode;
            }
        }

        AnsiConsole.MarkupLine("\n[bold green]GxP Compliance Synchronization Complete.[/]");
        return 0;
    }

    public static int ValidateCompliance()
    {
        return SyncCompliance(dryRun: true, commit: false);
    }
}
