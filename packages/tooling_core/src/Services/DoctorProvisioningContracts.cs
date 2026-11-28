using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.ToolingCore.Services;

public enum ProvisioningPlatform
{
    Linux,
    MacOS,
    Windows,
    Unsupported,
}

public sealed record DoctorProvisioningOptions(
    string RepoRoot,
    bool Minimal = false,
    bool LintersOnly = false);

public sealed record DoctorProvisioningStepResult(
    string Step,
    bool Succeeded,
    bool Blocking,
    string Message);

public sealed record DoctorProvisioningResult(
    bool Succeeded,
    IReadOnlyList<DoctorProvisioningStepResult> Steps);

public sealed record ProcessExecutionResult(
    int ExitCode,
    string StdOut,
    string StdErr)
{
    public string CombinedOutput
    {
        get
        {
            if (string.IsNullOrWhiteSpace(StdOut))
            {
                return StdErr;
            }

            if (string.IsNullOrWhiteSpace(StdErr))
            {
                return StdOut;
            }

            return $"{StdOut}\n{StdErr}";
        }
    }
}

public interface IDoctorProvisioningRuntime
{
    ProvisioningPlatform Platform { get; }

    Architecture CpuArchitecture { get; }

    string? GetEnvironmentVariable(string name);

    string? ResolveExecutable(string toolName, string workingDirectory);

    ProcessExecutionResult RunProcess(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int timeoutMs = 300_000);

    void DownloadFile(string url, string destinationPath);

    void SetPosixExecutablePermissions(string filePath);
}

public sealed class DefaultDoctorProvisioningRuntime : IDoctorProvisioningRuntime
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    public ProvisioningPlatform Platform
    {
        get
        {
            if (OperatingSystem.IsLinux())
            {
                return ProvisioningPlatform.Linux;
            }

            if (OperatingSystem.IsMacOS())
            {
                return ProvisioningPlatform.MacOS;
            }

            if (OperatingSystem.IsWindows())
            {
                return ProvisioningPlatform.Windows;
            }

            return ProvisioningPlatform.Unsupported;
        }
    }

    public Architecture CpuArchitecture => RuntimeInformation.OSArchitecture;

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string? ResolveExecutable(string toolName, string workingDirectory) =>
        PosixProcessLauncher.ResolveExecutablePath(toolName, workingDirectory);

    public ProcessExecutionResult RunProcess(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int timeoutMs = 300_000)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        ConfigureDotnetTelemetryOptOut(psi);

        try
        {
            using var process = PosixProcessLauncher.Start(psi);
            if (process is null)
            {
                return new ProcessExecutionResult(1, string.Empty, $"Failed to start '{fileName}'.");
            }

            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill(entireProcessTree: true);
                return new ProcessExecutionResult(124, string.Empty, $"Timed out running '{fileName}'.");
            }

            string stdOut = stdOutTask.GetAwaiter().GetResult().Trim();
            string stdErr = stdErrTask.GetAwaiter().GetResult().Trim();
            return new ProcessExecutionResult(process.ExitCode, stdOut, stdErr);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return new ProcessExecutionResult(127, string.Empty, ex.Message);
        }
    }

    public void DownloadFile(string url, string destinationPath)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid download URL '{url}'.");
        }

        string? parentDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        if (uri.IsFile)
        {
            string localSource = uri.LocalPath;
            if (!File.Exists(localSource))
            {
                throw new FileNotFoundException($"Local download source '{localSource}' was not found.", localSource);
            }

            File.Copy(localSource, destinationPath, overwrite: true);
            return;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported URI scheme '{uri.Scheme}' in '{url}'.");
        }

        Exception? lastError = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = SharedHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                using var networkStream = response.Content.ReadAsStream();
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                networkStream.CopyTo(fileStream);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                lastError = ex;
                if (attempt < 3)
                {
                    Thread.Sleep(250 * attempt);
                }
            }
        }

        throw new InvalidOperationException($"Failed to download '{url}' after 3 attempts: {lastError?.Message}", lastError);
    }

    public void SetPosixExecutablePermissions(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            filePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    internal static void ConfigureDotnetTelemetryOptOut(ProcessStartInfo psi)
    {
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["UseSharedCompilation"] = "false";
        psi.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
    }
}
