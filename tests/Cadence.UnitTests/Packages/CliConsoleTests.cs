using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Cadence.Cli.Services;

using FluentAssertions;

using Spectre.Console;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// The CLI prints its results when it has no terminal (#5417). With standard output redirected and <c>TERM=linux</c>,
/// Spectre.Console measured the console as -1 columns wide and dropped every markup line, so a failing pre-commit gate
/// printed only the banner.
/// </summary>
public sealed class CliConsoleTests
{
    [Fact]
    public void PlainConsole_PrintsGateFailuresAsPlainText()
    {
        using var output = new StringWriter();
        var console = CliConsole.Create(output, outputRedirected: true, detected: AnsiConsole.Console);

        console.MarkupLine($"[red]Gate 6 ({Markup.Escape("adrs")}) failed![/]");
        console.MarkupLine("\n[bold red]1 of 26 quality gates failed.[/]");
        console.MarkupLine("[red]Error: File 'x.md' has no '# ADR-NNNN: Title' heading on line 1 (found '# XDR-2263: PostgreSQL Relational Database').[/]");

        string text = output.ToString();
        text.Should().Contain("Gate 6 (adrs) failed!");
        text.Should().Contain("1 of 26 quality gates failed.");
        text.Should().Contain("Error: File 'x.md' has no '# ADR-NNNN: Title' heading on line 1 (found '# XDR-2263: PostgreSQL Relational Database').",
            "plain output is wide enough that an error line in a log does not wrap");
        text.Should().NotContain("\u001b", "redirected output carries no ANSI escape codes");
        console.Profile.Capabilities.Interactive.Should().BeFalse("status and progress displays must degrade to plain lines");
    }

    [Fact]
    public void AnsiTerminal_KeepsTheDetectedConsole()
    {
        using var output = new StringWriter();
        var detected = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.EightBit,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
            Out = new AnsiConsoleOutput(output),
        });

        CliConsole.Create(output, outputRedirected: false, detected).Should().BeSameAs(detected);
    }

    [Fact]
    public void TerminalWithoutAnsi_GetsThePlainConsole()
    {
        using var output = new StringWriter();
        var detected = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
            Out = new AnsiConsoleOutput(output),
        });

        var console = CliConsole.Create(output, outputRedirected: false, detected);

        console.Should().NotBeSameAs(detected);
        console.Profile.Capabilities.Ansi.Should().BeFalse();
        console.Profile.Width.Should().Be(CliConsole.PlainWidth);
    }

    /// <summary>
    /// The real path: the CLI as a process with standard output redirected and <c>TERM=linux</c>, whose terminfo
    /// entry gives .NET no column count, including under <c>GITHUB_ACTIONS=true</c> where Spectre's built-in GitHub
    /// enricher would otherwise force ANSI escape codes back on. Before the fix the failure line was dropped and only
    /// the banner printed.
    /// </summary>
    [Theory]
    [InlineData("linux", false)]
    [InlineData(null, false)]
    [InlineData("linux", true)]
    [InlineData(null, true)]
    public async Task Cli_WithRedirectedOutput_PrintsAFailingCheck(string? term, bool githubActions)
    {
        string cli = Path.Combine(AppContext.BaseDirectory, "cadence.dll");
        File.Exists(cli).Should().BeTrue($"the test project references the CLI, so {cli} is built next to the tests");

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        psi.ArgumentList.Add(cli);
        psi.ArgumentList.Add("check");
        psi.ArgumentList.Add("no-such-gate");

        psi.Environment.Remove("TERM");
        if (term is not null)
        {
            psi.Environment["TERM"] = term;
        }

        if (githubActions)
        {
            psi.Environment["CI"] = "true";
            psi.Environment["GITHUB_ACTIONS"] = "true";
        }
        else
        {
            psi.Environment.Remove("CI");
            psi.Environment.Remove("GITHUB_ACTIONS");
        }

        using var process = Process.Start(psi)!;
        process.StandardInput.Close();
        var ct = TestContext.Current.CancellationToken;
        var stderr = process.StandardError.ReadToEndAsync(ct);
        string stdout = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        process.ExitCode.Should().Be(1, await stderr);
        stdout.Should().Contain("Unknown quality gate 'no-such-gate'.");
        stdout.Should().Contain("Executing Cadence Quality Sentinels & Policy Gates...");
        stdout.Should().NotContain("\u001b");
    }
}
