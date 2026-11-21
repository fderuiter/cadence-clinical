using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class PosixScriptProcessHelperTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"posix-script-helper-{Guid.NewGuid():N}");

    public PosixScriptProcessHelperTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void StartWithPosixScriptFallback_ShebanglessPosixShimOnPath_ExecutesViaSh()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string binDir = Path.Combine(_tempDir, "bin");
        Directory.CreateDirectory(binDir);
        string shimPath = Path.Combine(binDir, "pnpm");

        // Mirrors npm's pnpm 10/11 POSIX wrapper: intentionally shebang-less so direct execve returns ENOEXEC (8).
        File.WriteAllText(shimPath, "# Shebang-less on purpose\necho \"shim-args:$*\"\n");
        File.SetUnixFileMode(
            shimPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var env = new Dictionary<string, string>
        {
            ["PATH"] = $"{binDir}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}",
        };
        var psi = new ProcessStartInfo("pnpm")
        {
            WorkingDirectory = _tempDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--version");
        psi.Environment["PATH"] = env["PATH"];

        using var process = PosixScriptProcessHelper.StartWithPosixScriptFallback(psi, env);
        process.Should().NotBeNull();
        string stdout = process!.StandardOutput.ReadToEnd().Trim();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().Be(0, stderr);
        stdout.Should().Be("shim-args:--version");
    }

    [Fact]
    public void ResolveExecutablePath_FindsBareToolInOverridePath_AndRelativeScript()
    {
        string binDir = Path.Combine(_tempDir, "tools");
        Directory.CreateDirectory(binDir);
        string toolPath = Path.Combine(binDir, "custom-tool");
        File.WriteAllText(toolPath, "echo ok\n");

        var env = new Dictionary<string, string> { ["PATH"] = binDir };

        PosixScriptProcessHelper.ResolveExecutablePath("custom-tool", _tempDir, env)
            .Should().Be(toolPath);
        PosixScriptProcessHelper.ResolveExecutablePath(Path.Combine("tools", "custom-tool"), _tempDir)
            .Should().Be(toolPath);
        PosixScriptProcessHelper.ResolveExecutablePath("nonexistent-tool", _tempDir, env)
            .Should().BeNull();
    }
}
