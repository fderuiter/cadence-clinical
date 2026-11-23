using System;
using System.CommandLine;

namespace Cadence.Cli.Commands;

/// <summary>Shared Native AOT-safe helpers for registering CLI commands and subcommand branches.</summary>
internal static class CommandRegistrationHelpers
{
    /// <summary>Adds a command without options or arguments that runs <paramref name="execute"/>.</summary>
    public static void AddCommand(Command parent, string name, string description, Func<int> execute) =>
        AddCommand(parent, name, description, command => command.SetAction(_ => execute()));

    /// <summary>Adds a command whose options, arguments and action are set by <paramref name="configure"/>.</summary>
    public static void AddCommand(Command parent, string name, string description, Action<Command> configure)
    {
        var command = new Command(name, description);
        configure(command);
        parent.Subcommands.Add(command);
    }

    /// <summary>Adds a group of subcommands. Invoking the branch alone reports that a subcommand is required.</summary>
    public static Command AddBranch(Command parent, string name, string description)
    {
        var branch = new Command(name, description);
        parent.Subcommands.Add(branch);
        return branch;
    }
}
