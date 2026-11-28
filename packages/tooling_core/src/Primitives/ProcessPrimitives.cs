using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

using Cadence.ToolingCore.Services;

namespace Cadence.ToolingCore.Primitives;

internal readonly record struct ProcessOutput(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// Shared AOT-safe subprocess execution helpers built over <see cref="PosixProcessLauncher"/>.
/// </summary>
internal static class ProcessPrimitives
{
    public static ProcessStartInfo CreateStartInfo(
        string fileName,
        string? workingDirectory = null,
        IEnumerable<string>? arguments = null,
        bool redirectInput = false)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        if (redirectInput)
        {
            psi.StandardInputEncoding = WorkspacePrimitives.Utf8NoBom;
        }

        if (arguments is not null)
        {
            foreach (string arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        return psi;
    }

    public static ProcessOutput RunCaptured(
        ProcessStartInfo startInfo,
        string? standardInput = null,
        int? timeoutMilliseconds = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        using Process? proc = PosixProcessLauncher.Start(startInfo);
        if (proc is null)
        {
            return new ProcessOutput(-1, string.Empty, $"Failed to start process '{startInfo.FileName}'.");
        }

        Task<string> stdoutTask = startInfo.RedirectStandardOutput
            ? proc.StandardOutput.ReadToEndAsync()
            : Task.FromResult(string.Empty);
        Task<string> stderrTask = startInfo.RedirectStandardError
            ? proc.StandardError.ReadToEndAsync()
            : Task.FromResult(string.Empty);

        if (startInfo.RedirectStandardInput)
        {
            if (standardInput is not null)
            {
                proc.StandardInput.Write(standardInput);
            }

            proc.StandardInput.Close();
        }

        if (timeoutMilliseconds is int timeoutMs)
        {
            if (!proc.WaitForExit(timeoutMs))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process exited between WaitForExit and Kill.
                }

                return new ProcessOutput(-1, string.Empty, $"Process '{startInfo.FileName}' timed out after {timeoutMs}ms.");
            }

            proc.WaitForExit();
        }
        else
        {
            proc.WaitForExit();
        }

        Task.WaitAll(stdoutTask, stderrTask);
        return new ProcessOutput(proc.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }

    public static bool TryRunCaptured(
        string fileName,
        string? workingDirectory,
        IEnumerable<string> arguments,
        out ProcessOutput output,
        string? standardInput = null,
        int? timeoutMilliseconds = null)
    {
        try
        {
            var psi = CreateStartInfo(fileName, workingDirectory, arguments, redirectInput: standardInput is not null);
            output = RunCaptured(psi, standardInput, timeoutMilliseconds);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            output = new ProcessOutput(-1, string.Empty, ex.Message);
            return false;
        }
    }
}
