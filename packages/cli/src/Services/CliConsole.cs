using System;
using System.IO;

using Spectre.Console;

namespace Cadence.Cli.Services;

/// <summary>
/// Chooses the Spectre.Console console the CLI writes through (#5417). Spectre takes its width from
/// <see cref="Console.WindowWidth"/>, which is <c>-1</c> when standard output is redirected and <c>TERM</c> names a
/// terminal whose terminfo entry has no column count (for example <c>TERM=linux</c> in an agent shell or a git hook).
/// Spectre replaces a width of 0 with 80 but keeps <c>-1</c>, and a markup line rendered at a negative width comes out
/// empty, so every result, error and "Gate N failed!" line was dropped and only the banner printed. Redirected output,
/// or a terminal without ANSI support, therefore gets a plain console: no escape codes, no colours, not interactive
/// (Spectre's status and progress displays then print plain lines), and a fixed width.
/// </summary>
public static class CliConsole
{
    /// <summary>Line width of plain output: wide enough that most error lines in a log do not wrap.</summary>
    public const int PlainWidth = 120;

    /// <summary>Width used on a terminal that reports no usable width.</summary>
    public const int FallbackTerminalWidth = 80;

    /// <summary>Replaces <see cref="AnsiConsole.Console"/> with the console <see cref="Create"/> picks for this process.</summary>
    public static void ConfigureForCurrentProcess() =>
        AnsiConsole.Console = Create(Console.Out, Console.IsOutputRedirected, AnsiConsole.Console);

    /// <summary>
    /// Returns a plain console over <paramref name="output"/> when the output is redirected or
    /// <paramref name="detected"/> (Spectre's own choice) has no ANSI support; otherwise returns
    /// <paramref name="detected"/>, with its width repaired when the terminal reported none.
    /// </summary>
    public static IAnsiConsole Create(TextWriter output, bool outputRedirected, IAnsiConsole detected)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(detected);

        if (outputRedirected || !detected.Profile.Capabilities.Ansi)
        {
            return CreatePlain(output);
        }

        if (detected.Profile.Width <= 0)
        {
            detected.Profile.Width = FallbackTerminalWidth;
        }

        return detected;
    }

    /// <summary>A console that renders markup as plain text of <see cref="PlainWidth"/> columns to <paramref name="output"/>.</summary>
    public static IAnsiConsole CreatePlain(TextWriter output)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(output),
        });
        console.Profile.Width = PlainWidth;
        return console;
    }
}
