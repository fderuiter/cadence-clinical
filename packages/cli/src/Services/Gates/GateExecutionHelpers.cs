using System;
using System.ComponentModel;
using System.Diagnostics;

using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services.Gates;

/// <summary>Shared Native AOT-safe process execution helpers used by quality gate slices.</summary>
internal static class GateExecutionHelpers
{
    public static bool IsDotnetAvailable() =>
        DetectSecretsGateService.FindExecutableOnPath("dotnet", Environment.GetEnvironmentVariable("PATH")) is not null;

    public static int RunQuietProcess(string workingDirectory, string fileName, params string[] arguments)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return 1;
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            _ = stderrTask.GetAwaiter().GetResult();
            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            Console.WriteLine($"[ERROR] Could not run {fileName}: {ex.Message}");
            return 1;
        }
    }

    public static int RunProcess(string workingDirectory, string fileName, params string[] arguments)
    {
        var psi = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            Console.WriteLine($"[ERROR] Could not run {fileName}: {ex.Message}");
            return 1;
        }
    }
}
