using System;
using System.Diagnostics;
using System.IO;

using Cadence.Cli.Services;

using FluentAssertions;

namespace Cadence.UnitTests.Packages.Gates;

/// <summary>
/// Disposable temporary repository workspace used by domain quality-gate fixture slices to construct minimal failing
/// repositories without mutating the real checkout.
/// </summary>
internal sealed class QualityGateFixtureWorkspace : IDisposable
{
    // Built at run time so the repository's own security, query-linter and RTM gates do not report test fixture files.
    public static readonly string PrivateKeyHeader = "-----BEGIN " + "PRIVATE KEY-----";
    public static readonly string BooleanComparison = "== " + "true";
    public static readonly string RequirementAttribute = "[GxP" + "Requirement(\"PRD-NOPE-999\")]";

    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"quality-gate-fixture-{Guid.NewGuid():N}");

    public QualityGateFixtureWorkspace()
    {
        Directory.CreateDirectory(Root);
    }

    public GateContext Context(
        string[]? files = null,
        bool preCommit = false,
        string? baseRef = null,
        string? pushBefore = null) =>
        new(Root, files, preCommit, BaseRef: baseRef, PushBefore: pushBefore);

    public static string Region(string body) =>
        $"{GateDocs.BeginMarker}\n\n{body.TrimEnd('\n')}\n\n{GateDocs.EndMarker}\n";

    public void WriteDotnetProject(string editorconfig, string source)
    {
        Write(".editorconfig", editorconfig);
        Write(
            "Fixture.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        Write("Bad.cs", source);
        Write(
            "Cadence.slnx",
            "<Solution>\n  <Project Path=\"Fixture.csproj\" />\n</Solution>\n");
    }

    public void WriteBrokenTestProject(string relativeCsprojPath)
    {
        string projectDir = Path.GetDirectoryName(relativeCsprojPath)!.Replace('\\', '/');
        Write(
            relativeCsprojPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <IsTestProject>true</IsTestProject>\n  </PropertyGroup>\n</Project>\n");
        Write(
            $"{projectDir}/Broken.cs",
            "namespace BrokenFixture;\n\npublic static class Broken\n{\n    public static int Fail( => 1;\n}\n");
    }

    public void CommitOnMainThenBranch()
    {
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "fixture@example.invalid");
        Git("config", "user.name", "Fixture");
        Git("config", "commit.gpgsign", "false");
        Git("add", "-A");
        Git("commit", "-q", "-m", "main");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
        Git("checkout", "-q", "-b", "feature");
    }

    public void Write(string relativePath, string content)
    {
        string path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        string error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} failed: {error}");
    }

    public void Dispose()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        // git marks its object files read-only.
        foreach (var file in new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(Root, recursive: true);
    }
}
