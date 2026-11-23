using System.CommandLine;

using static Cadence.Cli.Commands.CommandRegistrationHelpers;

namespace Cadence.Cli.Commands;

/// <summary>Registers top-level developer workflow commands (<c>doctor</c>, <c>check</c>, <c>test</c>, <c>ci</c>, <c>rulesets</c>, <c>dev</c>, <c>fix</c>).</summary>
internal static class CoreWorkflowCommands
{
    public static void Register(RootCommand root)
    {
        AddCommand(root, "doctor", "Validates system dependencies and local datastores", DoctorCommand.Configure);
        AddCommand(root, "check", "Runs architecture sentinels and quality gates", CheckCommand.Configure);
        AddCommand(root, "test", "Runs fast unit tests or testcontainer integration suites", TestCommand.Configure);
        AddCommand(root, "ci", "Runs the pull-request jobs of .github/workflows/ci.yml locally for pre-push CI parity", LocalCiCommand.Configure);
        AddCommand(root, "rulesets", "Validates CI Gate coverage and .github/rulesets/*.json (when present) and syncs rulesets via gh api", RulesetsCommand.Configure);
        AddCommand(root, "dev", "Starts the .NET Aspire AppHost with all local services and datastores", DevCommand.Execute);
        AddCommand(root, "fix", "Executes self-healing formatting, index sync, schema and OpenAPI pipeline", ToolCommands.ConfigureFix);
    }
}
